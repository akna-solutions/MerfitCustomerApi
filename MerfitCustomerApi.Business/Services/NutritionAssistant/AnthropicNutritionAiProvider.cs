using Anthropic;
using Anthropic.Exceptions;
using Anthropic.Models.Beta.Messages;
using MerfitCustomerApi.Business.Common;
using MerfitCustomerApi.Business.Interfaces.Services;
using MerfitCustomerApi.Domain.Entities.Enums;
using Microsoft.Extensions.Options;

namespace MerfitCustomerApi.Business.Services.NutritionAssistant;

/// <summary>
/// Anthropic Claude (Messages API, resmi Anthropic .NET SDK) saglayicisi. Ucretli saglayicidir; yalnizca
/// Provider olarak acikca secildiginde ya da AllowPaidFallback true iken yedek olarak kullanilir.
/// Anahtar NutritionAssistant__Anthropic__ApiKey ya da ANTHROPIC_API_KEY ortam degiskeninden okunur ve loglanmaz.
/// </summary>
public sealed class AnthropicNutritionAiProvider : INutritionAiProvider
{
    public const string ApiKeyEnvironmentVariable = "ANTHROPIC_API_KEY";

    private readonly NutritionAssistantOptions _options;
    private readonly NutritionAiProviderSettings _settings;
    private readonly Lazy<AnthropicClient>? _client;

    public AnthropicNutritionAiProvider(IOptions<NutritionAssistantOptions> options)
    {
        _options = options.Value;
        _settings = _options.Anthropic;

        var apiKey = NutritionAiHttp.ResolveApiKey(_settings, ApiKeyEnvironmentVariable);
        if (apiKey is not null)
        {
            var timeout = TimeSpan.FromSeconds(Math.Max(5, _options.TimeoutSeconds));
            // Yeniden deneme ve yedek saglayici kararlarini NutritionAiProviderRouter verir.
            _client = new Lazy<AnthropicClient>(() => string.IsNullOrWhiteSpace(_settings.BaseUrl)
                ? new AnthropicClient { ApiKey = apiKey, Timeout = timeout, MaxRetries = 0 }
                : new AnthropicClient { ApiKey = apiKey, Timeout = timeout, MaxRetries = 0, BaseUrl = _settings.BaseUrl });
        }
    }

    public string Name => NutritionAiProviderNames.Anthropic;
    public bool IsConfigured => _client is not null && !string.IsNullOrWhiteSpace(_settings.Model);
    public bool IsPaid => _settings.Paid;
    public string Model => _settings.Model;

    public async Task<NutritionAssistantModelResult> CompleteAsync(NutritionAssistantModelRequest request, CancellationToken cancellationToken = default)
    {
        if (_client is null)
        {
            throw new NutritionAiProviderException(Name, NutritionAiFailureKind.Rejected, errorCode: "not_configured");
        }

        var parameters = new MessageCreateParams
        {
            Model = _settings.Model,
            MaxTokens = Math.Max(256, _options.MaxOutputTokens),
            System = new List<BetaTextBlockParam>
            {
                // Sabit talimatlar once ve onbellekli; degisen baglam sonra.
                new() { Text = request.Instructions, CacheControl = new BetaCacheControlEphemeral() },
                new() { Text = request.Context },
            },
            OutputConfig = new BetaOutputConfig { Effort = ParseEffort(_settings.ReasoningEffort) },
            Messages = request.Messages
                .Select(m => new BetaMessageParam
                {
                    Role = m.Role == NutritionAssistantMessageRole.User ? Role.User : Role.Assistant,
                    Content = m.Content,
                })
                .ToList(),
        };
        // Model reddederse istek sunucu tarafinda uygun modele yonlendirilir.
        parameters = parameters with
        {
            Betas = ["server-side-fallback-2026-07-01"],
            Fallbacks = new Default(),
        };

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Max(5, _options.TimeoutSeconds) + 2));

        BetaMessage response;
        try
        {
            response = await _client.Value.Beta.Messages.Create(parameters, timeout.Token);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException ex)
        {
            throw new NutritionAiProviderException(Name, NutritionAiFailureKind.Timeout, innerException: ex);
        }
        catch (AnthropicRateLimitException ex)
        {
            throw new NutritionAiProviderException(Name, NutritionAiFailureKind.RateLimited, 429, innerException: ex);
        }
        catch (Anthropic5xxException ex)
        {
            throw new NutritionAiProviderException(Name, NutritionAiFailureKind.Transient, innerException: ex);
        }
        catch (AnthropicApiException ex)
        {
            // 400/401/403/404: yapilandirma hatasi (anahtar, model adi).
            throw new NutritionAiProviderException(Name, NutritionAiFailureKind.Rejected, innerException: ex);
        }
        catch (AnthropicIOException ex)
        {
            throw new NutritionAiProviderException(Name, NutritionAiFailureKind.Transient, innerException: ex);
        }

        var stopReason = response.StopReason?.Raw();
        var refused = string.Equals(stopReason, "refusal", StringComparison.OrdinalIgnoreCase);
        var text = string.Join("\n\n", response.Content
            .Select(b => b.Value)
            .OfType<BetaTextBlock>()
            .Select(t => t.Text.Trim())
            .Where(t => t.Length > 0));

        return new NutritionAssistantModelResult(
            text,
            (int)response.Usage.InputTokens + (int)(response.Usage.CacheReadInputTokens ?? 0) + (int)(response.Usage.CacheCreationInputTokens ?? 0),
            (int)response.Usage.OutputTokens,
            stopReason,
            string.IsNullOrEmpty(response.Model.Raw()) ? _settings.Model : response.Model.Raw(),
            refused,
            Name);
    }

    private static Effort ParseEffort(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "medium" => Effort.Medium,
        "high" => Effort.High,
        _ => Effort.Low,
    };
}
