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
/// Gonderim kuyrugunun tek gondericisi. Her turda zamani gelen kayitlari veritabaninda atomik olarak
/// sahiplenir (INotificationQueueStore), ardindan HER kayit icin gonderimden hemen once:
///  1. suresi dolmus mu, kullanici aktif mi, kural/kampanya hala gecerli mi,
///  2. kural kosulu hala saglaniyor mu (ogun kaydedilmemis / antrenman tamamlanmamis),
///  3. kullanici bu turu kapatmis mi, sessiz saatte mi (hatirlatmalar bastirilir, digerleri ertelenir),
/// kontrol eder. Uygunsa uygulama ici bildirimi (Notification) bir kez olusturur; push acik, aktif cihaz
/// var ve gunluk limit asilmamissa saglayiciya gonderir. Gecici saglayici hatalari ussel geri cekilmeyle
/// MaxDeliveryAttempts'e kadar yeniden denenir; daha once kabul edilmis cihazlara tekrar gonderilmez.
/// </summary>
public class NotificationDispatchProcessor : INotificationDispatchProcessor
{
    public const string ReasonExpired = "Expired";
    public const string ReasonUserInactive = "UserInactive";
    public const string ReasonRuleNotActive = "RuleNotActive";
    public const string ReasonCampaignCancelled = "CampaignCancelled";
    public const string ReasonConditionMet = "ConditionMet";
    public const string ReasonPreferenceDisabled = "PreferenceDisabled";
    public const string ReasonQuietHours = "QuietHours";
    public const string ReasonQuietHoursDeferred = "QuietHoursDeferred";
    public const string ReasonPushDisabled = "PushDisabled";
    public const string ReasonNoDevice = "NoDevice";
    public const string ReasonDailyLimit = "DailyLimit";
    public const string ReasonDevicesInvalid = "DevicesInvalid";
    public const string ReasonProviderRejected = "ProviderRejected";
    public const string ReasonRetriesExhausted = "RetriesExhausted";
    public const string ReasonRetryScheduled = "RetryScheduled";

    private readonly IUnitOfWork _unitOfWork;
    private readonly INotificationQueueStore _queueStore;
    private readonly INotificationEligibilityService _eligibility;
    private readonly INotificationPreferenceReader _preferences;
    private readonly IPushNotificationService _push;
    private readonly NotificationOptions _options;
    private readonly ILogger<NotificationDispatchProcessor> _logger;

    public NotificationDispatchProcessor(
        IUnitOfWork unitOfWork,
        INotificationQueueStore queueStore,
        INotificationEligibilityService eligibility,
        INotificationPreferenceReader preferences,
        IPushNotificationService push,
        IOptions<NotificationOptions> options,
        ILogger<NotificationDispatchProcessor> logger)
    {
        _unitOfWork = unitOfWork;
        _queueStore = queueStore;
        _eligibility = eligibility;
        _preferences = preferences;
        _push = push;
        _options = options.Value;
        _logger = logger;
    }

    private sealed class Context
    {
        public required Dictionary<long, (bool IsActive, DateTime? DeletedAt)> Users { get; init; }
        public required IReadOnlyDictionary<long, UserNotificationPreferences> Preferences { get; init; }
        public required Dictionary<long, List<UserDevice>> DevicesByUser { get; init; }
        public required Dictionary<long, NotificationRule> Rules { get; init; }
        public required Dictionary<long, NotificationCampaignStatus> Campaigns { get; init; }
        public required HashSet<(long DispatchId, long DeviceId)> AlreadyAccepted { get; init; }
        public required Dictionary<long, List<DateTime>> SentPushTimes { get; init; }
        public required HashSet<long> RuleConditionStillHolds { get; init; }
        public required HashSet<NotificationType> LimitExemptTypes { get; init; }
    }

    private sealed record PushWork(NotificationDispatch Dispatch, List<UserDevice> Devices);

