using MerfitCustomerApi.Domain.Common;
using MerfitCustomerApi.Domain.Entities.Enums;

namespace MerfitCustomerApi.Domain.Entities;

/// <summary>
/// MB Shop siparisi (store.Order). Tutarlar siparis aninda sunucuda hesaplanip saklanir; istemciden gelmez.
/// Teslimat adresi siparise ait degismez bir JSON kopyadir.
/// </summary>
public class StoreOrder : BaseEntity
{
    /// <summary>Musteriye gosterilen benzersiz numara (orn. "MBF-261029-7KQ2XD").</summary>
    public string OrderNumber { get; set; } = string.Empty;

    public long UserId { get; set; }

    /// <summary>Lojistik durum. Iyimser eszamanlilik belirtecidir: ayni siparis iki kez iptal edilip stok iki kez iade edilemez.</summary>
    public StoreOrderStatus Status { get; set; }

    public StorePaymentStatus PaymentStatus { get; set; }

    public StorePaymentMethod PaymentMethod { get; set; }

    /// <summary>Liste fiyatlariyla urun toplami.</summary>
    public decimal Subtotal { get; set; }

    /// <summary>Liste fiyati ile satis fiyati farki.</summary>
    public decimal DiscountAmount { get; set; }

    public decimal ShippingCost { get; set; }

    /// <summary>Subtotal - DiscountAmount + ShippingCost.</summary>
    public decimal TotalAmount { get; set; }

    /// <summary>ISO 4217 (orn. "TRY").</summary>
    public string Currency { get; set; } = "TRY";

    /// <summary>Teslimat adresinin siparis anindaki kopyasi (JSON).</summary>
    public string ShippingAddressSnapshot { get; set; } = string.Empty;

    /// <summary>Istemcinin her siparis denemesi icin urettigi anahtar; (UserId, IdempotencyKey) benzersizdir.</summary>
    public string IdempotencyKey { get; set; } = string.Empty;

    /// <summary>Odeme saglayicisinin adi (kartla odeme eklendiginde).</summary>
    public string? PaymentProvider { get; set; }

    /// <summary>Saglayici tarafindaki odeme/islem kimligi.</summary>
    public string? PaymentReference { get; set; }

    public DateTime? PaidAt { get; set; }

    public DateTime? CancelledAt { get; set; }

    public string? CancelReason { get; set; }

    public List<StoreOrderItem> Items { get; set; } = new();
}
