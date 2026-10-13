using System.Diagnostics;
using MerfitCustomerApi.Business.Common;
using MerfitCustomerApi.Business.Interfaces.Services;
using MerfitCustomerApi.Domain.Exceptions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MerfitCustomerApi.Business.Services.NutritionAssistant;

/// <summary>
/// Beslenme asistaninin saglayici secimi ve dayaniklilik kurallari (INutritionAssistantModelClient):
/// - Aktif saglayici (Provider) once denenir; yapilandirilmissa yedek (FallbackProvider) sonra. Ucretli
///   saglayiciya otomatik gecis yalnizca AllowPaidFallback true ise yapilir.
/// - Saglayici basina eszamanli istek siniri; sira beklemesi QueueTimeoutSeconds ile sinirlidir.
/// - 429: Retry-After kisa ise bir kez beklenip tekrar denenir; uzunsa saglayici o sure boyunca atlanir
///   (her mesajda kotasi dolu saglayiciya tekrar tekrar istek gitmez).
/// - 5xx / baglanti hatasi: en fazla MaxRetries kez, artan kisa beklemeyle. Zaman asimi ve 4xx tekrar denenmez.
/// - Hicbiri yanit veremezse kullaniciya teknik ayrinti icermeyen 503 doner (biliniyorsa Retry-After ile).
/// Loglar ve metrikler yalnizca saglayici adi, durum kodu, hata kodu ve sure icerir.
/// </summary>
public sealed class NutritionAiProviderRouter : INutritionAssistantModelClient
{
    internal const string UnavailableMessage = "Beslenme asistanı şu an yanıt veremiyor. Lütfen biraz sonra tekrar dene.";
    internal const string BusyMessage = "Beslenme asistanı şu an çok yoğun. Lütfen biraz sonra tekrar dene.";
    internal const string TimeoutMessage = "Beslenme asistanının yanıtı gecikti. Lütfen tekrar dene.";

    private readonly IReadOnlyList<INutritionAiProvider> _chain;
    private readonly NutritionAiProviderState _state;
    private readonly NutritionAssistantOptions _options;
    private readonly NutritionAssistantMetrics _metrics;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<NutritionAiProviderRouter> _logger;

    public NutritionAiProviderRouter(
        IEnumerable<INutritionAiProvider> providers,
        NutritionAiProviderState state,
        IOptions<NutritionAssistantOptions> options,
        NutritionAssistantMetrics metrics,
        TimeProvider timeProvider,
        ILogger<NutritionAiProviderRouter> logger)
    {
        _state = state;
        _options = options.Value;
        _metrics = metrics;
        _timeProvider = timeProvider;
        _logger = logger;
        _chain = BuildChain(providers.ToList(), _options);
    }

    public bool IsConfigured => _chain.Count > 0;

    public string Model => _chain.FirstOrDefault()?.Model ?? string.Empty;

    /// <summary>Denenecek saglayicilar, sirayla (test ve tanilama icin).</summary>
    public IReadOnlyList<string> ProviderChain => _chain.Select(p => p.Name).ToList();

    internal static IReadOnlyList<INutritionAiProvider> BuildChain(IReadOnlyList<INutritionAiProvider> providers, NutritionAssistantOptions options)
    {
        INutritionAiProvider? Find(string? name) => string.IsNullOrWhiteSpace(name)
            ? null
            : providers.FirstOrDefault(p => string.Equals(p.Name, name.Trim(), StringComparison.OrdinalIgnoreCase) && p.IsConfigured);

        var chain = new List<INutritionAiProvider>();
        // Aktif saglayici ucretli olsa bile acikca secildigi icin kullanilir.
        if (Find(options.Provider) is { } primary) chain.Add(primary);
        if (Find(options.FallbackProvider) is { } fallback
            && !chain.Contains(fallback)
            && (!fallback.IsPaid || options.AllowPaidFallback))
        {
            chain.Add(fallback);
        }
        return chain;
    }

