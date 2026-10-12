using MerfitCustomerApi.Business.Common;
using MerfitCustomerApi.Business.Interfaces.Services;
using MerfitCustomerApi.Business.Services.Notifications;
using MerfitCustomerApi.Business.Services.Notifications.Engine;
using MerfitCustomerApi.Business.Tests.TestHelpers;
using MerfitCustomerApi.Domain.Entities;
using MerfitCustomerApi.Domain.Entities.Enums;
using MerfitCustomerApi.Domain.Interfaces;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace MerfitCustomerApi.Business.Tests.Notifications;

/// <summary>Bildirim motoru testleri icin ortak veri ve sahte bagimliliklar.</summary>
internal sealed class NotificationTestData
{
    public InMemoryUnitOfWork Db { get; } = new();
    public NotificationOptions Options { get; } = new() { DefaultTimeZone = "Europe/Istanbul", DailyPushLimit = 4 };
    public FakePushService Push { get; } = new();
    public FakeQueueStore Queue { get; }

    public NotificationTestData()
    {
        Db.Register<ApplicationUser>().Register<UserDevice>().Register<UserPreference>().Register<UserProfile>()
            .Register<UserNotificationSetting>().Register<UserNotificationTypeSetting>()
            .Register<NotificationRule>().Register<NotificationTemplate>().Register<NotificationRuleTemplate>()
            .Register<NotificationDispatch>().Register<NotificationDeliveryAttempt>().Register<NotificationCampaign>()
            .Register<Notification>().Register<NutritionPlan>().Register<NutritionPlanDay>().Register<NutritionPlanMeal>()
            .Register<Meal>().Register<MealItem>().Register<WorkoutPlan>().Register<WorkoutPlanDay>()
            .Register<WorkoutSession>().Register<Workout>();
        Queue = new FakeQueueStore(Db.Set<NotificationDispatch>());
    }

    public NotificationService PreferenceService => new(Db.Object, Microsoft.Extensions.Options.Options.Create(Options));

    public NotificationEligibilityService Eligibility => new(Db.Object);

    public NotificationRulePlanner Planner => new(Db.Object, Eligibility, PreferenceService, Microsoft.Extensions.Options.Options.Create(Options), NullLogger<NotificationRulePlanner>.Instance);

    public NotificationDispatchProcessor Processor => new(Db.Object, Queue, Eligibility, PreferenceService, Push, Microsoft.Extensions.Options.Options.Create(Options), NullLogger<NotificationDispatchProcessor>.Instance);

    public ApplicationUser AddUser(long id, string? timeZone = "Europe/Istanbul", bool withDevice = true, string firstName = "Ayşe")
    {
        var user = new ApplicationUser { Id = id, Email = $"u{id}@test.local", IsActive = true, Role = UserRole.User, CreatedAt = DateTime.UtcNow.AddDays(-30) };
        Db.Set<ApplicationUser>().Add(user);
        Db.Set<UserProfile>().Add(new UserProfile { Id = id, UserId = id, FirstName = firstName });
        if (timeZone is not null)
        {
            Db.Set<UserPreference>().Add(new UserPreference { Id = id, UserId = id, Language = "tr", TimeZone = timeZone });
        }
        if (withDevice)
        {
            AddDevice(id * 10, id);
        }
        return user;
    }

    public UserDevice AddDevice(long deviceId, long userId, string? token = null)
    {
        var device = new UserDevice { Id = deviceId, UserId = userId, DeviceToken = token ?? $"ExponentPushToken[device{deviceId}xxxxxx]", IsActive = true, Platform = Platform.IOS };
        Db.Set<UserDevice>().Add(device);
        return device;
    }

