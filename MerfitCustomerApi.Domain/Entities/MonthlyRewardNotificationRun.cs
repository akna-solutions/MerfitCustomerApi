using MerfitCustomerApi.Domain.Common;
using MerfitCustomerApi.Domain.Entities.Enums;

namespace MerfitCustomerApi.Domain.Entities;

/// <summary>
/// Aylik liderlik donemi icin odul bildirimi isleme kaydi. Donem basina tek kayit (unique);
/// alicilar mevcut odul dagitiminin olusturdugu UserReward kayitlaridir. Islemenin tekrar
/// calistirilmasi NotificationDispatch.DedupeKey sayesinde yinelenen bildirim uretmez.
/// </summary>
public class MonthlyRewardNotificationRun : BaseEntity
{
    public long LeaderboardPeriodId { get; set; }

    public MonthlyRewardNotificationRunStatus Status { get; set; } = MonthlyRewardNotificationRunStatus.Pending;

    public int AttemptCount { get; set; }

    public int? RecipientCount { get; set; }

    public int? CreatedDispatchCount { get; set; }

    public DateTime? StartedAtUtc { get; set; }

    public DateTime? CompletedAtUtc { get; set; }

    public string? LastError { get; set; }

    public Guid? LockToken { get; set; }

    public DateTime? LockedUntilUtc { get; set; }
}
