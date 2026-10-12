using MerfitCustomerApi.Business.Common;
using MerfitCustomerApi.Business.Interfaces.Services;
using MerfitCustomerApi.Domain.Entities;
using MerfitCustomerApi.Domain.Entities.Enums;
using MerfitCustomerApi.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MerfitCustomerApi.Business.Services.Notifications.Engine;

/// <summary>
/// Aktif bildirim kurallarini her kullanicinin kendi saat diliminde degerlendirir. Kural saati
/// gectiginde (en fazla RuleSendWindow kadar gec) uygun kullanicilar icin "rule:{kural}:{kullanici}:{yerel gun}"
/// anahtariyla tek bir gonderim kaydi olusturur. Ayni planlama tekrar calistirilirsa (yeniden baslatma,
/// birden fazla instance) anahtar zaten var oldugu icin yeni kayit olusmaz. Uygunluk gonderimden hemen
/// once NotificationDispatchProcessor tarafindan yeniden degerlendirilir.
/// </summary>
public class NotificationRulePlanner : INotificationRulePlanner
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly INotificationEligibilityService _eligibility;
    private readonly INotificationPreferenceReader _preferences;
    private readonly NotificationOptions _options;
    private readonly ILogger<NotificationRulePlanner> _logger;

    public NotificationRulePlanner(
        IUnitOfWork unitOfWork,
        INotificationEligibilityService eligibility,
        INotificationPreferenceReader preferences,
        IOptions<NotificationOptions> options,
        ILogger<NotificationRulePlanner> logger)
    {
        _unitOfWork = unitOfWork;
        _eligibility = eligibility;
        _preferences = preferences;
        _options = options.Value;
        _logger = logger;
    }

    public static NotificationType TypeFor(NotificationRuleKind kind) => kind switch
    {
        NotificationRuleKind.NutritionReminder => NotificationType.NutritionReminder,
        NotificationRuleKind.WorkoutReminder => NotificationType.WorkoutReminder,
        _ => NotificationType.DailyMotivation,
    };

    public static string DedupeKey(long ruleId, long userId, DateOnly localDate) => $"rule:{ruleId}:{userId}:{localDate:yyyy-MM-dd}";

    public async Task<int> PlanDueRulesAsync(DateTime nowUtc, CancellationToken cancellationToken = default)
    {
        var rules = await _unitOfWork.Repository<NotificationRule>().GetQueryable()
            .Where(r => r.Status == NotificationRuleStatus.Active)
            .ToListAsync(cancellationToken);
        if (rules.Count == 0)
        {
            return 0;
        }

        var ruleIds = rules.Select(r => r.Id).ToList();
        var activeTemplates = _unitOfWork.Repository<NotificationTemplate>().GetQueryable().Where(t => t.IsActive);
        var templateLinks = await _unitOfWork.Repository<NotificationRuleTemplate>().GetQueryable()
            .Where(rt => ruleIds.Contains(rt.NotificationRuleId))
            .Join(activeTemplates, rt => rt.NotificationTemplateId, t => t.Id, (rt, t) => new { rt.NotificationRuleId, Template = t })
            .ToListAsync(cancellationToken);
        var templatesByRule = templateLinks
            .GroupBy(x => x.NotificationRuleId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.Template).Where(t => t.Type == TypeFor(rules.First(r => r.Id == g.Key).Kind)).OrderBy(t => t.Id).ToList());

        // Push hatirlatmalari yalnizca push alabilecek (aktif cihazi olan) aktif kullanicilar icin planlanir.
        var devices = _unitOfWork.Repository<UserDevice>().GetQueryable().Where(d => d.IsActive);
        var candidates = await _unitOfWork.Repository<ApplicationUser>().GetQueryable()
            .Where(u => u.IsActive && u.DeletedAt == null && u.Role == UserRole.User && devices.Any(d => d.UserId == u.Id))
            .Select(u => u.Id)
            .ToListAsync(cancellationToken);
        if (candidates.Count == 0)
        {
            return 0;
        }

        var timeZones = await _unitOfWork.Repository<UserPreference>().GetQueryable()
            .Where(p => candidates.Contains(p.UserId))
            .Select(p => new { p.UserId, p.TimeZone })
            .ToListAsync(cancellationToken);
        var tzByUser = timeZones.GroupBy(x => x.UserId).ToDictionary(g => g.Key, g => g.First().TimeZone);

        var groups = candidates
            .Select(id => (UserId: id, Zone: NotificationClock.Resolve(tzByUser.GetValueOrDefault(id), _options)))
            .GroupBy(x => x.Zone.Id);

        var created = 0;
        foreach (var group in groups)
        {
            var zone = group.First().Zone;
            var localNow = NotificationClock.ToLocal(nowUtc, zone);
            var localDate = DateOnly.FromDateTime(localNow);
            var userIds = group.Select(x => x.UserId).ToList();

            foreach (var rule in rules)
            {
                if (!NotificationClock.IsDayEnabled(rule.DaysOfWeekMask, localDate.DayOfWeek))
                {
                    continue;
                }

                var dueUtc = NotificationClock.ToUtc(localDate, rule.LocalTime, zone);
                if (nowUtc < dueUtc || nowUtc >= dueUtc + _options.RuleSendWindow)
                {
                    continue;
                }

                if (!templatesByRule.TryGetValue(rule.Id, out var templates) || templates.Count == 0)
                {
                    _logger.LogWarning("Notification rule {RuleId} has no active template of type {Type}; skipped.", rule.Id, TypeFor(rule.Kind));
                    continue;
                }

                foreach (var chunk in userIds.Chunk(Math.Max(1, _options.PlanningUserBatchSize)))
                {
                    created += await PlanChunkAsync(rule, templates, chunk, zone, localDate, dueUtc, nowUtc, cancellationToken);
                }
            }
        }

        return created;
    }

    private async Task<int> PlanChunkAsync(
        NotificationRule rule,
        List<NotificationTemplate> templates,
        long[] userIds,
        TimeZoneInfo zone,
        DateOnly localDate,
        DateTime dueUtc,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        var keys = userIds.ToDictionary(id => DedupeKey(rule.Id, id, localDate), id => id);
        var keyList = keys.Keys.ToList();
        var alreadyPlanned = await _unitOfWork.Repository<NotificationDispatch>().GetQueryable()
            .Where(d => keyList.Contains(d.DedupeKey))
            .Select(d => d.DedupeKey)
            .ToListAsync(cancellationToken);
        var remaining = keys.Where(kv => !alreadyPlanned.Contains(kv.Key)).Select(kv => kv.Value).ToList();
        if (remaining.Count == 0)
        {
            return 0;
        }

        var type = TypeFor(rule.Kind);
        var preferences = await _preferences.GetPreferencesAsync(remaining, cancellationToken);
        remaining = remaining.Where(id => !preferences[id].DisabledTypes.Contains(type)).ToList();
        if (remaining.Count == 0)
        {
            return 0;
        }

        var values = new Dictionary<long, Dictionary<string, string?>>();
        string dataJson;
        switch (rule.Kind)
        {
            case NotificationRuleKind.NutritionReminder:
            {
                var mealType = rule.MealType ?? MealType.Breakfast;
                var missing = await _eligibility.GetUsersMissingMealAsync(remaining, localDate, mealType, rule.OnlyUsersWithPlan, cancellationToken);
                foreach (var id in missing)
                {
                    values[id] = new() { [NotificationTemplateRenderer.MealName] = NotificationTemplateRenderer.MealDisplayName(mealType) };
                }
                dataJson = NotificationNavigation.Screen("nutrition", ("mealType", mealType.ToString()), ("date", localDate.ToString("yyyy-MM-dd")));
                break;
            }
            case NotificationRuleKind.WorkoutReminder:
            {
                var (startUtc, endUtc) = NotificationClock.LocalDayBoundsUtc(localDate, zone);
                var incomplete = await _eligibility.GetUsersWithIncompleteWorkoutAsync(remaining, localDate, startUtc, endUtc, cancellationToken);
                foreach (var (id, workout) in incomplete)
                {
                    values[id] = new() { [NotificationTemplateRenderer.WorkoutName] = workout.WorkoutName };
                }
                dataJson = NotificationNavigation.Screen("workoutPlan");
                break;
            }
            default:
                foreach (var id in remaining)
                {
                    values[id] = new();
                }
                dataJson = NotificationNavigation.Screen("dashboard");
                break;
        }

        if (values.Count == 0)
        {
            return 0;
        }

        var eligibleIds = values.Keys.ToList();
        var firstNames = await _unitOfWork.Repository<UserProfile>().GetQueryable()
            .Where(p => eligibleIds.Contains(p.UserId))
            .Select(p => new { p.UserId, p.FirstName })
            .ToListAsync(cancellationToken);
        var firstNameByUser = firstNames.GroupBy(x => x.UserId).ToDictionary(g => g.Key, g => g.First().FirstName);

        // Dunku sablon tekrar edilmesin.
        var yesterday = localDate.AddDays(-1);
        var lastTemplates = await _unitOfWork.Repository<NotificationDispatch>().GetQueryable()
            .Where(d => d.NotificationRuleId == rule.Id && d.LocalDate == yesterday && eligibleIds.Contains(d.UserId))
            .Select(d => new { d.UserId, d.NotificationTemplateId })
            .ToListAsync(cancellationToken);
        var lastTemplateByUser = lastTemplates.GroupBy(x => x.UserId).ToDictionary(g => g.Key, g => g.First().NotificationTemplateId);

        var expiresAtUtc = dueUtc + _options.RuleSendWindow;
        var dispatches = values.Select(kv =>
        {
            var userId = kv.Key;
            var template = PickTemplate(templates, userId, localDate, lastTemplateByUser.GetValueOrDefault(userId));
            kv.Value[NotificationTemplateRenderer.FirstName] = firstNameByUser.GetValueOrDefault(userId);
            var data = rule.Kind == NotificationRuleKind.DailyMotivation && template.DataJson is not null
                ? NotificationNavigation.Normalize(template.DataJson, out _) ?? dataJson
                : dataJson;

            return new NotificationDispatch
            {
                UserId = userId,
                Type = TypeFor(rule.Kind),
                Source = NotificationDispatchSource.Rule,
                NotificationRuleId = rule.Id,
                NotificationTemplateId = template.Id,
                DedupeKey = DedupeKey(rule.Id, userId, localDate),
                Title = Truncate(NotificationTemplateRenderer.Render(template.TitleTemplate, kv.Value), 200),
                Body = Truncate(NotificationTemplateRenderer.Render(template.BodyTemplate, kv.Value), 500),
                DataJson = data,
                LocalDate = localDate,
                ScheduledForUtc = nowUtc,
                NextAttemptAtUtc = nowUtc,
                ExpiresAtUtc = expiresAtUtc,
                Status = NotificationDispatchStatus.Pending,
                CreatedAt = nowUtc,
            };
        }).ToList();

        return await NotificationDispatchWriter.InsertIgnoringDuplicatesAsync(_unitOfWork, dispatches, cancellationToken);
    }

    /// <summary>Kullanici ve gune gore donen, dunku sablonu tekrar etmeyen secim.</summary>
    public static NotificationTemplate PickTemplate(IReadOnlyList<NotificationTemplate> templates, long userId, DateOnly localDate, long? lastTemplateId)
    {
        var index = (int)((localDate.DayNumber + userId) % templates.Count);
        if (templates.Count > 1 && templates[index].Id == lastTemplateId)
        {
            index = (index + 1) % templates.Count;
        }
        return templates[index];
    }

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];
}
