using Anthropic;
using Anthropic.Exceptions;
using Anthropic.Models.Beta.Messages;
using MerfitCustomerApi.Business.Common;
using MerfitCustomerApi.Business.Interfaces.Services;
using MerfitCustomerApi.Domain.Entities.Enums;
using MerfitCustomerApi.Domain.Exceptions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MerfitCustomerApi.Business.Services.NutritionAssistant;

/// <summary>
/// Beslenme asistaninin Anthropic Claude (Messages API, resmi Anthropic .NET SDK) uygulamasi.
/// Anahtar yalnizca yapilandirmadan / ortam degiskeninden okunur ve loglanmaz. Saglayici hatalari ve
/// zaman asimi kullaniciya anlasilir bir 503 olarak doner; istemcinin iptal ettigi istek iptal olarak yayilir.
/// </summary>
public sealed class AnthropicNutritionAssistantModelClient : INutritionAssistantModelClient
{
    private const string UnavailableMessage = "Beslenme asistanı şu an yanıt veremiyor. Lütfen biraz sonra tekrar deneyin.";
    private const string TimeoutMessage = "Beslenme asistanının yanıtı gecikti. Lütfen tekrar deneyin.";
    private const string BusyMessage = "Beslenme asistanı şu an çok yoğun. Lütfen biraz sonra tekrar deneyin.";

    private readonly NutritionAssistantOptions _options;
    private readonly ILogger<AnthropicNutritionAssistantModelClient> _logger;
    private readonly Lazy<AnthropicClient>? _client;

    public AnthropicNutritionAssistantModelClient(IOptions<NutritionAssistantOptions> options, ILogger<AnthropicNutritionAssistantModelClient> logger)
    {
        _options = options.Value;
        _logger = logger;

        var apiKey = string.IsNullOrWhiteSpace(_options.ApiKey)
            ? Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY")
            : _options.ApiKey;
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            var timeout = TimeSpan.FromSeconds(Math.Max(5, _options.TimeoutSeconds));
            // Zaman asimi kullaniciyi bekletir; tek yeniden deneme yeterli.
            _client = new Lazy<AnthropicClient>(() => string.IsNullOrWhiteSpace(_options.BaseUrl)
                ? new AnthropicClient { ApiKey = apiKey, Timeout = timeout, MaxRetries = 1 }
                : new AnthropicClient { ApiKey = apiKey, Timeout = timeout, MaxRetries = 1, BaseUrl = _options.BaseUrl });
        }
    }

    public bool IsConfigured => _client is not null;

    public string Model => _options.Model;

    public async Task<NutritionAssistantModelResult> CompleteAsync(NutritionAssistantModelRequest request, CancellationToken cancellationToken = default)
    {
        if (_client is null)
        {
            throw new ExternalServiceUnavailableException(UnavailableMessage);
        }

        var parameters = new MessageCreateParams
        {
            Model = _options.Model,
            MaxTokens = Math.Max(256, _options.MaxOutputTokens),
            System = new List<BetaTextBlockParam>
            {
                // Sabit talimatlar once ve onbellekli; degisen baglam sonra.
                new() { Text = request.Instructions, CacheControl = new BetaCacheControlEphemeral() },
                new() { Text = request.Context },
            },
            OutputConfig = new BetaOutputConfig { Effort = ParseEffort(_options.Effort) },
            Messages = request.Messages
                .Select(m => new BetaMessageParam
                {
                    Role = m.Role == NutritionAssistantMessageRole.User ? Role.User : Role.Assistant,
                    Content = m.Content,
                })
                .ToList(),
        };
        if (_options.EnableRefusalFallback)
        {
            parameters = parameters with
            {
                Betas = ["server-side-fallback-2026-07-01"],
                Fallbacks = new Default(),
            };
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Max(5, _options.TimeoutSeconds) + 5));

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
            _logger.LogWarning(ex, "Anthropic istegi zaman asimina ugradi. Model: {Model}", _options.Model);
            throw new ExternalServiceUnavailableException(TimeoutMessage, ex);
        }
        catch (AnthropicRateLimitException ex)
        {
            _logger.LogWarning(ex, "Anthropic hiz siniri. Model: {Model}", _options.Model);
            throw new ExternalServiceUnavailableException(BusyMessage, ex);
        }
        catch (Anthropic5xxException ex)
        {
            _logger.LogWarning(ex, "Anthropic sunucu hatasi. Model: {Model}", _options.Model);
            throw new ExternalServiceUnavailableException(UnavailableMessage, ex);
        }
        catch (AnthropicApiException ex)
        {
            // 400/401/403/404: yapilandirma hatasi (anahtar, model adi). Icerik loglanmaz.
            _logger.LogError(ex, "Anthropic istegi reddedildi. Model: {Model}", _options.Model);
            throw new ExternalServiceUnavailableException(UnavailableMessage, ex);
        }
        catch (AnthropicIOException ex)
        {
            _logger.LogWarning(ex, "Anthropic'e ulasilamadi. Model: {Model}", _options.Model);
            throw new ExternalServiceUnavailableException(UnavailableMessage, ex);
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
            string.IsNullOrEmpty(response.Model.Raw()) ? _options.Model : response.Model.Raw(),
            refused);
    }

    private static Effort ParseEffort(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "medium" => Effort.Medium,
        "high" => Effort.High,
        _ => Effort.Low,
    };
}