    public NotificationRule AddRule(long id, NotificationRuleKind kind, TimeOnly localTime, MealType? mealType = null, bool onlyWithPlan = true, int mask = 127, int templateCount = 1)
    {
        var rule = new NotificationRule { Id = id, Name = $"r{id}", Kind = kind, Status = NotificationRuleStatus.Active, LocalTime = localTime, MealType = mealType, OnlyUsersWithPlan = onlyWithPlan, DaysOfWeekMask = mask };
        Db.Set<NotificationRule>().Add(rule);
        for (var i = 0; i < templateCount; i++)
        {
            var templateId = id * 100 + i;
            Db.Set<NotificationTemplate>().Add(new NotificationTemplate
            {
                Id = templateId,
                Name = $"t{templateId}",
                Type = NotificationRulePlanner.TypeFor(kind),
                TitleTemplate = "Merhaba {firstName}",
                BodyTemplate = kind switch
                {
                    NotificationRuleKind.NutritionReminder => "{mealName} kaydını eklemek ister misin? (" + i + ")",
                    NotificationRuleKind.WorkoutReminder => "Bugün {workoutName} seni bekliyor. (" + i + ")",
                    _ => "Motivasyon " + i,
                },
                IsActive = true,
            });
            Db.Set<NotificationRuleTemplate>().Add(new NotificationRuleTemplate { Id = templateId, NotificationRuleId = id, NotificationTemplateId = templateId });
        }
        return rule;
    }

    /// <summary>Kullaniciya aktif bir beslenme plani ekler; verilen gunlerde verilen ogun planlidir.</summary>
    public void AddNutritionPlan(long userId, DayOfWeek day, params MealType[] meals)
    {
        var planId = userId * 1000;
        if (!Db.Set<NutritionPlan>().Any(p => p.Id == planId))
        {
            Db.Set<NutritionPlan>().Add(new NutritionPlan { Id = planId, UserId = userId, IsActive = true, Name = "plan" });
        }
        var dayId = planId + (int)day;
        Db.Set<NutritionPlanDay>().Add(new NutritionPlanDay { Id = dayId, NutritionPlanId = planId, DayOfWeek = day });
        foreach (var meal in meals)
        {
            Db.Set<NutritionPlanMeal>().Add(new NutritionPlanMeal { Id = dayId * 10 + (int)meal, NutritionPlanDayId = dayId, MealType = meal, Name = meal.ToString() });
        }
    }

    public void LogMeal(long userId, DateOnly date, MealType mealType, bool withItem = true)
    {
        var mealId = Db.Set<Meal>().Count + 1;
        Db.Set<Meal>().Add(new Meal { Id = mealId, UserId = userId, Date = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc), MealType = mealType });
        if (withItem)
        {
            Db.Set<MealItem>().Add(new MealItem { Id = mealId, MealId = mealId, FoodId = 1, Quantity = 1 });
        }
    }

    public void AddWorkoutPlan(long userId, DayOfWeek day, string workoutTitle = "Üst Vücut")
    {
        var planId = userId * 1000;
        if (!Db.Set<WorkoutPlan>().Any(p => p.Id == planId))
        {
            Db.Set<WorkoutPlan>().Add(new WorkoutPlan { Id = planId, UserId = userId, IsActive = true, Name = "plan" });
        }
        var workoutId = planId + (int)day;
        Db.Set<Workout>().Add(new Workout { Id = workoutId, Title = workoutTitle });
        Db.Set<WorkoutPlanDay>().Add(new WorkoutPlanDay { Id = workoutId, WorkoutPlanId = planId, DayOfWeek = day, WorkoutId = workoutId });
    }

    public void CompleteWorkout(long userId, DateTime completedAtUtc, WorkoutSessionStatus status = WorkoutSessionStatus.Completed)
    {
        Db.Set<WorkoutSession>().Add(new WorkoutSession { Id = Db.Set<WorkoutSession>().Count + 1, UserId = userId, WorkoutId = 1, Status = status, StartedAt = completedAtUtc.AddMinutes(-40), CompletedAt = completedAtUtc });
    }

    public NotificationDispatch AddDispatch(long id, long userId, NotificationType type = NotificationType.Announcement, NotificationDispatchSource source = NotificationDispatchSource.Campaign, long? ruleId = null, long? campaignId = null, DateOnly? localDate = null, DateTime? expiresAtUtc = null, DateTime? now = null)
    {
        var at = now ?? DateTime.UtcNow;
        var dispatch = new NotificationDispatch
        {
            Id = id,
            UserId = userId,
            Type = type,
            Source = source,
            NotificationRuleId = ruleId,
            NotificationCampaignId = campaignId,
            DedupeKey = $"test:{id}",
            Title = "Başlık",
            Body = "Gövde",
            DataJson = NotificationNavigation.Screen("dashboard"),
            LocalDate = localDate,
            ScheduledForUtc = at.AddMinutes(-1),
            NextAttemptAtUtc = at.AddMinutes(-1),
            ExpiresAtUtc = expiresAtUtc,
            Status = NotificationDispatchStatus.Pending,
            CreatedAt = at.AddMinutes(-1),
        };
        Db.Set<NotificationDispatch>().Add(dispatch);
        return dispatch;
    }
}

