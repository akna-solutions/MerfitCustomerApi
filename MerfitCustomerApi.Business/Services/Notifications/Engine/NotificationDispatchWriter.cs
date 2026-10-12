using MerfitCustomerApi.Business.Common;
using MerfitCustomerApi.Domain.Entities;
using MerfitCustomerApi.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace MerfitCustomerApi.Business.Services.Notifications.Engine;

/// <summary>
/// Gonderim kuyruguna DedupeKey benzersizligine dayanarak yazar. Mevcut anahtarlar once ayiklanir;
/// es zamanli baska bir calisan ayni anahtari arada yazdiysa veritabani unique index'i ikinci kaydi
/// reddeder ve o kayit sessizce atlanir. Boylece ayni is iki kez calissa da tek bildirim olusur.
/// Not: cakisma durumunda IUnitOfWork.ClearTracking cagrilir; bu yuzden ayri bir DI scope'unda kullanilir.
/// </summary>
public static class NotificationDispatchWriter
{
    private const int KeyLookupChunk = 500;

    public static async Task<int> InsertIgnoringDuplicatesAsync(IUnitOfWork unitOfWork, IReadOnlyList<NotificationDispatch> dispatches, CancellationToken cancellationToken)
    {
        if (dispatches.Count == 0)
        {
            return 0;
        }

        var repo = unitOfWork.Repository<NotificationDispatch>();
        var existing = new HashSet<string>(StringComparer.Ordinal);
        foreach (var chunk in dispatches.Select(d => d.DedupeKey).Distinct().Chunk(KeyLookupChunk))
        {
            var found = await repo.GetQueryable()
                .Where(d => chunk.Contains(d.DedupeKey))
                .Select(d => d.DedupeKey)
                .ToListAsync(cancellationToken);
            existing.UnionWith(found);
        }

        var fresh = dispatches
            .Where(d => !existing.Contains(d.DedupeKey))
            .DistinctBy(d => d.DedupeKey)
            .ToList();
        if (fresh.Count == 0)
        {
            return 0;
        }

        await repo.AddRangeAsync(fresh, cancellationToken);
        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return fresh.Count;
        }
        catch (DbUpdateException ex) when (DbExceptionClassifier.IsUniqueViolation(ex, out _))
        {
            unitOfWork.ClearTracking();
        }

        // Yaris durumu: tek tek yaz, zaten var olanlari atla.
        var inserted = 0;
        foreach (var dispatch in fresh)
        {
            dispatch.Id = 0;
            await repo.AddAsync(dispatch, cancellationToken);
            try
            {
                await unitOfWork.SaveChangesAsync(cancellationToken);
                inserted++;
            }
            catch (DbUpdateException ex) when (DbExceptionClassifier.IsUniqueViolation(ex, out _))
            {
                unitOfWork.ClearTracking();
            }
        }

        return inserted;
    }
}
