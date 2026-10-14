using System.Globalization;
using System.Text;

namespace MerfitCustomerApi.Business.Services.FoodAnalysis;

/// <summary>
/// Modelin tanidigi yemek adini besin katalogundaki (Food) dogrulanmis bir kayitla eslestirir. Turkce karakterler
/// ve buyuk/kucuk harf farki yok sayilir; tam ad eslesmesi ya da kelime ortusmesi aranir. Katalogdaki enerji
/// degeri modelin tahmininden cok farkliysa eslesme yanlis kabul edilir (orn. "tavuk" -> "tavuk suyu").
/// </summary>
public static class FoodCatalogMatcher
{
    /// <summary>Kelime ortusmesi icin en dusuk skor (0-1).</summary>
    public const double MinScore = 0.6;

    /// <summary>Katalog kcal/100 g, model tahmininin bu oranlari disindaysa eslesme reddedilir.</summary>
    private const decimal MinEnergyRatio = 0.4m;
    private const decimal MaxEnergyRatio = 2.5m;

    private static readonly HashSet<string> StopWords = new(StringComparer.Ordinal)
    {
        "ve", "ile", "bir", "porsiyon", "tabak", "kase", "dilim", "adet", "ev", "yapimi", "usulu", "sade",
    };

    public static CatalogFoodCandidate? FindBestMatch(ParsedFoodItem item, IReadOnlyList<CatalogFoodCandidate> catalog)
    {
        if (catalog.Count == 0) return null;

        var names = new[] { item.GenericName, item.Name }
            .Select(Normalize)
            .Where(n => n.Length > 0)
            .Distinct()
            .ToList();
        if (names.Count == 0) return null;

        CatalogFoodCandidate? best = null;
        var bestScore = 0d;
        foreach (var food in catalog)
        {
            if (!IsEnergyPlausible(item.CaloriesPer100g, food.CaloriesPer100g)) continue;

            var score = names.Max(name => Score(name, food.NormalizedName));
            // Esit skorda daha kisa (daha genel) katalog adi tercih edilir: "pilav" ~ "pirinç pilavı" yerine.
            if (score > bestScore || (score == bestScore && best is not null && food.NormalizedName.Length < best.NormalizedName.Length))
            {
                best = food;
                bestScore = score;
            }
        }

        return bestScore >= MinScore ? best : null;
    }

    private static double Score(string name, string catalogName)
    {
        if (name == catalogName) return 1;

        var a = Tokens(name);
        var b = Tokens(catalogName);
        if (a.Count == 0 || b.Count == 0) return 0;

        var common = a.Intersect(b).Count();
        if (common == 0) return 0;
        // Jaccard benzerligi; katalog adinin tum kelimeleri modelin adinda geciyorsa kuvvetli eslesme sayilir.
        var jaccard = (double)common / a.Union(b).Count();
        return common == b.Count ? Math.Max(jaccard, 0.75) : jaccard;
    }

    private static bool IsEnergyPlausible(decimal estimatedKcal, decimal catalogKcal)
    {
        // Cok dusuk enerjili (su, cay, salata yesillikleri) besinlerde oran anlamsizlasir; mutlak farka bakilir.
        if (estimatedKcal < 30 || catalogKcal < 30) return Math.Abs(estimatedKcal - catalogKcal) <= 40;
        var ratio = catalogKcal / estimatedKcal;
        return ratio >= MinEnergyRatio && ratio <= MaxEnergyRatio;
    }

    private static HashSet<string> Tokens(string normalized) => normalized
        .Split(' ', StringSplitOptions.RemoveEmptyEntries)
        .Where(t => t.Length > 1 && !StopWords.Contains(t))
        .ToHashSet(StringComparer.Ordinal);

    /// <summary>Turkce harfleri ASCII karsiliklarina indirger, noktalamayi atar: "Pirinç Pilavı (100 g)" -> "pirinc pilavi 100 g".</summary>
    public static string Normalize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        var lower = text.Trim().ToLower(CultureInfo.GetCultureInfo("tr-TR"));
        var builder = new StringBuilder(lower.Length);
        foreach (var ch in lower)
        {
            var mapped = ch switch
            {
                'ç' => 'c',
                'ğ' => 'g',
                'ı' => 'i',
                'ö' => 'o',
                'ş' => 's',
                'ü' => 'u',
                'â' => 'a',
                'î' => 'i',
                'û' => 'u',
                _ => ch,
            };
            builder.Append(char.IsLetterOrDigit(mapped) ? mapped : ' ');
        }
        return string.Join(' ', builder.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }
}

/// <summary>Eslestirmeye aday katalog besini; degerler 100 g icin normalize edilmistir.</summary>
public sealed record CatalogFoodCandidate(
    long FoodId,
    string Name,
    string NormalizedName,
    decimal CaloriesPer100g,
    decimal ProteinPer100g,
    decimal CarbsPer100g,
    decimal FatPer100g);
