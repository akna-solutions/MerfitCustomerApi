namespace MerfitCustomerApi.Business.Common;

/// <summary>
/// appsettings.json icindeki "OpenFoodFacts" bolumu. Barkodla urun sorgusu icin kullanilan ucretsiz,
/// acik veri kaynagi Open Food Facts'in (https://world.openfoodfacts.org) erisim ayarlari.
/// </summary>
public class OpenFoodFactsOptions
{
    public const string SectionName = "OpenFoodFacts";

    public string BaseUrl { get; set; } = "https://world.openfoodfacts.org";

    /// <summary>Open Food Facts, istemcinin kendini User-Agent ile tanitmasini ister.</summary>
    public string UserAgent { get; set; } = "MBFit/1.0 (https://mbfit.app)";

    public int TimeoutSeconds { get; set; } = 8;
}
