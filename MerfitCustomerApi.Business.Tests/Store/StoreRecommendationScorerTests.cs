using MerfitCustomerApi.Business.Services.Store;
using Xunit;
using static MerfitCustomerApi.Business.Services.Store.StoreRecommendationScorer;

namespace MerfitCustomerApi.Business.Tests.Store;

public class StoreRecommendationScorerTests
{
    // 1: Protein, 2: Protein Bar (1'in alti), 3: Giyim
    private static readonly CategoryTree Tree = new(new Dictionary<long, long?> { [1] = null, [2] = 1, [3] = null, [4] = 1 });

    private static Candidate C(long id, long category = 1, string brand = "A", decimal price = 100, bool inStock = true, string name = "Whey Protein",
        int co = 0, int coUser = 0, int sales = 0, double ratingSum = 0, int ratingCount = 0, bool inCart = false, bool purchased = false, decimal? list = null) =>
        new(id, category, brand, price, list ?? price, inStock, Tokens(name, brand), co, coUser, sales, ratingSum, ratingCount, inCart, purchased);

    private static readonly Context Ctx = new(100, 1, "A", 100, Tokens("Whey Protein Çikolata", "A"));

    [Fact]
    public void Tokens_IgnoreBrandShortWordsAndCase()
    {
        Assert.Equal(new[] { "whey", "protein", "çikolata" }.ToHashSet(), Tokens("PureLab WHEY Protein 1 kg Çikolata", "PureLab"));
        Assert.Equal(1 / 3.0, Jaccard(Tokens("whey protein"), Tokens("whey isolate")), 6);
    }

    [Fact]
    public void CategoryTree_Relation()
    {
        Assert.Equal(1, Tree.Relation(1, 1));
        Assert.Equal(0.5, Tree.Relation(1, 2));
        Assert.Equal(0.5, Tree.Relation(2, 4));
        Assert.Equal(0, Tree.Relation(2, 3));
    }

    [Fact]
    public void PriceSimilarity_IsSymmetricAndZeroAtDouble()
    {
        Assert.Equal(1, PriceSimilarity(100, 100), 6);
        Assert.Equal(PriceSimilarity(100, 150), PriceSimilarity(150, 100), 6);
        Assert.Equal(0, PriceSimilarity(100, 200), 6);
        Assert.Equal(0, PriceSimilarity(100, 0));
    }

    [Fact]
    public void Quality_FewReviewsStayNearNeutral()
    {
        Assert.True(Quality(5, 1) < Quality(50, 10));
        Assert.True(Quality(1, 1) > Quality(10, 10));
        Assert.Equal(0.25, Quality(0, 0), 6);
    }

    [Fact]
    public void Similar_SameCategoryAndCloseFeatureBeatsOtherCategory_UnrelatedIsZero()
    {
        var sameCategory = SimilarScore(Ctx, C(1, name: "Whey Protein Vanilya"), Tree);
        var childCategory = SimilarScore(Ctx, C(2, category: 2, brand: "B", name: "Fıstıklı Bar"), Tree);
        var unrelated = SimilarScore(Ctx, C(3, category: 3, brand: "B", name: "Tişört"), Tree);
        Assert.True(sameCategory > childCategory);
        Assert.True(childCategory > 0);
        Assert.Equal(0, unrelated);
    }

    [Fact]
    public void Similar_CoPurchaseMakesOtherCategoryRelevant_OutOfStockHalved()
    {
        Assert.True(SimilarScore(Ctx, C(3, category: 3, brand: "B", name: "Shaker", co: 5), Tree) > 0);
        var inStock = SimilarScore(Ctx, C(1), Tree);
        Assert.Equal(inStock / 2, SimilarScore(Ctx, C(1, inStock: false), Tree), 6);
    }

    [Fact]
    public void Recommended_ExcludesCartAndOutOfStock_PenalizesPurchased()
    {
        var affinity = new Affinity();
        Assert.Equal(0, RecommendedScore(Ctx, C(1, inCart: true, sales: 10), Tree, affinity, 10));
        Assert.Equal(0, RecommendedScore(Ctx, C(1, inStock: false, sales: 10), Tree, affinity, 10));
        var fresh = RecommendedScore(Ctx, C(1, sales: 10), Tree, affinity, 10);
        Assert.Equal(fresh * 0.6, RecommendedScore(Ctx, C(1, sales: 10, purchased: true), Tree, affinity, 10), 6);
    }

    [Fact]
    public void Recommended_UsesUserAffinityAndBoughtTogether()
    {
        var affinity = new Affinity();
        affinity.Add(3, "B", 3, Tree);
        var liked = RecommendedScore(Ctx, C(1, category: 3, brand: "B", name: "Tayt"), Tree, affinity, 0);
        var other = RecommendedScore(Ctx, C(2, category: 3, brand: "C", name: "Şort"), Tree, new Affinity(), 0);
        Assert.True(liked > other);
        var together = RecommendedScore(Ctx, C(3, category: 3, brand: "C", co: 4), Tree, new Affinity(), 0);
        Assert.True(together > other);
    }

    [Fact]
    public void Affinity_ChildCategorySignalReachesParent()
    {
        var affinity = new Affinity();
        affinity.Add(2, "A", 2, Tree);
        Assert.Equal(1, affinity.CategoryScore(2, Tree), 6);
        Assert.True(affinity.CategoryScore(1, Tree) > 0);
        Assert.Equal(0, affinity.CategoryScore(3, Tree));
    }

    [Fact]
    public void Pick_CapsBrandThenFills_DropsZero_TiesAreDeterministic()
    {
        var scored = Enumerable.Range(1, 5).Select(i => (C(i, brand: "A"), 10.0 - i))
            .Append((C(10, brand: "B"), 1.0))
            .Append((C(11, brand: "C"), 0.0))
            .ToList();
        var picked = Pick(scored, 5, capCategory: false).Select(c => c.Id).ToList();
        // A'dan en fazla 3, sonra B; liste dolmadigi icin kalan A'lar eklenir. Sifir puanli C secilmez.
        Assert.Equal(new long[] { 1, 2, 3, 10, 4 }, picked);

        var ties = Pick(new[] { (C(1, sales: 1, brand: "X"), 1.0), (C(2, sales: 5, brand: "Y"), 1.0), (C(3, sales: 5, brand: "Z"), 1.0) }, 3, false);
        Assert.Equal(new long[] { 3, 2, 1 }, ties.Select(c => c.Id));
    }

    [Fact]
    public void Pick_CapsCategoryForRecommendations()
    {
        var scored = Enumerable.Range(1, 6).Select(i => (C(i, category: 1, brand: $"B{i}"), 10.0 - i)).Append((C(20, category: 3, brand: "Z"), 0.5));
        var picked = Pick(scored, 5, capCategory: true).Select(c => c.Id).ToList();
        Assert.Equal(new long[] { 1, 2, 3, 4, 20 }, picked);
    }
}
