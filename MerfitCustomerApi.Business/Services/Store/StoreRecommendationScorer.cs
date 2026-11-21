using System.Globalization;
using System.Text.RegularExpressions;

namespace MerfitCustomerApi.Business.Services.Store;

/// <summary>
/// Oneri puanlamasi (saf; veritabanina dokunmaz, birim testlenir). StoreRecommendationService veriyi toplar, bu sinif siralar.
///
/// Benzer urunler (urun -> urun):
///   4.0 x kategori (ayni kategori 1, ayni ust kategori / ust-alt kategori 0.5)
/// + 1.5 x ayni marka
/// + 1.5 x fiyat yakinligi (ayni fiyat 1, 2 kat fark 0; log olcekli)
/// + 2.0 x ad benzerligi (anlamli kelimelerin Jaccard orani)
/// + 2.5 x birlikte satin alinma (ayni sipariste bulunma; 5 siparişte doyar)
/// + 0.3 x kalite (yayindaki yorumlarin Bayes ortalamasi)
///   Iliskisiz urunler (kategori, marka, ad ya da birlikte alinma ortak noktasi olmayan) elenir; stokta olmayanin puani yarilanir.
///
/// Onerilenler (kisi + urun):
///   3.0 x bu urunle birlikte alinma
/// + 2.0 x kullanicinin sepet/favori urunleriyle birlikte alinma
/// + 2.0 x kategori ilgisi (favori, sepet, satin alma ve bakilan urunden; 0-1'e normalize)
/// + 1.0 x marka ilgisi
/// + 1.5 x son 90 gun satis populerligi (log normalize)
/// + 1.0 x bakilan urunle kategori yakinligi
/// + 0.5 x kalite + 0.3 x indirim orani
///   Stokta olmayan ve sepetteki urunler onerilmez; daha once satin alinanin puani azaltilir.
///
/// Cesitlilik: ayni markadan en fazla 3, onerilerde ayni kategoriden en fazla 4 urun once secilir; liste dolmazsa sinir kaldirilir.
/// Esitlikte satis adedi, sonra yeni urun (Id) once gelir; sonuc deterministiktir.
/// </summary>
public static class StoreRecommendationScorer
{
    public const int MaxPerBrand = 3;
    public const int MaxPerCategory = 4;

    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");
    private static readonly Regex Words = new(@"\p{L}[\p{L}\p{N}]*", RegexOptions.Compiled);
    private static readonly HashSet<string> StopWords = new(StringComparer.Ordinal)
    {
        "ve", "ile", "için", "icin", "the", "and", "for", "gr", "kg", "ml", "adet", "paket", "set",
    };

    public sealed record Candidate(
        long Id,
        long CategoryId,
        string Brand,
        decimal SalePrice,
        decimal ListPrice,
        bool InStock,
        IReadOnlySet<string> NameTokens,
        int CoPurchaseWithContext,
        int CoPurchaseWithUser,
        int Sales90d,
        double RatingSum,
        int RatingCount,
        bool InCart = false,
        bool PurchasedBefore = false);

    public sealed record Context(long ProductId, long CategoryId, string Brand, decimal SalePrice, IReadOnlySet<string> NameTokens);

    /// <summary>Kategori agaci (kategori -> ust kategori).</summary>
    public sealed class CategoryTree
    {
        private readonly IReadOnlyDictionary<long, long?> _parents;

        public CategoryTree(IReadOnlyDictionary<long, long?> parents) => _parents = parents;

        public long? Parent(long categoryId) => _parents.TryGetValue(categoryId, out var p) ? p : null;

        /// <summary>1: ayni kategori; 0.5: kardes, ust ya da alt kategori; 0: iliskisiz.</summary>
        public double Relation(long a, long b)
        {
            if (a == b) return 1;
            var pa = Parent(a);
            var pb = Parent(b);
            if (pa == b || pb == a) return 0.5;
            if (pa is not null && pa == pb) return 0.5;
            return 0;
        }
    }

    /// <summary>Kullanicinin kategori ve marka ilgisi (ham agirliklar).</summary>
    public sealed class Affinity
    {
        public Dictionary<long, double> Categories { get; } = new();
        public Dictionary<string, double> Brands { get; } = new(StringComparer.OrdinalIgnoreCase);

        public void Add(long categoryId, string brand, double weight, CategoryTree tree)
        {
            Categories[categoryId] = Categories.GetValueOrDefault(categoryId) + weight;
            // Ust kategoriye yarim agirlik: "Protein Bar" ilgisi "Protein" kategorisine de yansir.
            if (tree.Parent(categoryId) is { } parent) Categories[parent] = Categories.GetValueOrDefault(parent) + weight / 2;
            if (!string.IsNullOrWhiteSpace(brand)) Brands[brand.Trim()] = Brands.GetValueOrDefault(brand.Trim()) + weight;
        }

        public double CategoryScore(long categoryId, CategoryTree tree)
        {
            if (Categories.Count == 0) return 0;
            var max = Categories.Values.Max();
            var own = Categories.GetValueOrDefault(categoryId);
            var parent = tree.Parent(categoryId) is { } p ? Categories.GetValueOrDefault(p) : 0;
            return Math.Min(1, (own + parent / 2) / max);
        }

        public double BrandScore(string brand)
        {
            if (Brands.Count == 0 || string.IsNullOrWhiteSpace(brand)) return 0;
            return Brands.GetValueOrDefault(brand.Trim()) / Brands.Values.Max();
        }
    }

