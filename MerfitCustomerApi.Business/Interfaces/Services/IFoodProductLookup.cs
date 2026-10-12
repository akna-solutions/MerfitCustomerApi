namespace MerfitCustomerApi.Business.Interfaces.Services;

/// <summary>Barkodla dis bir kaynaktan (Open Food Facts) paketli urun bilgisi getirir.</summary>
public interface IFoodProductLookup
{
    /// <summary>
    /// Urunu dondurur; urun yoksa veya 100 g/ml basina kalori bilgisi yoksa null doner.
    /// Servise ulasilamazsa <see cref="Domain.Exceptions.ExternalServiceUnavailableException"/> firlatir.
    /// </summary>
    Task<ExternalFoodProduct?> FindByBarcodeAsync(string barcode, CancellationToken cancellationToken = default);
}

/// <summary>Dis kaynaktan gelen urunun 100 g (veya 100 ml) basina besin degerleri.</summary>
public sealed record ExternalFoodProduct
{
    public required string Barcode { get; init; }
    public required string Name { get; init; }
    public string? Brand { get; init; }

    /// <summary>"g" veya "ml"; degerler bu birimin 100'u basinadir.</summary>
    public required string Unit { get; init; }

    public required decimal CaloriesPer100 { get; init; }
    public decimal ProteinPer100 { get; init; }
    public decimal CarbsPer100 { get; init; }
    public decimal FatPer100 { get; init; }
    public decimal? FiberPer100 { get; init; }
    public decimal? SugarPer100 { get; init; }

    /// <summary>Miligram cinsinden (Food.Sodium ile ayni birim).</summary>
    public decimal? SodiumMgPer100 { get; init; }

    public string? ImageUrl { get; init; }
}
