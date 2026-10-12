using MerfitCustomerApi.Domain.Common;
using MerfitCustomerApi.Domain.Entities.Enums;

namespace MerfitCustomerApi.Domain.Entities;

/// <summary>
/// Zamanlanmis bildirim kurali (beslenme/antrenman hatirlatmasi, gunluk motivasyon). Planlayici
/// her kullanicinin kendi saat diliminde LocalTime gectiginde uygunlugu degerlendirir ve kullanici,
/// kural ve gun basina en fazla bir gonderim kaydi olusturur.
/// </summary>
public class NotificationRule : BaseEntity
{
    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public NotificationRuleKind Kind { get; set; }

    public NotificationRuleStatus Status { get; set; } = NotificationRuleStatus.Active;

    /// <summary>Kullanicinin yerel saatiyle gonderim saati.</summary>
    public TimeOnly LocalTime { get; set; }

    /// <summary>Kuralin calistigi gunler (bit maskesi, bit = (int)DayOfWeek; 127 = her gun).</summary>
    public int DaysOfWeekMask { get; set; } = 127;

    /// <summary>Yalnizca NutritionReminder: hangi ogun.</summary>
    public MealType? MealType { get; set; }

    /// <summary>
    /// Yalnizca NutritionReminder: true ise yalnizca aktif beslenme planinda o gun bu ogun olan
    /// kullanicilara; false ise plani olmayan kullanicilara da hatirlatilir.
    /// </summary>
    public bool OnlyUsersWithPlan { get; set; } = true;
}

/// <summary>Kurala bagli sablonlar; planlayici kullaniciya ayni sablonu art arda gondermemek icin aralarinda doner.</summary>
public class NotificationRuleTemplate : BaseEntity
{
    public long NotificationRuleId { get; set; }

    public long NotificationTemplateId { get; set; }
}
