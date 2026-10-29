using System.ComponentModel.DataAnnotations;

namespace MerfitCustomerApi.Business.Dtos.Customer.Store;

public class StoreCheckoutOptionsDto
{
    public List<StorePaymentMethodDto> PaymentMethods { get; set; } = new();
    public decimal FreeShippingThreshold { get; set; }
    public decimal ShippingCost { get; set; }
    public int MaxQuantityPerLine { get; set; }
    public string Currency { get; set; } = "TRY";
}

public class StorePaymentMethodDto
{
    /// <summary>"cash_on_delivery" | "bank_transfer" | "card".</summary>
    public string Method { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool Available { get; set; }
    public string? UnavailableReason { get; set; }
}

public class CreateStoreOrderRequest
{
    [Range(1, long.MaxValue, ErrorMessage = "Teslimat adresi seçin.")]
    public long AddressId { get; set; }

    /// <summary>"cash_on_delivery" | "bank_transfer" | "card".</summary>
    [Required(ErrorMessage = "Ödeme yöntemi seçin.")]
    public string PaymentMethod { get; set; } = string.Empty;

    /// <summary>
    /// Istemcinin bu siparis denemesi icin urettigi anahtar. Ag hatasinda ayni anahtarla tekrar gonderilen istek yeni
    /// siparis olusturmaz; ilk siparis doner.
    /// </summary>
    [Required(ErrorMessage = "İstek anahtarı zorunludur.")]
    [RegularExpression(@"^[A-Za-z0-9_-]{8,64}$", ErrorMessage = "İstek anahtarı geçersiz.")]
    public string IdempotencyKey { get; set; } = string.Empty;

    /// <summary>
    /// Kullanicinin ekranda gordugu genel toplam. Verilirse ve sunucunun hesapladigi tutardan farkliysa siparis
    /// olusturulmaz (409); kullanici guncel tutari gorup tekrar onaylar. Tutar asla bu degerden alinmaz.
    /// </summary>
    [Range(0, 10_000_000)]
    public decimal? ExpectedTotal { get; set; }
}

public class CancelStoreOrderRequest
{
    [StringLength(300, ErrorMessage = "İptal nedeni en fazla 300 karakter olabilir.")]
    public string? Reason { get; set; }
}

public class StoreOrderAddressDto
{
    public string Title { get; set; } = string.Empty;
    public string RecipientName { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string District { get; set; } = string.Empty;
    public string AddressLine { get; set; } = string.Empty;
    public string? PostalCode { get; set; }
}

public class StoreOrderItemDto
{
    public long? ProductId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Brand { get; set; } = string.Empty;
    public string Sku { get; set; } = string.Empty;
    public string? ImageUrl { get; set; }
    public string? VariantLabel { get; set; }
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal UnitOriginalPrice { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal LineTotal { get; set; }
}

public class StoreOrderHistoryDto
{
    public string Status { get; set; } = string.Empty;
    public string PaymentStatus { get; set; } = string.Empty;
    public string? Note { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class StoreBankTransferInfoDto
{
    public string Iban { get; set; } = string.Empty;
    public string AccountName { get; set; } = string.Empty;
    public string? BankName { get; set; }

    /// <summary>Aciklamaya yazilacak referans (siparis numarasi).</summary>
    public string Reference { get; set; } = string.Empty;
}

public class StoreOrderListItemDto
{
    public long Id { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }

    /// <summary>"received" | "preparing" | "shipped" | "delivered" | "cancelled".</summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>"pending" | "paid" | "failed" | "refund_pending" | "refunded" | "cancelled".</summary>
    public string PaymentStatus { get; set; } = string.Empty;

    public string PaymentMethod { get; set; } = string.Empty;
    public decimal Total { get; set; }
    public string Currency { get; set; } = "TRY";
    public int ItemCount { get; set; }

    /// <summary>Ilk birkac urunun adi (liste satirinda gosterilir).</summary>
    public List<string> ItemNames { get; set; } = new();
}

public class StoreOrderDto : StoreOrderListItemDto
{
    public decimal Subtotal { get; set; }
    public decimal Discount { get; set; }
    public decimal ShippingCost { get; set; }
    public StoreOrderAddressDto ShippingAddress { get; set; } = new();
    public List<StoreOrderItemDto> Items { get; set; } = new();
    public List<StoreOrderHistoryDto> History { get; set; } = new();

    /// <summary>Musteri bu siparisi iptal edebilir mi (yalnizca "received" durumunda).</summary>
    public bool CanCancel { get; set; }

    public DateTime? CancelledAt { get; set; }
    public string? CancelReason { get; set; }

    /// <summary>Havale ile odenecek ve odeme bekleyen siparislerde banka bilgisi.</summary>
    public StoreBankTransferInfoDto? BankTransfer { get; set; }
}