/// <summary>Kuyruktaki zamani gelmis Pending kayitlari sahiplenen sahte INotificationQueueStore (PostgreSQL implementasyonu ayrica entegrasyon testinde dogrulanir).</summary>
internal sealed class FakeQueueStore : INotificationQueueStore
{
    private readonly List<NotificationDispatch> _dispatches;

    public FakeQueueStore(List<NotificationDispatch> dispatches)
    {
        _dispatches = dispatches;
    }

    public Task<IReadOnlyList<long>> ClaimDueDispatchesAsync(DateTime nowUtc, int batchSize, TimeSpan lockDuration, Guid lockToken, CancellationToken cancellationToken = default)
    {
        var claimed = _dispatches
            .Where(d => (d.Status == NotificationDispatchStatus.Pending && d.NextAttemptAtUtc <= nowUtc && d.ScheduledForUtc <= nowUtc)
                || (d.Status == NotificationDispatchStatus.Processing && d.LockedUntilUtc < nowUtc))
            .OrderBy(d => d.NextAttemptAtUtc)
            .Take(batchSize)
            .ToList();
        foreach (var d in claimed)
        {
            d.Status = NotificationDispatchStatus.Processing;
            d.LockToken = lockToken;
            d.LockedUntilUtc = nowUtc + lockDuration;
        }
        return Task.FromResult<IReadOnlyList<long>>(claimed.Select(d => d.Id).ToList());
    }
}

/// <summary>Gonderilen mesajlari kaydeden ve token bazinda sonuc donduren sahte push saglayicisi.</summary>
internal sealed class FakePushService : IPushNotificationService
{
    public List<PushMessage> Sent { get; } = new();
    public Dictionary<string, PushSendResult> ResultsByToken { get; } = new();
    public Dictionary<string, PushReceiptResult> Receipts { get; } = new();
    public Exception? ThrowOnSend { get; set; }
    private int _ticket;

    public bool IsValidToken(string token) => token.StartsWith("ExponentPushToken[", StringComparison.Ordinal) && token.EndsWith(']');

    public Task<IReadOnlyList<PushSendResult>> SendAsync(IReadOnlyList<PushMessage> messages, CancellationToken cancellationToken = default)
    {
        if (ThrowOnSend is not null)
        {
            throw ThrowOnSend;
        }

        Sent.AddRange(messages);
        return Task.FromResult<IReadOnlyList<PushSendResult>>(messages
            .Select(m => ResultsByToken.TryGetValue(m.To, out var r) ? r : new PushSendResult(PushSendOutcome.Accepted, ProviderMessageId: $"ticket-{++_ticket}"))
            .ToList());
    }

    public Task<IReadOnlyDictionary<string, PushReceiptResult>> GetReceiptsAsync(IReadOnlyCollection<string> providerMessageIds, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyDictionary<string, PushReceiptResult>>(providerMessageIds
            .Where(Receipts.ContainsKey)
            .ToDictionary(id => id, id => Receipts[id]));
}
