using System.Diagnostics.Metrics;

namespace MerfitCustomerApi.Business.Services.NutritionAssistant;

/// <summary>
/// Beslenme asistaninin saglayici metrikleri (System.Diagnostics.Metrics; dotnet-counters ya da
/// OpenTelemetry ile okunabilir). Etiketlerde yalnizca saglayici adi ve sonuc vardir; kullanici kimligi,
/// mesaj icerigi veya beslenme verisi yoktur.
/// </summary>
public sealed class NutritionAssistantMetrics : IDisposable
{
    public const string MeterName = "Merfit.NutritionAssistant";

    private readonly Meter _meter;
    private readonly Counter<long> _requests;
    private readonly Histogram<double> _duration;
    private readonly Counter<long> _tokens;

    public NutritionAssistantMetrics()
    {
        _meter = new Meter(MeterName);
        _requests = _meter.CreateCounter<long>("nutrition_assistant.provider.requests", description: "Saglayici cagrilari; outcome: success, rate_limited, timeout, transient_error, rejected, busy, cooling_down.");
        _duration = _meter.CreateHistogram<double>("nutrition_assistant.provider.duration", unit: "ms", description: "Saglayici cagrisi suresi.");
        _tokens = _meter.CreateCounter<long>("nutrition_assistant.provider.tokens", description: "Kullanilan token; direction: input | output.");
    }

    public void Record(string provider, string outcome, double? durationMs = null)
    {
        var providerTag = new KeyValuePair<string, object?>("provider", provider);
        _requests.Add(1, providerTag, new KeyValuePair<string, object?>("outcome", outcome));
        if (durationMs is { } ms)
        {
            _duration.Record(ms, providerTag, new KeyValuePair<string, object?>("outcome", outcome));
        }
    }

    public void RecordTokens(string provider, int input, int output)
    {
        var providerTag = new KeyValuePair<string, object?>("provider", provider);
        _tokens.Add(input, providerTag, new KeyValuePair<string, object?>("direction", "input"));
        _tokens.Add(output, providerTag, new KeyValuePair<string, object?>("direction", "output"));
    }

    public void Dispose() => _meter.Dispose();
}
