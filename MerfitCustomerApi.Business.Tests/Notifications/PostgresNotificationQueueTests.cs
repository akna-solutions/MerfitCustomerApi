using MerfitCustomerApi.Business.Services.Notifications.Engine;
using MerfitCustomerApi.Domain.Entities;
using MerfitCustomerApi.Domain.Entities.Enums;
using MerfitCustomerApi.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace MerfitCustomerApi.Business.Tests.Notifications;

/// <summary>
/// Yalnizca MERFIT_TEST_POSTGRES ortam degiskeni (migration'lari uygulanmis bir test veritabaninin
/// baglanti dizesi) tanimliysa calisir; aksi halde atlanir. Gonderim kuyrugunun veritabani seviyesindeki
/// garantilerini (FOR UPDATE SKIP LOCKED sahiplenme, DedupeKey benzersizligi) gercek PostgreSQL'de dogrular.
/// </summary>
public sealed class PostgresFactAttribute : FactAttribute
{
    public PostgresFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(PostgresNotificationQueueTests.ConnectionVariable)))
        {
            Skip = $"{PostgresNotificationQueueTests.ConnectionVariable} tanımlı değil.";
        }
    }
}

public class PostgresNotificationQueueTests
{
    public const string ConnectionVariable = "MERFIT_TEST_POSTGRES";

    private static AppDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(Environment.GetEnvironmentVariable(ConnectionVariable)).Options);

    private static async Task<long> CreateUserAsync()
    {
        await using var db = CreateContext();
        var email = $"queue-{Guid.NewGuid():N}@test.local";
        var user = new ApplicationUser
        {
            Email = email, NormalizedEmail = email.ToUpperInvariant(), UserName = email, NormalizedUserName = email.ToUpperInvariant(),
            PasswordHash = "x", IsActive = true, Role = UserRole.User, CreatedAt = DateTime.UtcNow,
        };
        db.ApplicationUsers.Add(user);
        await db.SaveChangesAsync();
        return user.Id;
    }

    private static NotificationDispatch Dispatch(long userId, string key, DateTime now) => new()
    {
        UserId = userId, Type = NotificationType.Announcement, Source = NotificationDispatchSource.Campaign, DedupeKey = key,
        Title = "t", Body = "b", ScheduledForUtc = now.AddMinutes(-1), NextAttemptAtUtc = now.AddMinutes(-1),
        Status = NotificationDispatchStatus.Pending, CreatedAt = now,
    };

    [PostgresFact]
    public async Task ConcurrentClaims_NeverReturnTheSameDispatchTwice()
    {
        var userId = await CreateUserAsync();
        var now = DateTime.UtcNow;
        var prefix = Guid.NewGuid().ToString("N");
        await using (var db = CreateContext())
        {
            db.NotificationDispatches.AddRange(Enumerable.Range(0, 40).Select(i => Dispatch(userId, $"{prefix}:{i}", now)));
            await db.SaveChangesAsync();
        }

        async Task<IReadOnlyList<long>> ClaimAsync()
        {
            await using var db = CreateContext();
            return await new NotificationQueueStore(db).ClaimDueDispatchesAsync(now, 1000, TimeSpan.FromMinutes(5), Guid.NewGuid());
        }

        var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Task.Run(ClaimAsync)));

        await using var check = CreateContext();
        var ours = await check.NotificationDispatches.Where(d => d.DedupeKey.StartsWith(prefix)).Select(d => d.Id).ToListAsync();
        var claimedOurs = results.SelectMany(r => r).Where(ours.Contains).ToList();
        Assert.Equal(claimedOurs.Count, claimedOurs.Distinct().Count());
        Assert.Equal(ours.OrderBy(x => x), claimedOurs.OrderBy(x => x));
        Assert.True(await check.NotificationDispatches.Where(d => d.DedupeKey.StartsWith(prefix)).AllAsync(d => d.Status == NotificationDispatchStatus.Processing));
    }

    [PostgresFact]
    public async Task ExpiredLock_IsReclaimed_ActiveLockIsNot()
    {
        var userId = await CreateUserAsync();
        var now = DateTime.UtcNow;
        var prefix = Guid.NewGuid().ToString("N");
        await using (var db = CreateContext())
        {
            var stale = Dispatch(userId, $"{prefix}:stale", now);
            stale.Status = NotificationDispatchStatus.Processing;
            stale.LockedUntilUtc = now.AddMinutes(-1);
            var active = Dispatch(userId, $"{prefix}:active", now);
            active.Status = NotificationDispatchStatus.Processing;
            active.LockedUntilUtc = now.AddMinutes(4);
            db.NotificationDispatches.AddRange(stale, active);
            await db.SaveChangesAsync();
        }

        await using var claimDb = CreateContext();
        var claimed = await new NotificationQueueStore(claimDb).ClaimDueDispatchesAsync(now, 1000, TimeSpan.FromMinutes(5), Guid.NewGuid());

        await using var check = CreateContext();
        var keys = await check.NotificationDispatches.Where(d => claimed.Contains(d.Id) && d.DedupeKey.StartsWith(prefix)).Select(d => d.DedupeKey).ToListAsync();
        Assert.Equal(new[] { $"{prefix}:stale" }, keys);
    }

    [PostgresFact]
    public async Task DispatchWriter_ConcurrentDuplicateInsert_CreatesExactlyOneRow()
    {
        var userId = await CreateUserAsync();
        var now = DateTime.UtcNow;
        var key = $"rule:test:{Guid.NewGuid():N}";

        async Task<int> InsertAsync()
        {
            await using var db = CreateContext();
            using var uow = new UnitOfWork(db);
            return await NotificationDispatchWriter.InsertIgnoringDuplicatesAsync(uow, new[] { Dispatch(userId, key, now) }, CancellationToken.None);
        }

        var results = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => Task.Run(InsertAsync)));

        await using var check = CreateContext();
        Assert.Equal(1, await check.NotificationDispatches.CountAsync(d => d.DedupeKey == key));
        Assert.Equal(1, results.Sum());
    }

    [PostgresFact]
    public async Task MonthlyRewardRun_IsUniquePerPeriod()
    {
        await using var db = CreateContext();
        var period = new LeaderboardPeriod { Type = LeaderboardPeriodType.Monthly, StartDate = DateTime.UtcNow.AddMonths(-1), EndDate = DateTime.UtcNow, CreatedAt = DateTime.UtcNow };
        db.LeaderboardPeriods.Add(period);
        await db.SaveChangesAsync();

        db.MonthlyRewardNotificationRuns.Add(new MonthlyRewardNotificationRun { LeaderboardPeriodId = period.Id, CreatedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();
        db.MonthlyRewardNotificationRuns.Add(new MonthlyRewardNotificationRun { LeaderboardPeriodId = period.Id, CreatedAt = DateTime.UtcNow });

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }
}
