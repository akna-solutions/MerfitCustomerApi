using MerfitCustomerApi.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace MerfitCustomerApi.Infrastructure.Persistence;

/// <summary>INotificationQueueStore'un PostgreSQL implementasyonu.</summary>
public class NotificationQueueStore : INotificationQueueStore
{
    private readonly AppDbContext _dbContext;

    public NotificationQueueStore(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IReadOnlyList<long>> ClaimDueDispatchesAsync(DateTime nowUtc, int batchSize, TimeSpan lockDuration, Guid lockToken, CancellationToken cancellationToken = default)
    {
        var lockedUntil = nowUtc + lockDuration;

        // Tek ifade: adaylar FOR UPDATE SKIP LOCKED ile kilitlenir, baska bir instance ayni satirlari
        // atlar. Kilidi dolmus Processing satirlari (cokmus calisan) yeniden sahiplenilir.
        return await _dbContext.Database.SqlQuery<long>($"""
            UPDATE notification."NotificationDispatch" AS d
            SET "Status" = 'Processing', "LockToken" = {lockToken}, "LockedUntilUtc" = {lockedUntil}, "UpdatedAt" = {nowUtc}
            WHERE d."Id" IN (
                SELECT c."Id" FROM notification."NotificationDispatch" AS c
                WHERE (c."Status" = 'Pending' AND c."NextAttemptAtUtc" <= {nowUtc} AND c."ScheduledForUtc" <= {nowUtc})
                   OR (c."Status" = 'Processing' AND c."LockedUntilUtc" < {nowUtc})
                ORDER BY c."NextAttemptAtUtc", c."Id"
                LIMIT {batchSize}
                FOR UPDATE SKIP LOCKED)
            RETURNING d."Id" AS "Value"
            """).ToListAsync(cancellationToken);
    }
}
