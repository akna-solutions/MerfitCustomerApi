using System.ComponentModel.DataAnnotations;

namespace MerfitCustomerApi.Business.Dtos.Customer.Store;

public class StoreAddressDto
{
    public long Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string RecipientName { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string District { get; set; } = string.Empty;
    public string AddressLine { get; set; } = string.Empty;
    public string? PostalCode { get; set; }
    public bool IsDefault { get; set; }
}

public class UpsertStoreAddressRequest
{
    [Required(ErrorMessage = "Adres başlığı zorunludur.")]
    [StringLength(50, MinimumLength = 2, ErrorMessage = "Adres başlığı 2-50 karakter olmalıdır.")]
    public string Title { get; set; } = string.Empty;

    [Required(ErrorMessage = "Alıcı adı zorunludur.")]
    [StringLength(120, MinimumLength = 5, ErrorMessage = "Ad ve soyad 5-120 karakter olmalıdır.")]
    public string RecipientName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Telefon zorunludur.")]
    [StringLength(20, ErrorMessage = "Telefon en fazla 20 karakter olabilir.")]
    public string PhoneNumber { get; set; } = string.Empty;

    [Required(ErrorMessage = "İl zorunludur.")]
    [StringLength(60, MinimumLength = 2, ErrorMessage = "İl 2-60 karakter olmalıdır.")]
    public string City { get; set; } = string.Empty;

    [Required(ErrorMessage = "İlçe zorunludur.")]
    [StringLength(60, MinimumLength = 2, ErrorMessage = "İlçe 2-60 karakter olmalıdır.")]
    public string District { get; set; } = string.Empty;

    [Required(ErrorMessage = "Açık adres zorunludur.")]
    [StringLength(500, MinimumLength = 10, ErrorMessage = "Açık adres 10-500 karakter olmalıdır.")]
    public string AddressLine { get; set; } = string.Empty;

    [RegularExpression(@"^\d{5}$", ErrorMessage = "Posta kodu 5 haneli olmalıdır.")]
    public string? PostalCode { get; set; }

    public bool IsDefault { get; set; }
}
