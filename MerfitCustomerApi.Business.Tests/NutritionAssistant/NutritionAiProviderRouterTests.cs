using System.Diagnostics.Metrics;
using MerfitCustomerApi.Business.Common;
using MerfitCustomerApi.Business.Interfaces.Services;
using MerfitCustomerApi.Business.Services.NutritionAssistant;
using MerfitCustomerApi.Domain.Exceptions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace MerfitCustomerApi.Business.Tests.NutritionAssistant;

/// <summary>
/// Saglayici secimi ve dayaniklilik: yedek saglayici, ucretli saglayiciya otomatik gecisin kapali olmasi,
/// Retry-After'a uyum, sinirli yeniden deneme, eszamanlilik siniri ve log/metriklerde gizli veri olmamasi.
/// </summary>
public class NutritionAiProviderRouterTests
{
    private readonly NutritionAssistantOptions _options = new()
    {
        Provider = "Groq",
        MaxRetries = 1,
        MaxRetryAfterWaitSeconds = 1,
        DefaultCooldownSeconds = 30,
        QueueTimeoutSeconds = 0,
    };
    private readonly FakeProvider _groq = new("Groq");
    private readonly FakeProvider _gemini = new("Gemini");
    private readonly FakeProvider _anthropic = new("Anthropic") { Paid = true };
    private readonly CapturingLogger _logger = new();
    private readonly MutableClock _clock = new();
    private NutritionAiProviderState? _state;

    private NutritionAiProviderRouter Router()
    {
        _state ??= new NutritionAiProviderState(Options.Create(_options));
        return new NutritionAiProviderRouter(new[] { _groq, _gemini, _anthropic }, _state, Options.Create(_options), new NutritionAssistantMetrics(), _clock, _logger);
    }

    private static NutritionAssistantModelRequest Request() => GroqNutritionAiProviderTests.Request();

    [Fact]
    public async Task UsesConfiguredPrimaryProvider()
    {
        var result = await Router().CompleteAsync(Request());

        Assert.Equal("Groq", result.Provider);
        Assert.Equal(1, _groq.Calls);
        Assert.Equal(0, _gemini.Calls);
    }

    [Fact]
    public void ProviderIsSelectedFromConfiguration_WithoutCodeChanges()
    {
        _options.Provider = "gemini";
        _options.FallbackProvider = "Groq";

        Assert.Equal(new[] { "Gemini", "Groq" }, Router().ProviderChain);
    }

    [Fact]
    public async Task RateLimitedPrimary_FallsBackToConfiguredFreeProvider_AndCoolsDown()
    {
        _options.FallbackProvider = "Gemini";
        _groq.Failures.Enqueue(new NutritionAiProviderException("Groq", NutritionAiFailureKind.RateLimited, 429, TimeSpan.FromSeconds(40)));

        var first = await Router().CompleteAsync(Request());
        var second = await Router().CompleteAsync(Request());

        Assert.Equal("Gemini", first.Provider);
        Assert.Equal("Gemini", second.Provider);
        // Uzun Retry-After: beklenmez, tekrar denenmez; bekleme suresince Groq'a hic istek gitmez.
        Assert.Equal(1, _groq.Calls);

        _clock.Advance(TimeSpan.FromSeconds(41));
        Assert.Equal("Groq", (await Router().CompleteAsync(Request())).Provider);
    }

    [Fact]
    public async Task ShortRetryAfter_IsHonoured_ThenRetriedOnce()
    {
        _groq.Failures.Enqueue(new NutritionAiProviderException("Groq", NutritionAiFailureKind.RateLimited, 429, TimeSpan.FromMilliseconds(50)));

        var result = await Router().CompleteAsync(Request());

        Assert.Equal("Groq", result.Provider);
        Assert.Equal(2, _groq.Calls);
    }

    [Fact]
    public async Task PaidFallback_IsNotUsed_ByDefault()
    {
        _options.FallbackProvider = "Anthropic";
        _groq.Failures.Enqueue(new NutritionAiProviderException("Groq", NutritionAiFailureKind.RateLimited, 429, TimeSpan.FromMinutes(1)));

        var ex = await Assert.ThrowsAsync<ExternalServiceUnavailableException>(() => Router().CompleteAsync(Request()));

        Assert.Equal(0, _anthropic.Calls);
        Assert.Contains("yoğun", ex.Message);
        Assert.Equal(TimeSpan.FromMinutes(1), ex.RetryAfter);
    }

    [Fact]
    public async Task PaidFallback_IsUsed_OnlyWhenExplicitlyAllowed()
    {
        _options.FallbackProvider = "Anthropic";
        _options.AllowPaidFallback = true;
        _groq.Failures.Enqueue(new NutritionAiProviderException("Groq", NutritionAiFailureKind.Rejected, 401));

        var result = await Router().CompleteAsync(Request());

        Assert.Equal("Anthropic", result.Provider);
        Assert.Equal(1, _groq.Calls); // 4xx tekrar denenmez
    }

