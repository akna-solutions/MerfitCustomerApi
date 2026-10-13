using System.Collections.Concurrent;
using MerfitCustomerApi.Business.Common;
using Microsoft.Extensions.Options;

namespace MerfitCustomerApi.Business.Services.NutritionAssistant;

/// <summary>
/// Sunucu ornegi boyunca saglayici basina paylasilan durum (singleton): eszamanli istek sinirlayicisi ve
/// 429 sonrasi "bekleme" (cooldown) bitis zamani. Birden fazla sunucu orneginde her ornek kendi sinirini
/// uygular; toplam eszamanlilik = ornek sayisi x MaxConcurrentRequestsPerProvider.
/// </summary>
public sealed class NutritionAiProviderState
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _gates = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, DateTimeOffset> _cooldowns = new(StringComparer.OrdinalIgnoreCase);
    private readonly int _maxConcurrent;

    public NutritionAiProviderState(IOptions<NutritionAssistantOptions> options)
    {
        _maxConcurrent = Math.Max(1, options.Value.MaxConcurrentRequestsPerProvider);
    }

    public SemaphoreSlim Gate(string provider) => _gates.GetOrAdd(provider, _ => new SemaphoreSlim(_maxConcurrent, _maxConcurrent));

    /// <summary>Saglayici bekleme suresindeyse bitis zamanini dondurur.</summary>
    public DateTimeOffset? CoolingDownUntil(string provider, DateTimeOffset now) =>
        _cooldowns.TryGetValue(provider, out var until) && until > now ? until : null;

    public void CoolDown(string provider, DateTimeOffset until) =>
        _cooldowns.AddOrUpdate(provider, until, (_, existing) => existing > until ? existing : until);

    public void Clear(string provider) => _cooldowns.TryRemove(provider, out _);
}