    public static IReadOnlySet<string> Tokens(string? name, string? brand = null)
    {
        if (string.IsNullOrWhiteSpace(name)) return new HashSet<string>();
        var brandTokens = brand is null ? new HashSet<string>() : Words.Matches(brand.ToLower(Turkish)).Select(m => m.Value).ToHashSet();
        return Words.Matches(name.ToLower(Turkish))
            .Select(m => m.Value)
            .Where(t => t.Length >= 3 && !StopWords.Contains(t) && !brandTokens.Contains(t))
            .ToHashSet(StringComparer.Ordinal);
    }

    public static double Jaccard(IReadOnlySet<string> a, IReadOnlySet<string> b)
    {
        if (a.Count == 0 || b.Count == 0) return 0;
        var intersection = a.Count(b.Contains);
        return intersection == 0 ? 0 : intersection / (double)(a.Count + b.Count - intersection);
    }

    /// <summary>Birlikte alinma sayisini 0-1'e indirger (log; 5 siparis = 1).</summary>
    public static double CoPurchase(int orders) => orders <= 0 ? 0 : Math.Min(1, Math.Log(1 + orders) / Math.Log(6));

    /// <summary>Iki fiyatin yakinligi: ayni 1, iki kat fark ve otesi 0.</summary>
    public static double PriceSimilarity(decimal a, decimal b)
    {
        if (a <= 0 || b <= 0) return 0;
        var ratio = Math.Abs(Math.Log((double)a / (double)b));
        return Math.Max(0, 1 - ratio / Math.Log(2));
    }

    /// <summary>Bayes ortalamasi (5 sanal 3.5 puanli oy) -> [-1, 1]. Az yorumlu urun asiri one cikmaz.</summary>
    public static double Quality(double ratingSum, int ratingCount)
    {
        const double priorCount = 5, priorMean = 3.5;
        var mean = (ratingSum + priorCount * priorMean) / (ratingCount + priorCount);
        return Math.Clamp((mean - 3) / 2, -1, 1);
    }

    public static double Discount(decimal listPrice, decimal salePrice) =>
        listPrice > 0 && salePrice < listPrice ? (double)((listPrice - salePrice) / listPrice) : 0;

    public static double SimilarScore(Context context, Candidate c, CategoryTree tree)
    {
        var category = tree.Relation(context.CategoryId, c.CategoryId);
        var sameBrand = !string.IsNullOrWhiteSpace(context.Brand) && string.Equals(context.Brand.Trim(), c.Brand.Trim(), StringComparison.OrdinalIgnoreCase);
        var name = Jaccard(context.NameTokens, c.NameTokens);
        var co = CoPurchase(c.CoPurchaseWithContext);
        if (category == 0 && !sameBrand && name < 0.2 && co == 0) return 0;

        var score = 4.0 * category
                    + (sameBrand ? 1.5 : 0)
                    + 1.5 * PriceSimilarity(context.SalePrice, c.SalePrice)
                    + 2.0 * name
                    + 2.5 * co
                    + 0.3 * Quality(c.RatingSum, c.RatingCount);
        return c.InStock ? score : score / 2;
    }

    public static double RecommendedScore(Context context, Candidate c, CategoryTree tree, Affinity affinity, int maxSales90d)
    {
        if (!c.InStock || c.InCart) return 0;
        var popularity = maxSales90d <= 0 ? 0 : Math.Log(1 + c.Sales90d) / Math.Log(1 + maxSales90d);
        var score = 3.0 * CoPurchase(c.CoPurchaseWithContext)
                    + 2.0 * CoPurchase(c.CoPurchaseWithUser)
                    + 2.0 * affinity.CategoryScore(c.CategoryId, tree)
                    + 1.0 * affinity.BrandScore(c.Brand)
                    + 1.5 * popularity
                    + 1.0 * tree.Relation(context.CategoryId, c.CategoryId)
                    + 0.5 * Quality(c.RatingSum, c.RatingCount)
                    + 0.3 * Discount(c.ListPrice, c.SalePrice);
        if (c.PurchasedBefore) score *= 0.6;
        return Math.Max(0, score);
    }

    /// <summary>Puana gore secer; once marka/kategori sinirlariyla, liste dolmazsa sinirsiz. Sifir puanlilar secilmez.</summary>
    public static List<Candidate> Pick(IEnumerable<(Candidate Candidate, double Score)> scored, int limit, bool capCategory)
    {
        var ordered = scored
            .Where(s => s.Score > 0)
            .OrderByDescending(s => s.Score)
            .ThenByDescending(s => s.Candidate.Sales90d)
            .ThenByDescending(s => s.Candidate.Id)
            .Select(s => s.Candidate)
            .ToList();

        var picked = new List<Candidate>();
        var brands = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var categories = new Dictionary<long, int>();
        foreach (var c in ordered)
        {
            if (picked.Count >= limit) break;
            var brandKey = c.Brand.Trim();
            if (brands.GetValueOrDefault(brandKey) >= MaxPerBrand) continue;
            if (capCategory && categories.GetValueOrDefault(c.CategoryId) >= MaxPerCategory) continue;
            picked.Add(c);
            brands[brandKey] = brands.GetValueOrDefault(brandKey) + 1;
            categories[c.CategoryId] = categories.GetValueOrDefault(c.CategoryId) + 1;
        }
        foreach (var c in ordered)
        {
            if (picked.Count >= limit) break;
            if (!picked.Contains(c)) picked.Add(c);
        }
        return picked;
    }
}
