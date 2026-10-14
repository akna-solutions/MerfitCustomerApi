using MerfitCustomerApi.Business.Common;
using Microsoft.Extensions.Options;

namespace MerfitCustomerApi.Business.Services.FoodAnalysis;

/// <summary>
/// Sunucu ornegi basina (singleton) saglayici korumasi: ayni anda en fazla N analiz ve saglayici 429 dondurdugunde
/// bekleme suresi boyunca yeni istek gondermeme. Boylece ucretsiz kotasi dolmus saglayiciya her istekte tekrar gidilmez.
/// </summary>
public sealed class FoodAnalysisGate
{
    private readonly SemaphoreSlim _slots;
    private readonly TimeProvider _timeProvider;
    private long _cooldownUntilTicks;

    public FoodAnalysisGate(IOptions<FoodAnalysisOptions> options, TimeProvider timeProvider)
    {
        _slots = new SemaphoreSlim(Math.Max(1, options.Value.MaxConcurrentRequests));
        _timeProvider = timeProvider;
    }

    /// <summary>Saglayici bekleme suresindeyse kalan sure; degilse null.</summary>
    public TimeSpan? CooldownRemaining
    {
        get
        {
            var remaining = new DateTimeOffset(Interlocked.Read(ref _cooldownUntilTicks), TimeSpan.Zero) - _timeProvider.GetUtcNow();
            return remaining > TimeSpan.Zero ? remaining : null;
        }
    }

    public void StartCooldown(TimeSpan duration)
    {
        var until = _timeProvider.GetUtcNow().Add(duration).UtcTicks;
        long current;
        do
        {
            current = Interlocked.Read(ref _cooldownUntilTicks);
            if (current >= until) return;
        }
        while (Interlocked.CompareExchange(ref _cooldownUntilTicks, until, current) != current);
    }

    /// <summary>Bos yer bekler; sure dolarsa false doner.</summary>
    public Task<bool> TryEnterAsync(TimeSpan wait, CancellationToken cancellationToken) => _slots.WaitAsync(wait, cancellationToken);

    public void Exit() => _slots.Release();
}
