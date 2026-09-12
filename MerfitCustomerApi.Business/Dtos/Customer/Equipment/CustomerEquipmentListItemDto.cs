namespace MerfitCustomerApi.Business.Dtos.Customer.Equipment;

/// <summary>
/// GET /api/equipment tarafindan donen ekipman ogesi. MerfitNativeApp onboarding akisindaki
/// EquipmentStep, kullaniciya burada gelen "Slug" degerlerini gosterir (dumbbells, barbell, ...)
/// ve secilenlerin "Id" degerlerini RegisterRequest.EquipmentIds icinde geri gonderir.
/// </summary>
public class CustomerEquipmentListItemDto
{
    public long Id { get; set; }

    /// <summary>Ekranda gosterilecek isim (orn. "Dambil").</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Istemcinin sabit kodladigi anahtar (orn. "dumbbells"). RN tarafindaki
    /// Equipment union type'i ile birebir eslesecek sekilde seed edilmelidir.</summary>
    public string Slug { get; set; } = string.Empty;
}