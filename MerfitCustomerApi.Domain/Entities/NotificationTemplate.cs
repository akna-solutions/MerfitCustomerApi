using MerfitCustomerApi.Domain.Common;
using MerfitCustomerApi.Domain.Entities.Enums;

namespace MerfitCustomerApi.Domain.Entities;

/// <summary>
/// Yeniden kullanilabilir bildirim metni. Baslik/govde {firstName}, {mealName}, {workoutName},
/// {rewardTitle}, {rank}, {periodLabel} yer tutucularini icerebilir (bkz. NotificationTemplateRenderer).
/// </summary>
public class NotificationTemplate : BaseEntity
{
    public string Name { get; set; } = string.Empty;

    /// <summary>Bu sablonla uretilen bildirimin turu (kullanici tercihleri bu ture gore uygulanir).</summary>
    public NotificationType Type { get; set; }

    public string TitleTemplate { get; set; } = string.Empty;

    public string BodyTemplate { get; set; } = string.Empty;

    /// <summary>Opsiyonel yonlendirme verisi (JSON, orn. {"screen":"nutrition"}).</summary>
    public string? DataJson { get; set; }

    public bool IsActive { get; set; } = true;
}
