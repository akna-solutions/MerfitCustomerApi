using MerfitCustomerApi.Domain.Common;

namespace MerfitCustomerApi.Domain.Entities;

/// <summary>Urun gorseli (store.ProductImage). Gorseller herkese acik https adresleridir.</summary>
public class ProductImage : BaseEntity
{
    public long ProductId { get; set; }

    public string ImageUrl { get; set; } = string.Empty;

    public int SortOrder { get; set; }

    /// <summary>Listelerde gosterilen ana gorsel; urun basina en fazla bir tane.</summary>
    public bool IsPrimary { get; set; }
}
