using MerfitCustomerApi.Business.Common.Pagination;
using MerfitCustomerApi.Business.Services.Store;

namespace MerfitCustomerApi.Business.Dtos.Customer.Store;

public class StoreCategoryDto
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? ImageUrl { get; set; }
    public string? Icon { get; set; }
    public long? ParentCategoryId { get; set; }

    /// <summary>Kategorideki aktif urun sayisi.</summary>
    public int ProductCount { get; set; }
}

/// <summary>Liste/kart gosterimi icin urun ozeti. Fiyatlar sunucuda hesaplanir.</summary>
public class StoreProductListItemDto
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string Brand { get; set; } = string.Empty;
    public string Sku { get; set; } = string.Empty;
    public long CategoryId { get; set; }

    /// <summary>Satis fiyati (indirim varsa indirimli).</summary>
    public decimal Price { get; set; }

    /// <summary>Liste fiyati; indirim yoksa Price ile aynidir.</summary>
    public decimal OriginalPrice { get; set; }

    public int DiscountPercentage { get; set; }
    public int StockQuantity { get; set; }
    public bool InStock { get; set; }

    /// <summary>Ana gorsel; yoksa null (mobil yedek gorunum cizer).</summary>
    public string? ImageUrl { get; set; }

    /// <summary>true ise sepete eklemeden once secenek secilmelidir.</summary>
    public bool HasVariants { get; set; }

    public bool IsFeatured { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class StoreProductDetailDto : StoreProductListItemDto
{
    public string Description { get; set; } = string.Empty;
    public string? CategoryName { get; set; }
    public List<string> Images { get; set; } = new();
    public List<StoreVariantGroup> Variants { get; set; } = new();
    public List<string> Features { get; set; } = new();
    public List<StoreSpec> Specs { get; set; } = new();
}

/// <summary>
/// GET /api/store/products sorgu parametreleri. Page/PageSize PagedRequest'ten gelir (en fazla 100).
/// </summary>
public class StoreProductQuery : PagedRequest
{
    /// <summary>Ad, marka veya SKU icinde aranir.</summary>
    public string? Q { get; set; }

    public long? CategoryId { get; set; }

    /// <summary>Yalnizca bu kampanyanin urunleri.</summary>
    public long? CampaignId { get; set; }

    /// <summary>"featured" | "discounted" | "new" | "bestsellers".</summary>
    public string? Collection { get; set; }

    /// <summary>Virgulle ayrilmis marka listesi.</summary>
    public string? Brands { get; set; }

    public decimal? MinPrice { get; set; }
    public decimal? MaxPrice { get; set; }
    public bool InStockOnly { get; set; }
    public bool DiscountedOnly { get; set; }

    /// <summary>"featured" | "price_asc" | "price_desc" | "discount" | "newest" | "name_asc" | "bestsellers".</summary>
    public string? Sort { get; set; }

    /// <summary>Haric tutulacak urun (benzer urunler listesinde urunun kendisi).</summary>
    public long? ExcludeId { get; set; }
}

public class StoreCampaignDto
{
    public long Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? ImageUrl { get; set; }
    public string? Tint { get; set; }

    /// <summary>Kampanyadaki aktif urunlerin en yuksek indirim orani (urun fiyatlarindan turetilir).</summary>
    public int DiscountPercentage { get; set; }

    public int ProductCount { get; set; }

    /// <summary>Kartta fiyati gosterilen urun (en yuksek indirimli).</summary>
    public StoreProductListItemDto? FeaturedProduct { get; set; }
}
