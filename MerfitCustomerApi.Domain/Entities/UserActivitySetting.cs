using MerfitCustomerApi.Domain.Common;

namespace MerfitCustomerApi.Domain.Entities;

/// <summary>Kullanicinin aktivite (adim sayar) ayarlari. Kullanici basina tek satir.</summary>
public class UserActivitySetting : BaseEntity
{
    public long UserId { get; set; }

    /// <summary>Gunluk adim hedefi.</summary>
    public int DailyStepGoal { get; set; }
}
