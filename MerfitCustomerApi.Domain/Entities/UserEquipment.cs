using MerfitCustomerApi.Domain.Common;

namespace MerfitCustomerApi.Domain.Entities;

/// <summary>
/// UserEquipment varligini temsil eder; bir kullanicinin sahip oldugu/erisebildigi ekipmanlari ifade eder.
/// Onboarding surecinde "What equipment do you have?" adiminda toplanir.
/// </summary>
public class UserEquipment : BaseEntity
{
    /// <summary>
    /// Iliskili kullanicinin kimligi.
    /// </summary>
    public long UserId { get; set; }

    /// <summary>
    /// Iliskili ekipmanin kimligi.
    /// </summary>
    public long EquipmentId { get; set; }
}