    public async Task<int> ProcessDueAsync(DateTime nowUtc, CancellationToken cancellationToken = default)
    {
        var lockToken = Guid.NewGuid();
        var claimedIds = await _queueStore.ClaimDueDispatchesAsync(nowUtc, _options.DispatchBatchSize, _options.DispatchLockDuration, lockToken, cancellationToken);
        if (claimedIds.Count == 0)
        {
            return 0;
        }

        var ids = claimedIds.ToList();
        var dispatches = await _unitOfWork.Repository<NotificationDispatch>().GetQueryable(asNoTracking: false)
            .Where(d => ids.Contains(d.Id) && d.LockToken == lockToken)
            .OrderBy(d => d.Id)
            .ToListAsync(cancellationToken);
        if (dispatches.Count == 0)
        {
            return 0;
        }

        var context = await LoadContextAsync(dispatches, nowUtc, cancellationToken);

        var pushWork = new List<PushWork>();
        var newInbox = new List<(NotificationDispatch Dispatch, Notification Notification)>();
        foreach (var dispatch in dispatches)
        {
            try
            {
                var work = await EvaluateAsync(dispatch, context, nowUtc, newInbox, cancellationToken);
                if (work is not null)
                {
                    pushWork.Add(work);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Notification dispatch {DispatchId} evaluation failed.", dispatch.Id);
                ScheduleRetryOrFail(dispatch, nowUtc, ex.Message);
            }
        }

        // Uygulama ici bildirimler kimlik alsin diye once kaydedilir; push "data" alani notificationId icerir.
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        foreach (var (dispatch, notification) in newInbox)
        {
            dispatch.NotificationId = notification.Id;
        }

        if (pushWork.Count > 0)
        {
            await SendPushAsync(pushWork, context, nowUtc, cancellationToken);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return dispatches.Count;
    }

    private async Task<Context> LoadContextAsync(List<NotificationDispatch> dispatches, DateTime nowUtc, CancellationToken cancellationToken)
    {
        var userIds = dispatches.Select(d => d.UserId).Distinct().ToList();
        var dispatchIds = dispatches.Select(d => d.Id).ToList();

        var users = await _unitOfWork.Repository<ApplicationUser>().GetQueryable()
            .Where(u => userIds.Contains(u.Id))
            .Select(u => new { u.Id, u.IsActive, u.DeletedAt })
            .ToListAsync(cancellationToken);

        var preferences = await _preferences.GetPreferencesAsync(userIds, cancellationToken);

        var devices = await _unitOfWork.Repository<UserDevice>().GetQueryable(asNoTracking: false)
            .Where(d => userIds.Contains(d.UserId) && d.IsActive)
            .ToListAsync(cancellationToken);

        var ruleIds = dispatches.Where(d => d.NotificationRuleId.HasValue).Select(d => d.NotificationRuleId!.Value).Distinct().ToList();
        var rules = ruleIds.Count == 0
            ? new List<NotificationRule>()
            : await _unitOfWork.Repository<NotificationRule>().GetQueryable().Where(r => ruleIds.Contains(r.Id)).ToListAsync(cancellationToken);

        var campaignIds = dispatches.Where(d => d.NotificationCampaignId.HasValue).Select(d => d.NotificationCampaignId!.Value).Distinct().ToList();
        var campaigns = campaignIds.Count == 0
            ? new List<(long Id, NotificationCampaignStatus Status)>()
            : (await _unitOfWork.Repository<NotificationCampaign>().GetQueryable()
                .Where(c => campaignIds.Contains(c.Id))
                .Select(c => new { c.Id, c.Status })
                .ToListAsync(cancellationToken)).Select(c => (c.Id, c.Status)).ToList();

        var accepted = await _unitOfWork.Repository<NotificationDeliveryAttempt>().GetQueryable()
            .Where(a => dispatchIds.Contains(a.NotificationDispatchId)
                && (a.Status == NotificationDeliveryStatus.Accepted || a.Status == NotificationDeliveryStatus.ReceiptOk))
            .Select(a => new { a.NotificationDispatchId, a.UserDeviceId })
            .ToListAsync(cancellationToken);

        var exempt = _options.DailyLimitExemptTypes
            .Select(t => Enum.TryParse<NotificationType>(t, true, out var type) ? type : (NotificationType?)null)
            .Where(t => t.HasValue)
            .Select(t => t!.Value)
            .ToHashSet();

        // Gunluk limit: son 36 saatte push'u kabul edilmis (Sent) ve limite tabi gonderimler; yerel gun filtresi asagida.
        var since = nowUtc.AddHours(-36);
        var exemptList = exempt.ToList();
        var sent = await _unitOfWork.Repository<NotificationDispatch>().GetQueryable()
            .Where(d => userIds.Contains(d.UserId)
                && d.Status == NotificationDispatchStatus.Sent
                && d.ProcessedAtUtc >= since
                && !exemptList.Contains(d.Type))
            .Select(d => new { d.UserId, ProcessedAtUtc = d.ProcessedAtUtc!.Value })
            .ToListAsync(cancellationToken);

        var context = new Context
        {
            Users = users.ToDictionary(u => u.Id, u => (u.IsActive, u.DeletedAt)),
            Preferences = preferences,
            DevicesByUser = devices.GroupBy(d => d.UserId).ToDictionary(g => g.Key, g => g.ToList()),
            Rules = rules.ToDictionary(r => r.Id),
            Campaigns = campaigns.ToDictionary(c => c.Id, c => c.Status),
            AlreadyAccepted = accepted.Select(a => (a.NotificationDispatchId, a.UserDeviceId)).ToHashSet(),
            SentPushTimes = sent.GroupBy(s => s.UserId).ToDictionary(g => g.Key, g => g.Select(x => x.ProcessedAtUtc).ToList()),
            RuleConditionStillHolds = new HashSet<long>(),
            LimitExemptTypes = exempt,
        };

        await EvaluateRuleConditionsAsync(dispatches, context, cancellationToken);
        return context;
    }

    /// <summary>Kural gonderimlerinin kosulunu (ogun kaydedilmemis / antrenman tamamlanmamis) toplu olarak yeniden degerlendirir.</summary>
    private async Task EvaluateRuleConditionsAsync(List<NotificationDispatch> dispatches, Context context, CancellationToken cancellationToken)
    {
        var ruleDispatches = dispatches
            .Where(d => d.Source == NotificationDispatchSource.Rule && d.NotificationRuleId.HasValue && d.LocalDate.HasValue)
            .Where(d => context.Rules.ContainsKey(d.NotificationRuleId!.Value))
            .GroupBy(d => (RuleId: d.NotificationRuleId!.Value, LocalDate: d.LocalDate!.Value, Zone: ZoneFor(d.UserId, context).Id));

        foreach (var group in ruleDispatches)
        {
            var rule = context.Rules[group.Key.RuleId];
            var userIds = group.Select(d => d.UserId).Distinct().ToList();
            switch (rule.Kind)
            {
                case NotificationRuleKind.NutritionReminder:
                {
                    var missing = await _eligibility.GetUsersMissingMealAsync(userIds, group.Key.LocalDate, rule.MealType ?? MealType.Breakfast, rule.OnlyUsersWithPlan, cancellationToken);
                    foreach (var d in group.Where(d => missing.Contains(d.UserId)))
                    {
                        context.RuleConditionStillHolds.Add(d.Id);
                    }
                    break;
                }
                case NotificationRuleKind.WorkoutReminder:
                {
                    var zone = ZoneFor(userIds[0], context);
                    var (startUtc, endUtc) = NotificationClock.LocalDayBoundsUtc(group.Key.LocalDate, zone);
                    var incomplete = await _eligibility.GetUsersWithIncompleteWorkoutAsync(userIds, group.Key.LocalDate, startUtc, endUtc, cancellationToken);
                    foreach (var d in group.Where(d => incomplete.ContainsKey(d.UserId)))
                    {
                        context.RuleConditionStillHolds.Add(d.Id);
                    }
                    break;
                }
                default:
                    foreach (var d in group)
                    {
                        context.RuleConditionStillHolds.Add(d.Id);
                    }
                    break;
            }
        }
    }

    private TimeZoneInfo ZoneFor(long userId, Context context) =>
        NotificationClock.Resolve(context.Preferences.TryGetValue(userId, out var p) ? p.TimeZone : null, _options);

    /// <summary>Kaydi sonuclandirir veya push'a hazirlar. Push gerekiyorsa PushWork dondurur.</summary>
    private async Task<PushWork?> EvaluateAsync(NotificationDispatch dispatch, Context context, DateTime nowUtc, List<(NotificationDispatch, Notification)> newInbox, CancellationToken cancellationToken)
    {
        if (dispatch.ExpiresAtUtc.HasValue && dispatch.ExpiresAtUtc.Value <= nowUtc)
        {
            Finish(dispatch, NotificationDispatchStatus.Suppressed, ReasonExpired, nowUtc);
            return null;
        }

        if (!context.Users.TryGetValue(dispatch.UserId, out var user) || !user.IsActive || user.DeletedAt.HasValue)
        {
            Finish(dispatch, NotificationDispatchStatus.Suppressed, ReasonUserInactive, nowUtc);
            return null;
        }

        switch (dispatch.Source)
        {
            case NotificationDispatchSource.Rule:
                if (!dispatch.NotificationRuleId.HasValue
                    || !context.Rules.TryGetValue(dispatch.NotificationRuleId.Value, out var rule)
                    || rule.Status != NotificationRuleStatus.Active)
                {
                    Finish(dispatch, NotificationDispatchStatus.Cancelled, ReasonRuleNotActive, nowUtc);
                    return null;
                }

                if (!context.RuleConditionStillHolds.Contains(dispatch.Id))
                {
                    Finish(dispatch, NotificationDispatchStatus.Cancelled, ReasonConditionMet, nowUtc);
                    return null;
                }
                break;

            case NotificationDispatchSource.Campaign:
                if (dispatch.NotificationCampaignId.HasValue
                    && context.Campaigns.TryGetValue(dispatch.NotificationCampaignId.Value, out var campaignStatus)
                    && campaignStatus == NotificationCampaignStatus.Cancelled)
                {
                    Finish(dispatch, NotificationDispatchStatus.Cancelled, ReasonCampaignCancelled, nowUtc);
                    return null;
                }
                break;
        }

        var preferences = context.Preferences[dispatch.UserId];
        if (preferences.DisabledTypes.Contains(dispatch.Type))
        {
            Finish(dispatch, NotificationDispatchStatus.Suppressed, ReasonPreferenceDisabled, nowUtc);
            return null;
        }

        var zone = ZoneFor(dispatch.UserId, context);
        var localTime = TimeOnly.FromDateTime(NotificationClock.ToLocal(nowUtc, zone));
        if (NotificationClock.IsWithinQuietHours(localTime, preferences.QuietHoursStart, preferences.QuietHoursEnd))
        {
            if (dispatch.Source == NotificationDispatchSource.Rule)
            {
                // Saate bagli hatirlatmalar sessiz saat sonrasina ertelenmez; bayatlar. Uygulama ici kayit
                // onceki bir denemede olustuysa yalnizca push birakilir.
                Finish(dispatch, dispatch.NotificationId is null ? NotificationDispatchStatus.Suppressed : NotificationDispatchStatus.InAppOnly, ReasonQuietHours, nowUtc);
                return null;
            }

            var resumeAt = NotificationClock.QuietHoursEndUtc(nowUtc, zone, preferences.QuietHoursStart!.Value, preferences.QuietHoursEnd!.Value);
            if (dispatch.ExpiresAtUtc.HasValue && resumeAt >= dispatch.ExpiresAtUtc.Value)
            {
                Finish(dispatch, NotificationDispatchStatus.Suppressed, ReasonQuietHours, nowUtc);
                return null;
            }

            Release(dispatch, NotificationDispatchStatus.Pending, ReasonQuietHoursDeferred, resumeAt, nowUtc);
            return null;
        }

        if (dispatch.NotificationId is null)
        {
            var notification = new Notification
            {
                UserId = dispatch.UserId,
                Type = dispatch.Type,
                Title = dispatch.Title,
                Body = Truncate(dispatch.Body, 300)!,
                DataJson = dispatch.DataJson,
                IsRead = false,
                ExpiresAt = dispatch.Source == NotificationDispatchSource.Rule ? null : dispatch.ExpiresAtUtc,
                CreatedAt = nowUtc,
            };
            await _unitOfWork.Repository<Notification>().AddAsync(notification, cancellationToken);
            newInbox.Add((dispatch, notification));
        }

        if (!preferences.PushEnabled)
        {
            Finish(dispatch, NotificationDispatchStatus.InAppOnly, ReasonPushDisabled, nowUtc);
            return null;
        }

        var devices = context.DevicesByUser.GetValueOrDefault(dispatch.UserId)?
            .Where(d => d.IsActive && !context.AlreadyAccepted.Contains((dispatch.Id, d.Id)))
            .ToList() ?? new List<UserDevice>();
        var anyAccepted = context.AlreadyAccepted.Any(a => a.DispatchId == dispatch.Id);
        if (devices.Count == 0)
        {
            if (anyAccepted)
            {
                Finish(dispatch, NotificationDispatchStatus.Sent, null, nowUtc);
            }
            else
            {
                Finish(dispatch, NotificationDispatchStatus.InAppOnly, ReasonNoDevice, nowUtc);
            }
            return null;
        }

        if (!anyAccepted && !context.LimitExemptTypes.Contains(dispatch.Type))
        {
            var dayStartUtc = NotificationClock.LocalDayBoundsUtc(DateOnly.FromDateTime(NotificationClock.ToLocal(nowUtc, zone)), zone).StartUtc;
            var sentTimes = context.SentPushTimes.GetValueOrDefault(dispatch.UserId);
            var sentToday = sentTimes?.Count(t => t >= dayStartUtc) ?? 0;
            if (sentToday >= _options.DailyPushLimit)
            {
                Finish(dispatch, NotificationDispatchStatus.InAppOnly, ReasonDailyLimit, nowUtc);
                return null;
            }

            // Ayni turdaki sonraki kayitlar da bu gonderimi saysin.
            if (!context.SentPushTimes.ContainsKey(dispatch.UserId))
            {
                context.SentPushTimes[dispatch.UserId] = new List<DateTime>();
            }
            context.SentPushTimes[dispatch.UserId].Add(nowUtc);
        }

        return new PushWork(dispatch, devices);
    }

    private async Task SendPushAsync(List<PushWork> work, Context context, DateTime nowUtc, CancellationToken cancellationToken)
    {
        var messages = new List<PushMessage>();
        var targets = new List<(NotificationDispatch Dispatch, UserDevice Device)>();
        foreach (var item in work)
        {
            var data = NotificationNavigation.ToDictionary(item.Dispatch.DataJson);
            if (item.Dispatch.NotificationId.HasValue)
            {
                data["notificationId"] = item.Dispatch.NotificationId.Value.ToString();
            }
            data["type"] = item.Dispatch.Type.ToString();

            foreach (var device in item.Devices)
            {
                messages.Add(new PushMessage(device.DeviceToken, item.Dispatch.Title, item.Dispatch.Body, data));
                targets.Add((item.Dispatch, device));
            }
        }

        IReadOnlyList<PushSendResult> results;
        try
        {
            results = await _push.SendAsync(messages, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Push provider call failed.");
            results = Enumerable.Repeat(new PushSendResult(PushSendOutcome.Transient, ErrorCode: PushErrorCodes.Network, ErrorMessage: Truncate(ex.Message, 500)), messages.Count).ToList();
        }

        var attempts = _unitOfWork.Repository<NotificationDeliveryAttempt>();
        var outcomes = new Dictionary<long, List<PushSendResult>>();
        for (var i = 0; i < targets.Count; i++)
        {
            var (dispatch, device) = targets[i];
            var result = i < results.Count ? results[i] : new PushSendResult(PushSendOutcome.Transient, ErrorCode: "MissingResult");

            await attempts.AddAsync(new NotificationDeliveryAttempt
            {
                NotificationDispatchId = dispatch.Id,
                UserDeviceId = device.Id,
                AttemptNumber = dispatch.AttemptCount + 1,
                Status = result.Outcome switch
                {
                    PushSendOutcome.Accepted => NotificationDeliveryStatus.Accepted,
                    PushSendOutcome.Rejected => NotificationDeliveryStatus.Rejected,
                    _ => NotificationDeliveryStatus.TransientError,
                },
                ProviderMessageId = result.ProviderMessageId,
                ErrorCode = result.ErrorCode,
                ErrorMessage = result.ErrorMessage,
                CreatedAt = nowUtc,
            }, cancellationToken);

            if (result.Outcome == PushSendOutcome.Rejected && (result.IsDeviceUnregistered || result.ErrorCode == PushErrorCodes.InvalidToken))
            {
                NotificationDeviceService.Deactivate(device, NotificationDeviceService.ReasonDeviceNotRegistered, nowUtc);
            }

            if (!outcomes.TryGetValue(dispatch.Id, out var list))
            {
                outcomes[dispatch.Id] = list = new List<PushSendResult>();
            }
            list.Add(result);
        }

        foreach (var item in work)
        {
            var dispatch = item.Dispatch;
            var results2 = outcomes.GetValueOrDefault(dispatch.Id) ?? new List<PushSendResult>();
            dispatch.AttemptCount += 1;

            if (results2.Any(r => r.Outcome == PushSendOutcome.Accepted) || context.AlreadyAccepted.Any(a => a.DispatchId == dispatch.Id))
            {
                Finish(dispatch, NotificationDispatchStatus.Sent, null, nowUtc);
            }
            else if (results2.Any(r => r.Outcome == PushSendOutcome.Transient))
            {
                ScheduleRetryOrFail(dispatch, nowUtc, results2.First(r => r.Outcome == PushSendOutcome.Transient).ErrorMessage, countsAsAttempt: false);
            }
            else if (item.Devices.All(d => !d.IsActive))
            {
                // Tum token'lar gecersizdi; uygulama ici bildirim olusturuldu.
                Finish(dispatch, NotificationDispatchStatus.InAppOnly, ReasonDevicesInvalid, nowUtc);
            }
            else
            {
                Finish(dispatch, NotificationDispatchStatus.Failed, ReasonProviderRejected, nowUtc, results2.FirstOrDefault()?.ErrorMessage);
            }
        }
    }

    /// <summary>Gecici hata: ussel geri cekilmeyle tekrar dener veya deneme hakki bittiyse Failed yapar.</summary>
    private void ScheduleRetryOrFail(NotificationDispatch dispatch, DateTime nowUtc, string? error, bool countsAsAttempt = true)
    {
        if (countsAsAttempt)
        {
            dispatch.AttemptCount += 1;
        }

        if (dispatch.AttemptCount >= _options.MaxDeliveryAttempts)
        {
            Finish(dispatch, NotificationDispatchStatus.Failed, ReasonRetriesExhausted, nowUtc, error);
            return;
        }

        var nextAttempt = nowUtc + RetryDelay(dispatch.AttemptCount, _options);
        if (dispatch.ExpiresAtUtc.HasValue && nextAttempt >= dispatch.ExpiresAtUtc.Value)
        {
            Finish(dispatch, NotificationDispatchStatus.Failed, ReasonRetriesExhausted, nowUtc, error);
            return;
        }

        Release(dispatch, NotificationDispatchStatus.Pending, ReasonRetryScheduled, nextAttempt, nowUtc);
        dispatch.LastError = Truncate(error, 1000);
    }

    public static TimeSpan RetryDelay(int attemptCount, NotificationOptions options)
    {
        var exponent = Math.Clamp(attemptCount - 1, 0, 20);
        var delay = TimeSpan.FromTicks(options.RetryBaseDelay.Ticks * (1L << exponent));
        return delay > options.RetryMaxDelay ? options.RetryMaxDelay : delay;
    }

    private static void Finish(NotificationDispatch dispatch, NotificationDispatchStatus status, string? reason, DateTime nowUtc, string? error = null)
    {
        dispatch.Status = status;
        dispatch.StatusReason = reason;
        dispatch.ProcessedAtUtc = nowUtc;
        dispatch.LockToken = null;
        dispatch.LockedUntilUtc = null;
        dispatch.UpdatedAt = nowUtc;
        if (error is not null)
        {
            dispatch.LastError = Truncate(error, 1000);
        }
    }

    private static void Release(NotificationDispatch dispatch, NotificationDispatchStatus status, string reason, DateTime nextAttemptAtUtc, DateTime nowUtc)
    {
        dispatch.Status = status;
        dispatch.StatusReason = reason;
        dispatch.NextAttemptAtUtc = nextAttemptAtUtc;
        dispatch.LockToken = null;
        dispatch.LockedUntilUtc = null;
        dispatch.UpdatedAt = nowUtc;
    }

    public async Task<int> CheckReceiptsAsync(DateTime nowUtc, CancellationToken cancellationToken = default)
    {
        var readyBefore = nowUtc - _options.ReceiptDelay;
        var oldest = nowUtc - _options.ReceiptMaxAge;
        var attempts = await _unitOfWork.Repository<NotificationDeliveryAttempt>().GetQueryable(asNoTracking: false)
            .Where(a => a.Status == NotificationDeliveryStatus.Accepted
                && a.ReceiptCheckedAtUtc == null
                && a.ProviderMessageId != null
                && a.CreatedAt <= readyBefore)
            .OrderBy(a => a.Id)
            .Take(1000)
            .ToListAsync(cancellationToken);
        if (attempts.Count == 0)
        {
            return 0;
        }

        var receipts = await _push.GetReceiptsAsync(attempts.Select(a => a.ProviderMessageId!).ToList(), cancellationToken);

        var unregisteredDeviceIds = new HashSet<long>();
        foreach (var attempt in attempts)
        {
            if (receipts.TryGetValue(attempt.ProviderMessageId!, out var receipt))
            {
                attempt.Status = receipt.IsOk ? NotificationDeliveryStatus.ReceiptOk : NotificationDeliveryStatus.ReceiptError;
                attempt.ErrorCode = receipt.ErrorCode;
                attempt.ErrorMessage = receipt.ErrorMessage;
                attempt.ReceiptCheckedAtUtc = nowUtc;
                attempt.UpdatedAt = nowUtc;
                if (receipt.IsDeviceUnregistered)
                {
                    unregisteredDeviceIds.Add(attempt.UserDeviceId);
                }
            }
            else if (attempt.CreatedAt < oldest)
            {
                // Saglayici makbuzu artik tutmuyor; kabul edildi olarak kalir, tekrar sorgulanmaz.
                attempt.ReceiptCheckedAtUtc = nowUtc;
                attempt.UpdatedAt = nowUtc;
            }
        }

        if (unregisteredDeviceIds.Count > 0)
        {
            var ids = unregisteredDeviceIds.ToList();
            var devices = await _unitOfWork.Repository<UserDevice>().GetQueryable(asNoTracking: false)
                .Where(d => ids.Contains(d.Id) && d.IsActive)
                .ToListAsync(cancellationToken);
            foreach (var device in devices)
            {
                NotificationDeviceService.Deactivate(device, NotificationDeviceService.ReasonDeviceNotRegistered, nowUtc);
            }
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return attempts.Count;
    }

    public async Task<int> CompleteFinishedCampaignsAsync(DateTime nowUtc, CancellationToken cancellationToken = default)
    {
        var open = new[] { NotificationDispatchStatus.Pending, NotificationDispatchStatus.Processing };
        var dispatches = _unitOfWork.Repository<NotificationDispatch>().GetQueryable();
        var finished = await _unitOfWork.Repository<NotificationCampaign>().GetQueryable(asNoTracking: false)
            .Where(c => c.Status == NotificationCampaignStatus.Dispatching)
            .Where(c => !dispatches.Any(d => d.NotificationCampaignId == c.Id && open.Contains(d.Status)))
            .ToListAsync(cancellationToken);

        foreach (var campaign in finished)
        {
            campaign.Status = NotificationCampaignStatus.Completed;
            campaign.CompletedAtUtc = nowUtc;
            campaign.UpdatedAt = nowUtc;
        }

        if (finished.Count > 0)
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return finished.Count;
    }

    private static string? Truncate(string? value, int max) => value is null || value.Length <= max ? value : value[..max];
}
