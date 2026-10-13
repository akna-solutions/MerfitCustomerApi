using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using MerfitCustomerApi.Business.Common;
using MerfitCustomerApi.Business.Interfaces.Services;
using MerfitCustomerApi.Domain.Entities.Enums;
using Microsoft.Extensions.Options;

namespace MerfitCustomerApi.Business.Services.NutritionAssistant;

/// <summary>
/// Groq (OpenAI uyumlu Chat Completions API) saglayicisi. IHttpClientFactory ile typed client olarak
/// kaydedilir. Anahtar NutritionAssistant__Groq__ApiKey ya da GROQ_API_KEY ortam degiskeninden okunur,
/// yalnizca Authorization basliginda gider ve loglanmaz.
/// </summary>
public sealed class GroqNutritionAiProvider : INutritionAiProvider
{
    public const string ApiKeyEnvironmentVariable = "GROQ_API_KEY";

    private readonly HttpClient _http;
    private readonly NutritionAssistantOptions _options;
    private readonly NutritionAiProviderSettings _settings;
    private readonly string? _apiKey;

    public GroqNutritionAiProvider(HttpClient http, IOptions<NutritionAssistantOptions> options)
    {
        _http = http;
        _options = options.Value;
        _settings = _options.Groq;
        _apiKey = NutritionAiHttp.ResolveApiKey(_settings, ApiKeyEnvironmentVariable);
    }

    public string Name => NutritionAiProviderNames.Groq;
    public bool IsConfigured => _apiKey is not null && !string.IsNullOrWhiteSpace(_settings.Model) && _http.BaseAddress is not null;
    public bool IsPaid => _settings.Paid;
    public string Model => _settings.Model;

    public async Task<NutritionAssistantModelResult> CompleteAsync(NutritionAssistantModelRequest request, CancellationToken cancellationToken = default)
    {
        if (!IsConfigured) throw new NutritionAiProviderException(Name, NutritionAiFailureKind.Rejected, errorCode: "not_configured");

        var messages = new List<ChatMessage>
        {
            new("system", request.Instructions),
            new("system", request.Context),
        };
        messages.AddRange(request.Messages.Select(m => new ChatMessage(m.Role == NutritionAssistantMessageRole.User ? "user" : "assistant", m.Content)));

        var hasReasoning = !string.IsNullOrWhiteSpace(_settings.ReasoningEffort);
        var body = new ChatRequest
        {
            Model = _settings.Model,
            Messages = messages,
            MaxCompletionTokens = Math.Max(128, _options.MaxOutputTokens),
            Temperature = _options.Temperature,
            ReasoningEffort = hasReasoning ? _settings.ReasoningEffort!.Trim().ToLowerInvariant() : null,
            // Dusunme metni yanita eklenmez (kullaniciya gosterilmez, aktarim kucuk kalir).
            IncludeReasoning = hasReasoning ? false : null,
        };

        using var message = new HttpRequestMessage(HttpMethod.Post, "chat/completions")
        {
            Content = JsonContent.Create(body, options: NutritionAiHttp.Json),
        };
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Max(5, _options.TimeoutSeconds)));
        try
        {
            using var response = await _http.SendAsync(message, timeout.Token);
            if (!response.IsSuccessStatusCode)
            {
                throw await NutritionAiHttp.ToExceptionAsync(Name, response, timeout.Token);
            }

            var result = await response.Content.ReadFromJsonAsync<ChatResponse>(NutritionAiHttp.Json, timeout.Token)
                ?? throw new NutritionAiProviderException(Name, NutritionAiFailureKind.Transient, (int)response.StatusCode, errorCode: "empty_body");
            var choice = result.Choices?.FirstOrDefault();
            var finish = choice?.FinishReason;
            return new NutritionAssistantModelResult(
                choice?.Message?.Content?.Trim() ?? string.Empty,
                result.Usage?.PromptTokens ?? 0,
                result.Usage?.CompletionTokens ?? 0,
                finish,
                string.IsNullOrWhiteSpace(result.Model) ? _settings.Model : result.Model,
                Refused: string.Equals(finish, "content_filter", StringComparison.OrdinalIgnoreCase),
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

    private sealed record ChatMessage(
        [property: JsonPropertyName("role")] string Role,
        [property: JsonPropertyName("content")] string Content);

    private sealed class ChatRequest
    {
        [JsonPropertyName("model")] public string Model { get; init; } = string.Empty;
        [JsonPropertyName("messages")] public List<ChatMessage> Messages { get; init; } = new();
        [JsonPropertyName("max_completion_tokens")] public int MaxCompletionTokens { get; init; }
        [JsonPropertyName("temperature")] public double Temperature { get; init; }
        [JsonPropertyName("reasoning_effort")] public string? ReasoningEffort { get; init; }
        [JsonPropertyName("include_reasoning")] public bool? IncludeReasoning { get; init; }
        [JsonPropertyName("stream")] public bool Stream { get; init; }
    }

    private sealed class ChatResponse
    {
        [JsonPropertyName("model")] public string? Model { get; init; }
        [JsonPropertyName("choices")] public List<Choice>? Choices { get; init; }
        [JsonPropertyName("usage")] public UsageInfo? Usage { get; init; }
    }

    private sealed class Choice
    {
        [JsonPropertyName("message")] public ResponseMessage? Message { get; init; }
        [JsonPropertyName("finish_reason")] public string? FinishReason { get; init; }
    }

    private sealed class ResponseMessage
    {
        [JsonPropertyName("content")] public string? Content { get; init; }
    }

    private sealed class UsageInfo
    {
        [JsonPropertyName("prompt_tokens")] public int PromptTokens { get; init; }
        [JsonPropertyName("completion_tokens")] public int CompletionTokens { get; init; }
    }
}
