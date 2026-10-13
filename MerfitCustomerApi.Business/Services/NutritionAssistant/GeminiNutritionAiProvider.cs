using System.Net.Http.Json;
using System.Text.Json.Serialization;
using MerfitCustomerApi.Business.Common;
using MerfitCustomerApi.Business.Interfaces.Services;
using MerfitCustomerApi.Domain.Entities.Enums;
using Microsoft.Extensions.Options;

namespace MerfitCustomerApi.Business.Services.NutritionAssistant;

/// <summary>
/// Google Gemini (generateContent REST API) saglayicisi; istege bagli ikinci saglayici. Anahtar
/// NutritionAssistant__Gemini__ApiKey ya da GEMINI_API_KEY ortam degiskeninden okunur, yalnizca
/// x-goog-api-key basliginda gider (URL'de degil, boylece erisim loglarina dusmez).
///
/// Uyari: Gemini'nin ucretsiz katmaninda Google gonderilen icerigi urun gelistirme icin kullanabilir ve
/// insan incelemecilere gosterebilir; kisisel/saglik verisi icin ucretli katman tercih edilmelidir.
/// </summary>
public sealed class GeminiNutritionAiProvider : INutritionAiProvider
{
    public const string ApiKeyEnvironmentVariable = "GEMINI_API_KEY";

    private static readonly HashSet<string> BlockedFinishReasons = new(StringComparer.OrdinalIgnoreCase)
    {
        "SAFETY", "PROHIBITED_CONTENT", "BLOCKLIST", "SPII", "RECITATION",
    };

    private readonly HttpClient _http;
    private readonly NutritionAssistantOptions _options;
    private readonly NutritionAiProviderSettings _settings;
    private readonly string? _apiKey;

    public GeminiNutritionAiProvider(HttpClient http, IOptions<NutritionAssistantOptions> options)
    {
        _http = http;
        _options = options.Value;
        _settings = _options.Gemini;
        _apiKey = NutritionAiHttp.ResolveApiKey(_settings, ApiKeyEnvironmentVariable);
    }

    public string Name => NutritionAiProviderNames.Gemini;
    public bool IsConfigured => _apiKey is not null && !string.IsNullOrWhiteSpace(_settings.Model) && _http.BaseAddress is not null;
    public bool IsPaid => _settings.Paid;
    public string Model => _settings.Model;

    public async Task<NutritionAssistantModelResult> CompleteAsync(NutritionAssistantModelRequest request, CancellationToken cancellationToken = default)
    {
        if (!IsConfigured) throw new NutritionAiProviderException(Name, NutritionAiFailureKind.Rejected, errorCode: "not_configured");

        var body = new GenerateRequest
        {
            SystemInstruction = new Content { Parts = { new Part { Text = request.Instructions }, new Part { Text = request.Context } } },
            Contents = request.Messages
                .Select(m => new Content
                {
                    Role = m.Role == NutritionAssistantMessageRole.User ? "user" : "model",
                    Parts = { new Part { Text = m.Content } },
                })
                .ToList(),
            GenerationConfig = new GenerationConfig
            {
                MaxOutputTokens = Math.Max(128, _options.MaxOutputTokens),
                Temperature = _options.Temperature,
            },
        };

        using var message = new HttpRequestMessage(HttpMethod.Post, $"models/{Uri.EscapeDataString(_settings.Model)}:generateContent")
        {
            Content = JsonContent.Create(body, options: NutritionAiHttp.Json),
        };
        message.Headers.Add("x-goog-api-key", _apiKey);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Max(5, _options.TimeoutSeconds)));
        try
        {
            using var response = await _http.SendAsync(message, timeout.Token);
            if (!response.IsSuccessStatusCode)
            {
                throw await NutritionAiHttp.ToExceptionAsync(Name, response, timeout.Token);
            }

            var result = await response.Content.ReadFromJsonAsync<GenerateResponse>(NutritionAiHttp.Json, timeout.Token)
                ?? throw new NutritionAiProviderException(Name, NutritionAiFailureKind.Transient, (int)response.StatusCode, errorCode: "empty_body");
            var candidate = result.Candidates?.FirstOrDefault();
            var finish = candidate?.FinishReason;
            var text = string.Join("\n\n", (candidate?.Content?.Parts ?? new List<Part>())
                .Where(p => p.Thought != true && !string.IsNullOrWhiteSpace(p.Text))
                .Select(p => p.Text!.Trim()));
            var usage = result.UsageMetadata;
            return new NutritionAssistantModelResult(
                text,
                usage?.PromptTokenCount ?? 0,
                (usage?.CandidatesTokenCount ?? 0) + (usage?.ThoughtsTokenCount ?? 0),
                finish ?? result.PromptFeedback?.BlockReason,
                string.IsNullOrWhiteSpace(result.ModelVersion) ? _settings.Model : result.ModelVersion,
                Refused: result.PromptFeedback?.BlockReason is not null || (finish is not null && BlockedFinishReasons.Contains(finish)),
                Provider: Name);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException ex)
        {
            throw new NutritionAiProviderException(Name, NutritionAiFailureKind.Timeout, innerException: ex);
        }
        catch (HttpRequestException ex)
        {
            throw new NutritionAiProviderException(Name, NutritionAiFailureKind.Transient, errorCode: ex.HttpRequestError.ToString(), innerException: ex);
        }
        catch (System.Text.Json.JsonException ex)
        {
            throw new NutritionAiProviderException(Name, NutritionAiFailureKind.Transient, errorCode: "invalid_json", innerException: ex);
        }
    }

    private sealed class GenerateRequest
    {
        [JsonPropertyName("systemInstruction")] public Content? SystemInstruction { get; init; }
        [JsonPropertyName("contents")] public List<Content> Contents { get; init; } = new();
        [JsonPropertyName("generationConfig")] public GenerationConfig? GenerationConfig { get; init; }
    }

    private sealed class Content
    {
        [JsonPropertyName("role")] public string? Role { get; init; }
        [JsonPropertyName("parts")] public List<Part> Parts { get; init; } = new();
    }

    private sealed class Part
    {
        [JsonPropertyName("text")] public string? Text { get; init; }
        [JsonPropertyName("thought")] public bool? Thought { get; init; }
    }

    private sealed class GenerationConfig
    {
        [JsonPropertyName("maxOutputTokens")] public int MaxOutputTokens { get; init; }
        [JsonPropertyName("temperature")] public double Temperature { get; init; }
    }

    private sealed class GenerateResponse
    {
        [JsonPropertyName("candidates")] public List<Candidate>? Candidates { get; init; }
        [JsonPropertyName("promptFeedback")] public PromptFeedback? PromptFeedback { get; init; }
        [JsonPropertyName("usageMetadata")] public UsageMetadata? UsageMetadata { get; init; }
        [JsonPropertyName("modelVersion")] public string? ModelVersion { get; init; }
    }

    private sealed class Candidate
    {
        [JsonPropertyName("content")] public Content? Content { get; init; }
        [JsonPropertyName("finishReason")] public string? FinishReason { get; init; }
    }

    private sealed class PromptFeedback
    {
        [JsonPropertyName("blockReason")] public string? BlockReason { get; init; }
    }

    private sealed class UsageMetadata
    {
        [JsonPropertyName("promptTokenCount")] public int PromptTokenCount { get; init; }
        [JsonPropertyName("candidatesTokenCount")] public int CandidatesTokenCount { get; init; }
        [JsonPropertyName("thoughtsTokenCount")] public int ThoughtsTokenCount { get; init; }
    }
}
