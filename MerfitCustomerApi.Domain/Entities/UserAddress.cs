using MerfitCustomerApi.Domain.Common;

namespace MerfitCustomerApi.Domain.Entities;

/// <summary>
/// Kullanicinin teslimat adresi (store.Address). Siparis bu kayda bagli DEGILDIR: siparis aninda adresin degismez bir
/// kopyasi siparise yazilir; adres sonradan degisse/silinse de gecmis siparis etkilenmez.
/// </summary>
public class UserAddress : BaseEntity
{
    public long UserId { get; set; }

    /// <summary>Kullanicinin verdigi ad (Ev, Is...).</summary>
    public string Title { get; set; } = string.Empty;

    public string RecipientName { get; set; } = string.Empty;

    public string PhoneNumber { get; set; } = string.Empty;

    public string City { get; set; } = string.Empty;

    public string District { get; set; } = string.Empty;

    public string AddressLine { get; set; } = string.Empty;

    public string? PostalCode { get; set; }

    /// <summary>Kullanici basina en fazla bir varsayilan adres (filtreli benzersiz index).</summary>
    public bool IsDefault { get; set; }
}
