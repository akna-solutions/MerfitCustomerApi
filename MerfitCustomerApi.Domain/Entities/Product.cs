using MerfitCustomerApi.Domain.Common;

namespace MerfitCustomerApi.Domain.Entities;

/// <summary>
/// MB Shop urunu (store.Product). Fiyatlar TL cinsinden numeric(12,2) saklanir.
/// Satis fiyati DiscountPrice varsa odur, yoksa Price'tir (bkz. StorePricing). Fiyat ve stok her zaman sunucuda
/// hesaplanir; istemciden gelen fiyat kullanilmaz.
/// </summary>
public class Product : BaseEntity
{
    public string Name { get; set; } = string.Empty;

    /// <summary>URL dostu benzersiz anahtar.</summary>
    public string Slug { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    /// <summary>Stok kodu; benzersizdir.</summary>
    public string Sku { get; set; } = string.Empty;

    public long CategoryId { get; set; }

    public string Brand { get; set; } = string.Empty;

    /// <summary>Liste (indirimsiz) birim fiyati.</summary>
    public decimal Price { get; set; }

    /// <summary>Indirimli satis fiyati; null ise indirim yoktur. Price'tan kucuk olmalidir.</summary>
    public decimal? DiscountPrice { get; set; }

    /// <summary>
    /// Satilabilir stok. Siparis olusturulurken atomik olarak (UPDATE ... WHERE StockQuantity &gt;= adet) dusurulur,
    /// iptalde iade edilir; negatif olamaz (check constraint).
    /// </summary>
    public int StockQuantity { get; set; }

    public bool IsActive { get; set; } = true;

    /// <summary>"Senin Icin Sectiklerimiz" / one cikanlar.</summary>
    public bool IsFeatured { get; set; }

    /// <summary>Admin'in belirledigi gosterim sirasi ("one cikanlar" siralamasi).</summary>
    public int SortOrder { get; set; }

    /// <summary>Urun ozellikleri; JSON dizi (string[]).</summary>
    public string? FeaturesJson { get; set; }

    /// <summary>Kullanim/teknik bilgiler; JSON dizi ([{label, value}]).</summary>
    public string? SpecsJson { get; set; }

    /// <summary>
    /// Varyant gruplari (beden, renk, paket, aroma); JSON dizi
    /// ([{id, label, kind, options: [{id, label, priceDelta, inStock, swatch}]}]). Stok urun seviyesindedir;
    /// secenekteki inStock=false yalnizca o secenegi satisa kapatir.
    /// </summary>
    public string? VariantsJson { get; set; }

    public List<ProductImage> Images { get; set; } = new();
}
