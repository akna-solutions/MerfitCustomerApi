using MerfitCustomerApi.Business.Services.FoodAnalysis;
using Xunit;

namespace MerfitCustomerApi.Business.Tests.FoodAnalysis;

public class FoodCatalogMatcherTests
{
    private static readonly IReadOnlyList<CatalogFoodCandidate> Catalog = new[]
    {
        Candidate(1, "Pirinç Pilavı", 130),
        Candidate(2, "Tavuk Suyu", 15),
        Candidate(3, "Izgara Tavuk Göğsü", 165),
        Candidate(4, "Bulgur Pilavı", 120),
    };

    private static CatalogFoodCandidate Candidate(long id, string name, decimal kcal) =>
        new(id, name, FoodCatalogMatcher.Normalize(name), kcal, 3, 20, 2);

    private static ParsedFoodItem Item(string name, string generic, decimal kcal) =>
        new(name, generic, 150, 100, 200, kcal, 3, 20, 2, 0.9m, "low", null);

    [Fact]
    public void Normalize_FoldsTurkishCharactersAndPunctuation()
    {
        Assert.Equal("pirinc pilavi 100 g", FoodCatalogMatcher.Normalize("  Pirinç PİLAVI (100 g) "));
        Assert.Equal("izgara tavuk gogsu", FoodCatalogMatcher.Normalize("Izgara Tavuk Göğsü"));
    }

    [Fact]
    public void ExactNameIgnoringCaseAndDiacritics_Matches()
    {
        Assert.Equal(1, FoodCatalogMatcher.FindBestMatch(Item("Pirinc pilavi", "pirinç pilavı", 150), Catalog)?.FoodId);
    }

    [Fact]
    public void AllCatalogWordsContained_Matches()
    {
        Assert.Equal(3, FoodCatalogMatcher.FindBestMatch(Item("Izgara tavuk göğsü dilimleri", "ızgara tavuk göğsü", 170), Catalog)?.FoodId);
    }

    [Fact]
    public void ImplausibleEnergy_IsNotMatched()
    {
        // "Tavuk" kelimesi ortak ama tavuk suyu (15 kcal) yemek tahminiyle (165 kcal) uyusmuyor.
        Assert.Null(FoodCatalogMatcher.FindBestMatch(Item("Tavuk sote", "tavuk suyu", 165), new[] { Catalog[1] }));
    }

    [Fact]
    public void UnrelatedName_IsNotMatched()
    {
        Assert.Null(FoodCatalogMatcher.FindBestMatch(Item("Yoğurtlu mantı", "mantı", 170), Catalog));
    }
}
