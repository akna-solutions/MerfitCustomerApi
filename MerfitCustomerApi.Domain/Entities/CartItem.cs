using MerfitCustomerApi.Domain.Common;

namespace MerfitCustomerApi.Domain.Entities;

/// <summary>
/// Sepet kalemi (store.CartItem). Fiyat SAKLANMAZ: sepet her okundugunda guncel urun fiyatiyla hesaplanir; siparis
/// aninda fiyat yeniden dogrulanip siparis kalemine kopyalanir. Ayni urun + ayni varyant tek satirdir
/// ((CartId, ProductId, VariantKey) benzersiz); tekrar eklemede adet artar.
/// </summary>
public class CartItem : BaseEntity
{
    public long CartId { get; set; }

    public long ProductId { get; set; }

    /// <summary>Secilen varyantlarin siralanmis anahtari (orn. "flavor:choc|package:2kg"); varyant yoksa bos.</summary>
    public string VariantKey { get; set; } = string.Empty;

    /// <summary>Secilen varyantlar; JSON nesne ({groupId: optionId}).</summary>
    public string? SelectedVariantJson { get; set; }

    public int Quantity { get; set; }
}
