using System.Linq.Expressions;
using MerfitCustomerApi.Business.Dtos.Customer.Store;
using MerfitCustomerApi.Domain.Entities;

namespace MerfitCustomerApi.Business.Services.Store;

/// <summary>Urun entity'sinden musteri DTO'larina donusum (fiyatlar StorePricing'den).</summary>
internal static class StoreProductMapper
{
    /// <summary>
    /// Liste karti icin gereken kolonlar. Performans: urun listesi/favoriler/kampanyalar/oneriler veritabanindan
    /// tum satiri (Description, FeaturesJson, SpecsJson gibi buyuk metin alanlari) cekmez; yalnizca kartta
    /// kullanilan kolonlar okunur. Detay sayfasi tam satiri ayrica yukler.
    /// </summary>
    public static readonly Expression<Func<Product, Product>> ListColumns = p => new Product
    {
        Id = p.Id,
        Name = p.Name,
        Slug = p.Slug,
        Brand = p.Brand,
        Sku = p.Sku,
        CategoryId = p.CategoryId,
        Price = p.Price,
        DiscountPrice = p.DiscountPrice,
        StockQuantity = p.StockQuantity,
        IsActive = p.IsActive,
        IsFeatured = p.IsFeatured,
        SortOrder = p.SortOrder,
        CreatedAt = p.CreatedAt,
        HighlightBadge = p.HighlightBadge,
        VariantsJson = p.VariantsJson,
    };

    public static T Fill<T>(T dto, Product p, string? imageUrl) where T : StoreProductListItemDto
    {
        var sale = StorePricing.SalePrice(p);
        dto.Id = p.Id;
        dto.Name = p.Name;
        dto.Slug = p.Slug;
        dto.Brand = p.Brand;
        dto.Sku = p.Sku;
        dto.CategoryId = p.CategoryId;
        dto.Price = sale;
        dto.OriginalPrice = p.Price;
        dto.DiscountPercentage = StorePricing.DiscountPercent(p.Price, sale);
        dto.StockQuantity = Math.Max(0, p.StockQuantity);
        dto.InStock = p.StockQuantity > 0;
        dto.ImageUrl = StoreUrls.SafeImage(imageUrl);
        dto.HasVariants = StoreJson.ReadList<StoreVariantGroup>(p.VariantsJson).Count > 0;
        dto.IsFeatured = p.IsFeatured;
        dto.CreatedAt = DateTime.SpecifyKind(p.CreatedAt, DateTimeKind.Utc);
        dto.HighlightBadge = p.HighlightBadge;
        return dto;
    }

    public static StoreProductListItemDto ToListItem(Product p, string? imageUrl) => Fill(new StoreProductListItemDto(), p, imageUrl);
}
