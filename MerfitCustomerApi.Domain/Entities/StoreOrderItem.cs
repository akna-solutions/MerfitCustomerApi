using MerfitCustomerApi.Domain.Common;

namespace MerfitCustomerApi.Domain.Entities;

/// <summary>
/// Siparis kalemi (store.OrderItem). Urun adi, SKU, gorsel, varyant ve fiyatlar siparis anindaki kopyadir; urun
/// sonradan degisse veya pasife alinsa da gecmis siparis ayni kalir.
/// </summary>
public class StoreOrderItem : BaseEntity
{
    public long OrderId { get; set; }

    /// <summary>Kaynak urun; urun kaydi silinirse null olur (kopya alanlar korunur).</summary>
    public long? ProductId { get; set; }

    public string ProductNameSnapshot { get; set; } = string.Empty;

    public string SkuSnapshot { get; set; } = string.Empty;

    public string BrandSnapshot { get; set; } = string.Empty;

    public string? ImageUrlSnapshot { get; set; }

    /// <summary>Okunabilir varyant etiketi (orn. "2 kg · Cikolata").</summary>
    public string? VariantLabel { get; set; }

    public string VariantKey { get; set; } = string.Empty;

    /// <summary>Varyant farki dahil indirimsiz birim fiyat.</summary>
    public decimal UnitListPrice { get; set; }

    /// <summary>Varyant farki dahil satis birim fiyati.</summary>
    public decimal UnitPrice { get; set; }

    public int Quantity { get; set; }

    /// <summary>(UnitListPrice - UnitPrice) * Quantity.</summary>
    public decimal DiscountAmount { get; set; }

    /// <summary>UnitPrice * Quantity.</summary>
    public decimal LineTotal { get; set; }
}