    public async Task<NutritionAssistantModelResult> CompleteAsync(NutritionAssistantModelRequest request, CancellationToken cancellationToken = default)
    {
        if (_chain.Count == 0)
        {
            throw new ExternalServiceUnavailableException(UnavailableMessage);
        }

        NutritionAiProviderException? last = null;
        TimeSpan? retryAfter = null;
        foreach (var provider in _chain)
        {
            var now = _timeProvider.GetUtcNow();
            if (_state.CoolingDownUntil(provider.Name, now) is { } until)
            {
                // Kotasi dolan saglayiciya bekleme suresi bitene kadar istek gonderilmez.
                _metrics.Record(provider.Name, "cooling_down");
                retryAfter = Min(retryAfter, until - now);
                last ??= new NutritionAiProviderException(provider.Name, NutritionAiFailureKind.RateLimited, 429, until - now, "cooling_down");
                continue;
            }

            var gate = _state.Gate(provider.Name);
            if (!await gate.WaitAsync(TimeSpan.FromSeconds(Math.Max(0, _options.QueueTimeoutSeconds)), cancellationToken))
            {
                _metrics.Record(provider.Name, "busy");
                _logger.LogWarning("Beslenme asistani saglayicisi dolu (eszamanli istek siniri). Provider: {Provider}", provider.Name);
                last = new NutritionAiProviderException(provider.Name, NutritionAiFailureKind.RateLimited, errorCode: "local_concurrency_limit");
                continue;
            }

            try
            {
                var result = await CallWithRetriesAsync(provider, request, cancellationToken);
                if (result.Error is null)
                {
                    return result.Value!;
                }

                last = result.Error;
                if (last.Kind == NutritionAiFailureKind.RateLimited)
                {
                    var wait = last.RetryAfter ?? TimeSpan.FromSeconds(Math.Max(1, _options.DefaultCooldownSeconds));
                    _state.CoolDown(provider.Name, _timeProvider.GetUtcNow() + wait);
                    retryAfter = Min(retryAfter, wait);
                }
            }
            finally
            {
                gate.Release();
            }
        }

        throw last?.Kind switch
        {
            NutritionAiFailureKind.RateLimited => new ExternalServiceUnavailableException(BusyMessage, last, retryAfter),
            NutritionAiFailureKind.Timeout => new ExternalServiceUnavailableException(TimeoutMessage, last),
            _ => new ExternalServiceUnavailableException(UnavailableMessage, last),
        };
    }

    private async Task<(NutritionAssistantModelResult? Value, NutritionAiProviderException? Error)> CallWithRetriesAsync(
        INutritionAiProvider provider, NutritionAssistantModelRequest request, CancellationToken cancellationToken)
    {
        var maxAttempts = 1 + Math.Max(0, _options.MaxRetries);
        for (var attempt = 1; ; attempt++)
        {
            var stopwatch = Stopwatch.StartNew();
            try
            {
                var result = await provider.CompleteAsync(request, cancellationToken);
                _metrics.Record(provider.Name, "success", stopwatch.Elapsed.TotalMilliseconds);
                _metrics.RecordTokens(provider.Name, result.InputTokens, result.OutputTokens);
                _state.Clear(provider.Name);
                return (string.IsNullOrEmpty(result.Provider) ? result with { Provider = provider.Name } : result, null);
            }
            catch (NutritionAiProviderException ex)
            {
                _metrics.Record(provider.Name, Outcome(ex.Kind), stopwatch.Elapsed.TotalMilliseconds);
                var level = ex.Kind == NutritionAiFailureKind.Rejected ? LogLevel.Error : LogLevel.Warning;
                _logger.Log(level,
                    "Beslenme asistani saglayici hatasi. Provider: {Provider}, Kind: {Kind}, StatusCode: {StatusCode}, ErrorCode: {ErrorCode}, RetryAfterSeconds: {RetryAfterSeconds}, Attempt: {Attempt}, DurationMs: {DurationMs}",
                    provider.Name, ex.Kind, ex.StatusCode, ex.ErrorCode, ex.RetryAfter?.TotalSeconds, attempt, stopwatch.ElapsedMilliseconds);

                if (attempt >= maxAttempts) return (null, ex);

                switch (ex.Kind)
                {
                    case NutritionAiFailureKind.RateLimited
                        when ex.RetryAfter is { } wait && wait <= TimeSpan.FromSeconds(Math.Max(0, _options.MaxRetryAfterWaitSeconds)):
                        await Task.Delay(wait, _timeProvider, cancellationToken);
                        continue;
                    case NutritionAiFailureKind.Transient:
                        await Task.Delay(TimeSpan.FromMilliseconds(250 * attempt), _timeProvider, cancellationToken);
                        continue;
                    default:
                        // Uzun kota beklemesi, zaman asimi ve 4xx tekrar denenmez.
                        return (null, ex);
                }
            }
        }
    }

    private static string Outcome(NutritionAiFailureKind kind) => kind switch
    {
        NutritionAiFailureKind.RateLimited => "rate_limited",
        NutritionAiFailureKind.Timeout => "timeout",
        NutritionAiFailureKind.Transient => "transient_error",
        _ => "rejected",
    };

    private static TimeSpan? Min(TimeSpan? current, TimeSpan candidate) =>
        current is null || candidate < current ? candidate : current;
}
