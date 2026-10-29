using MerfitCustomerApi.Business.Dtos.Customer.Progress;
using MerfitCustomerApi.Business.Services.Progress;
using MerfitCustomerApi.Business.Tests.AccountEmail;
using MerfitCustomerApi.Business.Tests.TestHelpers;
using MerfitCustomerApi.Domain.Entities;
using MerfitCustomerApi.Domain.Entities.Enums;
using Xunit;

namespace MerfitCustomerApi.Business.Tests.Progress;

/// <summary>Iki gunde bir kilo hatirlatmasi: son kayit, erteleme/kapatma ve kayit olmayan yeni hesaplar.</summary>
public class WeightReminderServiceTests
{
    private const long UserId = 21;

    private readonly InMemoryUnitOfWork _db = new();
    private readonly MutableTimeProvider _clock = new() { Now = new DateTimeOffset(2026, 10, 20, 9, 0, 0, TimeSpan.Zero) };

    public WeightReminderServiceTests()
    {
        _db.Register<BodyMeasurement>().Register<UserPreference>().Register<ApplicationUser>();
        _db.Set<UserProfile>().Add(new UserProfile { Id = 1, UserId = UserId, WeightKg = 80, CreatedAt = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc) });
        _db.Set<ApplicationUser>().Add(new ApplicationUser { Id = UserId, Email = "a@b.com", CreatedAt = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc) });
    }

    private WeightReminderService CreateService() => new(_db.Object, _clock);

    private void AddWeight(int day, decimal kg) =>
        _db.Set<BodyMeasurement>().Add(new BodyMeasurement
        {
            Id = day, UserId = UserId, WeightKg = kg, RecordedAt = new DateTime(2026, 10, day, 0, 0, 0, DateTimeKind.Utc),
        });

    [Theory]
    [InlineData(20, false)] // bugun girildi
    [InlineData(19, false)] // dun girildi
    [InlineData(18, true)]  // iki gun once
    [InlineData(10, true)]
    public async Task Prompt_DependsOnDaysSinceLastLog(int lastDay, bool expected)
    {
        AddWeight(lastDay, 79.4m);

        var result = await CreateService().GetAsync(UserId);

        Assert.Equal(expected, result.ShouldPrompt);
        Assert.Equal($"2026-10-{lastDay:00}", result.LastLoggedDate);
        Assert.Equal(79.4m, result.LastWeightKg);
        Assert.Equal(WeightReminderService.IntervalDays, result.IntervalDays);
    }

    [Fact]
    public async Task OnlyEntriesWithWeight_Count()
    {
        AddWeight(10, 79m);
        // Yalnizca cevre olcumu iceren (kilosuz) bugunku kayit hatirlatmayi susturmaz.
        _db.Set<BodyMeasurement>().Add(new BodyMeasurement { Id = 99, UserId = UserId, WaistCm = 80, RecordedAt = new DateTime(2026, 10, 20, 0, 0, 0, DateTimeKind.Utc) });

        Assert.True((await CreateService().GetAsync(UserId)).ShouldPrompt);
    }

    [Fact]
    public async Task NewAccountWithoutLogs_IsNotPromptedImmediately()
    {
        _db.Set<ApplicationUser>()[0].CreatedAt = new DateTime(2026, 10, 19, 20, 0, 0, DateTimeKind.Utc);

        var result = await CreateService().GetAsync(UserId);

        Assert.False(result.ShouldPrompt);
        Assert.Null(result.LastLoggedDate);
        Assert.Equal(80m, result.LastWeightKg); // kayitta girilen profil kilosu
    }

    [Fact]
    public async Task OldAccountWithoutLogs_IsPrompted()
    {
        Assert.True((await CreateService().GetAsync(UserId)).ShouldPrompt);
    }

    [Fact]
    public async Task Later_SnoozesForOneDay_ThenPromptsAgain()
    {
        AddWeight(10, 79m);
        var service = CreateService();

        var snoozed = await service.SnoozeAsync(UserId, new SnoozeWeightReminderRequest { Action = "later" });

        Assert.False(snoozed.ShouldPrompt);
        var preference = Assert.Single(_db.Set<UserPreference>());
        Assert.Equal(_clock.Now.UtcDateTime.AddDays(1), preference.WeightReminderSnoozedUntil);

        _clock.Advance(TimeSpan.FromHours(23));
        Assert.False((await service.GetAsync(UserId)).ShouldPrompt);
        _clock.Advance(TimeSpan.FromHours(2));
        Assert.True((await service.GetAsync(UserId)).ShouldPrompt);
    }

    [Fact]
    public async Task Dismiss_WaitsForNextInterval_AndKeepsExistingPreferences()
    {
        AddWeight(10, 79m);
        _db.Set<UserPreference>().Add(new UserPreference { Id = 5, UserId = UserId, Language = "tr", Theme = Theme.Dark, UnitSystem = UnitSystem.Imperial });
        var service = CreateService();

        var result = await service.SnoozeAsync(UserId, new SnoozeWeightReminderRequest { Action = "dismiss" });

        Assert.False(result.ShouldPrompt);
        Assert.Equal("imperial", result.UnitSystem);
        var preference = Assert.Single(_db.Set<UserPreference>());
        Assert.Equal(Theme.Dark, preference.Theme);
        Assert.Equal(_clock.Now.UtcDateTime.AddDays(2), preference.WeightReminderSnoozedUntil);

        _clock.Advance(TimeSpan.FromDays(2).Add(TimeSpan.FromMinutes(1)));
        Assert.True((await service.GetAsync(UserId)).ShouldPrompt);
    }

    [Fact]
    public async Task LoggingWeightAfterSnooze_StopsPrompt()
    {
        AddWeight(10, 79m);
        var service = CreateService();
        await service.SnoozeAsync(UserId, new SnoozeWeightReminderRequest { Action = "later" });
        _clock.Advance(TimeSpan.FromDays(1).Add(TimeSpan.FromHours(1)));

        AddWeight(21, 78.6m);

        Assert.False((await service.GetAsync(UserId)).ShouldPrompt);
    }
}
