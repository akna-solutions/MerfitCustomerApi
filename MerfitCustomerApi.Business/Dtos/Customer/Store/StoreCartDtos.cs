using System.ComponentModel.DataAnnotations;

namespace MerfitCustomerApi.Business.Dtos.Customer.Store;

/// <summary>
/// Sepet ve toplamlar. Fiyatlar her istekte guncel urun fiyatiyla sunucuda hesaplanir; mobil bu tutarlari gosterir.
/// Satisa kapanan/stogu yetmeyen satirlar IsAvailable=false ile isaretlenir ve toplamlara katilmaz.
/// </summary>
public class StoreCartDto
{
    public List<StoreCartItemDto> Items { get; set; } = new();
    public StoreTotalsDto Totals { get; set; } = new();

    /// <summary>true ise siparis verilebilir (en az bir satir var ve tum satirlar uygun).</summary>
    public bool CanCheckout { get; set; }
}

public class StoreCartItemDto
{
    /// <summary>Sepet kalemi kimligi (adet guncelleme/silme icin).</summary>
    public long Id { get; set; }
    public long ProductId { get; set; }
    public long CategoryId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Brand { get; set; } = string.Empty;
    public string Sku { get; set; } = string.Empty;
    public string? ImageUrl { get; set; }
    public Dictionary<string, string> SelectedVariant { get; set; } = new();
    public string VariantLabel { get; set; } = string.Empty;
    public int Quantity { get; set; }

    /// <summary>Bu satir icin secilebilecek en fazla adet (stok ve satir siniri).</summary>
    public int MaxQuantity { get; set; }

    public decimal UnitPrice { get; set; }
    public decimal UnitOriginalPrice { get; set; }
    public decimal LineTotal { get; set; }
    public decimal LineOriginalTotal { get; set; }
    public bool IsAvailable { get; set; }

    /// <summary>IsAvailable=false ise kullaniciya gosterilecek neden.</summary>
    public string? UnavailableReason { get; set; }
}

public class StoreTotalsDto
{
    public int ItemCount { get; set; }

    /// <summary>Liste fiyatlariyla ara toplam.</summary>
    public decimal Subtotal { get; set; }

    public decimal Discount { get; set; }
    public decimal ShippingCost { get; set; }
    public decimal Total { get; set; }
    public decimal RemainingForFreeShipping { get; set; }
    public decimal FreeShippingThreshold { get; set; }
    public string Currency { get; set; } = "TRY";
}

public class AddCartItemRequest
{
    [Range(1, long.MaxValue, ErrorMessage = "Geçersiz ürün.")]
    public long ProductId { get; set; }

    [Range(1, 100, ErrorMessage = "Adet 1 ile 100 arasında olmalıdır.")]
    public int Quantity { get; set; } = 1;

    /// <summary>{grupId: secenekId}. Fiyat gonderilmez; gonderilse de yok sayilir.</summary>
    [MaxLength(10)]
    public Dictionary<string, string>? SelectedVariant { get; set; }
}

public class UpdateCartItemRequest
{
    /// <summary>0 satiri kaldirir.</summary>
    [Range(0, 100, ErrorMessage = "Adet 0 ile 100 arasında olmalıdır.")]
    public int Quantity { get; set; }
}
