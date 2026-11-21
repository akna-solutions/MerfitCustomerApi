using MerfitCustomerApi.Business.Common;
using MerfitCustomerApi.Business.Dtos.Customer.Store;

namespace MerfitCustomerApi.Business.Services.Store;

/// <summary>Rozet kodlari. Istemci bilinmeyen kodu gostermez; yeni kod eklemek geriye uyumludur.</summary>
public static class StoreBadgeCodes
{
    public const string Advantageous = "advantageous";
    public const string MbfitPick = "mbfit_pick";
    public const string LowStock = "low_stock";
    public const string Bestseller = "bestseller";
    public const string TopRated = "top_rated";
    public const string New = "new";
    public const string FreeShipping = "free_shipping";

    /// <summary>Admin'in elle atayabildigi rozetler (store.Product.HighlightBadge). Digerleri yalnizca veriden turetilir.</summary>
    public static readonly IReadOnlyList<string> Assignable = new[] { Advantageous, MbfitPick };
}

/// <summary>Rozet hesaplamasi icin bir urunun gercek verileri.</summary>
public sealed record StoreBadgeFacts(
    string? HighlightBadge,
    DateTime CreatedAt,
    int StockQuantity,
    decimal SalePrice,
    bool IsBestseller,
    int ReviewCount,
    decimal AverageRating);

/// <summary>
/// Rozet kurallarinin tek kaynagi. Siralama onem sirasidir: admin rozeti, son urunler, cok satan, yuksek puan, yeni,
/// kargo bedava. Her rozet yalnizca kosulu gercekten saglaniyorsa uretilir.
/// </summary>
public static class StoreBadgeRules
{
    public static List<StoreProductBadgeDto> Build(StoreBadgeFacts facts, StoreOptions options, DateTime nowUtc)
    {
        var rules = options.Badges;
        var badges = new List<StoreProductBadgeDto>();

        switch (facts.HighlightBadge)
        {
            case StoreBadgeCodes.Advantageous:
                badges.Add(Badge(StoreBadgeCodes.Advantageous, "Avantajlı Ürün"));
                break;
            case StoreBadgeCodes.MbfitPick:
                badges.Add(Badge(StoreBadgeCodes.MbfitPick, "MBFit Seçimi"));
                break;
        }

        if (facts.StockQuantity > 0 && facts.StockQuantity <= rules.LowStockThreshold)
        {
            badges.Add(Badge(StoreBadgeCodes.LowStock, $"Son {facts.StockQuantity} Ürün", facts.StockQuantity));
        }

        if (facts.IsBestseller) badges.Add(Badge(StoreBadgeCodes.Bestseller, "Çok Satan"));

        if (facts.ReviewCount >= rules.TopRatedMinReviews && facts.ReviewCount > 0 && facts.AverageRating >= rules.TopRatedMinAverage)
        {
            badges.Add(Badge(StoreBadgeCodes.TopRated, "Yüksek Puanlı"));
        }

        var created = DateTime.SpecifyKind(facts.CreatedAt, DateTimeKind.Utc);
        if (rules.NewProductDays > 0 && created <= nowUtc && created > nowUtc.AddDays(-rules.NewProductDays))
        {
            badges.Add(Badge(StoreBadgeCodes.New, "Yeni"));
        }

        // Tek basina esigi gecen urun sepete eklendiginde kargo ucretsizdir (StorePricing.ShippingFor ile ayni kural).
        if (options.FreeShippingThreshold > 0 && facts.SalePrice >= options.FreeShippingThreshold)
        {
            badges.Add(Badge(StoreBadgeCodes.FreeShipping, "Kargo Bedava"));
        }

        return badges;
    }

    private static StoreProductBadgeDto Badge(string code, string label, int? value = null) => new() { Code = code, Label = label, Value = value };
}
