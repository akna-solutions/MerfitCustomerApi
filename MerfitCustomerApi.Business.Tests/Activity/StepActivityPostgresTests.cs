using MerfitCustomerApi.Business.Common;
using MerfitCustomerApi.Business.Dtos.Customer.Activity;
using MerfitCustomerApi.Business.Services.Activity;
using MerfitCustomerApi.Business.Services.Scoring;
using MerfitCustomerApi.Business.Tests.Notifications;
using MerfitCustomerApi.Domain.Entities;
using MerfitCustomerApi.Domain.Entities.Enums;
using MerfitCustomerApi.Infrastructure.Persistence;
using MerfitCustomerApi.Infrastructure.Persistence.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using Xunit;

namespace MerfitCustomerApi.Business.Tests.Activity;

/// <summary>
/// Yalnizca MERFIT_TEST_POSTGRES tanimliysa calisir. Gercek PostgreSQL ile: integer[] saatlik adimlarin
/// kaydi, ayni gun icin eszamanli senkronizasyonlarda tek satir (benzersiz index + yeniden deneme), check
/// constraint'ler, adim senkronizasyonunun gercek puan motorunda basari kazandirmasi ve basari seed'inin
/// tekrar calistirilabilmesi.
/// </summary>
public class StepActivityPostgresTests
{
    private static AppDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(Environment.GetEnvironmentVariable(PostgresNotificationQueueTests.ConnectionVariable)).Options);

    private static (StepActivityService Service, AppDbContext Db) CreateService()
    {
        var db = CreateContext();
        var uow = new UnitOfWork(db);
        var scoring = new MerfitScoreService(uow, Options.Create(new MerfitScoringOptions()), NullLogger<MerfitScoreService>.Instance);
        var service = new StepActivityService(uow, scoring, Options.Create(new StepActivityOptions()), TimeProvider.System, NullLogger<StepActivityService>.Instance);
        return (service, db);
    }

    private static async Task<long> CreateUserAsync()
    {
        await using var db = CreateContext();
        await db.Database.EnsureCreatedAsync();
        var email = $"steps-{Guid.NewGuid():N}@test.local";
        var user = new ApplicationUser
        {
            Email = email, NormalizedEmail = email.ToUpperInvariant(), UserName = email, NormalizedUserName = email.ToUpperInvariant(),
            PasswordHash = "x", IsActive = true, Role = UserRole.User, CreatedAt = DateTime.UtcNow,
        };
        db.ApplicationUsers.Add(user);
        await db.SaveChangesAsync();
        return user.Id;
    }

    /// <summary>Gecen gun (yerel saat UTC+0): saat bazli zaman kurallari uygulanmaz.</summary>
    private static DateOnly Yesterday => DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1);

    private static StepSyncRequest Request(int hour, int steps)
    {
        var hours = new int[24];
        hours[hour] = steps;
        return new StepSyncRequest
        {
            Source = "pedometer",
            Platform = "android",
            Days = new List<StepSyncDayRequest> { new() { Date = Yesterday, HourlySteps = hours, UtcOffsetMinutes = 0 } },
        };
    }

    [PostgresFact]
    public async Task ConcurrentSyncsForSameNewDay_ProduceSingleMergedRow()
    {
        var userId = await CreateUserAsync();

        async Task SyncAsync(int hour, int steps)
        {
            var (service, db) = CreateService();
            await using (db)
            {
                await service.SyncAsync(userId, Request(hour, steps));
            }
        }

        // Farkli saatleri tasiyan 6 eszamanli istek + ayni istegin tekrarlari.
        await Task.WhenAll(new[] { (8, 1000), (9, 2000), (10, 3000), (8, 1000), (9, 2000), (10, 3000) }
            .Select(x => Task.Run(() => SyncAsync(x.Item1, x.Item2))));

        await using var check = CreateContext();
        var row = await check.DailyActivities.SingleAsync(a => a.UserId == userId);
        Assert.Equal(6000, row.TotalSteps);
        Assert.Equal(new[] { 1000, 2000, 3000 }, row.HourlySteps.Skip(8).Take(3));
        Assert.Equal(24, row.HourlySteps.Length);
    }

    [PostgresFact]
    public async Task VerifiedSteps_EarnStepAchievementOnce_ThroughRealScoreEngine()
    {
        await using (var seed = CreateContext())
        {
            await seed.Database.EnsureCreatedAsync();
            // Seed iki kez calistirilinca mukerrer basari olusmaz.
            await seed.Database.ExecuteSqlRawAsync(StepActivity.StepAchievementsSeedSql);
            await seed.Database.ExecuteSqlRawAsync(StepActivity.StepAchievementsSeedSql);
            Assert.Equal(1, await seed.Achievements.CountAsync(a => a.Code == "STEPS_DAILY_10000"));
        }

        var userId = await CreateUserAsync();
        var hours = new int[24];
        hours[9] = 6000;
        hours[17] = 5000;
        var request = new StepSyncRequest
        {
            Source = "pedometer",
            Days = new List<StepSyncDayRequest> { new() { Date = Yesterday, HourlySteps = hours, UtcOffsetMinutes = 0 } },
        };

        for (var i = 0; i < 2; i++)
        {
            var (service, db) = CreateService();
            await using (db)
            {
                await service.SyncAsync(userId, request);
            }
        }

        await using var check = CreateContext();
        var earnedCodes = await (
            from ua in check.UserAchievements
            join a in check.Achievements on ua.AchievementId equals a.Id
            where ua.UserId == userId
            select a.Code).ToListAsync();
        Assert.Contains("STEPS_FIRST_1000", earnedCodes);
        Assert.Contains("STEPS_DAILY_5000", earnedCodes);
        Assert.Contains("STEPS_DAILY_10000", earnedCodes);
        Assert.DoesNotContain("STEPS_DAILY_20000", earnedCodes);
        Assert.Equal(earnedCodes.Count, earnedCodes.Distinct().Count());
    }

    [PostgresFact]
    public async Task CheckConstraints_RejectBrokenRows()
    {
        var userId = await CreateUserAsync();
        await using var db = CreateContext();
        db.DailyActivities.Add(new DailyActivity
        {
            UserId = userId, Date = Yesterday, HourlySteps = new int[23], DailyGoal = 10000, Source = "pedometer", CreatedAt = DateTime.UtcNow, LastSyncedAt = DateTime.UtcNow,
        });

        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Equal(PostgresErrorCodes.CheckViolation, Assert.IsType<PostgresException>(ex.InnerException).SqlState);
    }
}