    [Fact]
    public async Task TransientErrors_AreRetriedAtMostOnce_ThenFail()
    {
        for (var i = 0; i < 5; i++)
        {
            _groq.Failures.Enqueue(new NutritionAiProviderException("Groq", NutritionAiFailureKind.Transient, 503));
        }

        var ex = await Assert.ThrowsAsync<ExternalServiceUnavailableException>(() => Router().CompleteAsync(Request()));

        Assert.Equal(2, _groq.Calls);
        Assert.Contains("yanıt veremiyor", ex.Message);
    }

    [Fact]
    public async Task Timeout_IsNotRetried()
    {
        _groq.Failures.Enqueue(new NutritionAiProviderException("Groq", NutritionAiFailureKind.Timeout));

        var ex = await Assert.ThrowsAsync<ExternalServiceUnavailableException>(() => Router().CompleteAsync(Request()));

        Assert.Equal(1, _groq.Calls);
        Assert.Contains("gecikti", ex.Message);
    }

    [Fact]
    public async Task ConcurrencyLimit_ReturnsBusy_InsteadOfQueueingForever()
    {
        _options.MaxConcurrentRequestsPerProvider = 1;
        var release = new TaskCompletionSource();
        _groq.Gate = release.Task;

        var first = Router().CompleteAsync(Request());
        var second = await Assert.ThrowsAsync<ExternalServiceUnavailableException>(() => Router().CompleteAsync(Request()));

        Assert.Contains("yoğun", second.Message);
        release.SetResult();
        Assert.Equal("Groq", (await first).Provider);
    }

    [Fact]
    public async Task CallerCancellation_Propagates_WithoutFallback()
    {
        _options.FallbackProvider = "Gemini";
        _groq.Failures.Enqueue(new OperationCanceledException());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Router().CompleteAsync(Request(), new CancellationToken(true)));

        Assert.Equal(0, _gemini.Calls);
    }

    [Fact]
    public async Task NoConfiguredProvider_IsUnavailable()
    {
        _groq.Configured = false;

        var router = Router();

        Assert.False(router.IsConfigured);
        await Assert.ThrowsAsync<ExternalServiceUnavailableException>(() => router.CompleteAsync(Request()));
    }

    [Fact]
    public async Task LogsAndMetrics_ContainNoKeysOrMessageContent()
    {
        var tags = new List<string>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == NutritionAssistantMetrics.MeterName) l.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<long>((_, _, measurementTags, _) => tags.AddRange(measurementTags.ToArray().Select(t => $"{t.Key}={t.Value}")));
        listener.SetMeasurementEventCallback<double>((_, _, measurementTags, _) => tags.AddRange(measurementTags.ToArray().Select(t => $"{t.Key}={t.Value}")));
        listener.Start();

        _options.FallbackProvider = "Gemini";
        _groq.Failures.Enqueue(new NutritionAiProviderException("Groq", NutritionAiFailureKind.Rejected, 401, errorCode: "invalid_api_key"));
        await Router().CompleteAsync(Request());

        var logged = string.Join("\n", _logger.Entries);
        Assert.Contains("invalid_api_key", logged);
        Assert.Contains("401", logged);
        foreach (var secret in new[] { "gsk_", "AIza", "Tavuk", "Akşam ne yiyebilirim", "protein 65" })
        {
            Assert.DoesNotContain(secret, logged);
            Assert.DoesNotContain(tags, t => t.Contains(secret));
        }
        Assert.Contains("outcome=rejected", tags);
        Assert.Contains("outcome=success", tags);
        Assert.All(tags, t => Assert.Matches("^(provider|outcome|direction)=", t));
    }

    private sealed class FakeProvider : INutritionAiProvider
    {
        public FakeProvider(string name) => Name = name;

        public string Name { get; }
        public bool Configured { get; set; } = true;
        public bool Paid { get; set; }
        public int Calls { get; private set; }
        public Queue<Exception> Failures { get; } = new();
        public Task? Gate { get; set; }

        public bool IsConfigured => Configured;
        public bool IsPaid => Paid;
        public string Model => Name.ToLowerInvariant() + "-model";

        public async Task<NutritionAssistantModelResult> CompleteAsync(NutritionAssistantModelRequest request, CancellationToken cancellationToken = default)
        {
            Calls++;
            if (Gate is { } gate) await gate;
            if (Failures.TryDequeue(out var failure)) throw failure;
            return new NutritionAssistantModelResult("tamam", 10, 2, "stop", Model, false, Name);
        }
    }

    private sealed class MutableClock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 10, 13, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan by) => _now += by;
    }

    private sealed class CapturingLogger : ILogger<NutritionAiProviderRouter>
    {
        public List<string> Entries { get; } = new();
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            lock (Entries) Entries.Add(formatter(state, exception) + (exception is null ? string.Empty : "\n" + exception));
        }
    }
}
