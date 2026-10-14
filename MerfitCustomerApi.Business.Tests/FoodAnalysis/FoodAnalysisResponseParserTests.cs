using MerfitCustomerApi.Business.Services.FoodAnalysis;
using Xunit;

namespace MerfitCustomerApi.Business.Tests.FoodAnalysis;

/// <summary>Modelin yapilandirilmis ciktisi sema, aralik ve enerji tutarliligi acisindan dogrulanir.</summary>
public class FoodAnalysisResponseParserTests
{
    [Fact]
    public void ValidResponse_IsParsed()
    {
        var parsed = FoodAnalysisResponseParser.Parse(FoodAnalysisTestData.ValidModelJson, 8);

        Assert.True(parsed.ContainsFood);
        Assert.Equal("Pilav ve mantı", parsed.MealName);
        Assert.Equal(2, parsed.Items.Count);
        var manti = parsed.Items[1];
        Assert.Equal(("Yoğurtlu mantı", "mantı", 250m, 150m, 350m), (manti.Name, manti.GenericName, manti.EstimatedGrams, manti.MinGrams, manti.MaxGrams));
        Assert.Equal(("high", 0.7m), (manti.PortionUncertainty, manti.Confidence));
        Assert.Equal("Tereyağlı sosun miktarı görünmüyor.", manti.Note);
        Assert.Equal(new[] { "kasede koyu renkli sos" }, parsed.UnidentifiedItems);
        Assert.Empty(parsed.Warnings);
    }

    [Theory]
    [InlineData("bu bir json değil")]
    [InlineData("{\"containsFood\": true, \"items\": [")]
    [InlineData("[]")]
    [InlineData("{\"items\": []}")]
    [InlineData("{\"containsFood\": \"evet\", \"items\": []}")]
    [InlineData("{\"containsFood\": true, \"items\": {}}")]
    public void InvalidJson_Throws(string json)
    {
        Assert.Throws<FoodAnalysisInvalidResponseException>(() => FoodAnalysisResponseParser.Parse(json, 8));
    }

    [Fact]
    public void InconsistentItem_IsDroppedWithWarning()
    {
        // 500 kcal/100 g ama makrolar ~80 kcal: enerji tutarsiz.
        var json = """
            {"containsFood": true, "mealName": "x", "overallConfidence": 0.8, "unidentifiedItems": [],
             "items": [
               {"name": "Elma", "genericName": "elma", "estimatedGrams": 150, "minGrams": 120, "maxGrams": 180,
                "caloriesPer100g": 52, "proteinPer100g": 0.3, "carbsPer100g": 14, "fatPer100g": 0.2, "confidence": 0.95, "portionUncertainty": "low"},
               {"name": "Kek", "genericName": "kek", "estimatedGrams": 80, "minGrams": 60, "maxGrams": 100,
                "caloriesPer100g": 500, "proteinPer100g": 5, "carbsPer100g": 15, "fatPer100g": 1, "confidence": 0.8, "portionUncertainty": "medium"}
             ]}
            """;

        var parsed = FoodAnalysisResponseParser.Parse(json, 8);

        Assert.Equal("Elma", Assert.Single(parsed.Items).Name);
        Assert.Contains(parsed.Warnings, w => w.Contains("tutarsız"));
    }

    [Theory]
    [InlineData("\"estimatedGrams\": 0")]
    [InlineData("\"estimatedGrams\": 9000")]
    [InlineData("\"estimatedGrams\": -5")]
    [InlineData("\"estimatedGrams\": null")]
    public void OutOfRangeGrams_MakeResponseInvalid_WhenNoItemSurvives(string grams)
    {
        var json = $$"""
            {"containsFood": true, "mealName": "x", "overallConfidence": 0.8, "unidentifiedItems": [],
             "items": [{"name": "Elma", "genericName": "elma", {{grams}}, "minGrams": 1, "maxGrams": 2,
                        "caloriesPer100g": 52, "proteinPer100g": 0.3, "carbsPer100g": 14, "fatPer100g": 0.2, "confidence": 0.9, "portionUncertainty": "low"}]}
            """;

        Assert.Throws<FoodAnalysisInvalidResponseException>(() => FoodAnalysisResponseParser.Parse(json, 8));
    }

    [Fact]
    public void ConfidenceAsPercentAndBadRange_AreNormalised()
    {
        var json = """
            {"containsFood": true, "mealName": "", "overallConfidence": 85, "unidentifiedItems": [],
             "items": [{"name": "Elma", "genericName": "", "estimatedGrams": "150", "minGrams": 300, "maxGrams": 10,
                        "caloriesPer100g": 52, "proteinPer100g": 0.3, "carbsPer100g": 14, "fatPer100g": 0.2, "confidence": 90, "portionUncertainty": "çok"}]}
            """;

        var item = Assert.Single(FoodAnalysisResponseParser.Parse(json, 8).Items);

        Assert.Equal((150m, 150m, 150m), (item.EstimatedGrams, item.MinGrams, item.MaxGrams));
        Assert.Equal((0.9m, "medium", "Elma"), (item.Confidence, item.PortionUncertainty, item.GenericName));
        Assert.Equal(0.85m, FoodAnalysisResponseParser.Parse(json, 8).OverallConfidence);
        Assert.Equal("Elma", FoodAnalysisResponseParser.Parse(json, 8).MealName);
    }

    [Fact]
    public void NoFood_ReturnsEmptyResult()
    {
        var parsed = FoodAnalysisResponseParser.Parse("""{"containsFood": false, "mealName": "", "items": [], "unidentifiedItems": [], "overallConfidence": 0}""", 8);

        Assert.False(parsed.ContainsFood);
        Assert.Empty(parsed.Items);
    }

    [Fact]
    public void TooManyItems_AreTruncatedWithWarning()
    {
        var item = """{"name": "Zeytin", "genericName": "zeytin", "estimatedGrams": 20, "minGrams": 10, "maxGrams": 30, "caloriesPer100g": 115, "proteinPer100g": 0.8, "carbsPer100g": 6, "fatPer100g": 11, "confidence": 0.9, "portionUncertainty": "low"}""";
        var json = $$"""{"containsFood": true, "mealName": "Kahvaltı", "overallConfidence": 0.9, "unidentifiedItems": [], "items": [{{string.Join(",", Enumerable.Repeat(item, 5))}}]}""";

        var parsed = FoodAnalysisResponseParser.Parse(json, 3);

        Assert.Equal(3, parsed.Items.Count);
        Assert.Contains(parsed.Warnings, w => w.Contains("ilk 3"));
    }

    [Fact]
    public void CodeFenceAndMarkupInNames_AreCleaned()
    {
        var json = "```json\n" + """
            {"containsFood": true, "mealName": "<b>Çorba</b>", "overallConfidence": 0.9, "unidentifiedItems": [],
             "items": [{"name": "Mercimek\n çorbası <script>", "genericName": "mercimek çorbası", "estimatedGrams": 300, "minGrams": 250, "maxGrams": 350,
                        "caloriesPer100g": 56, "proteinPer100g": 3.5, "carbsPer100g": 8, "fatPer100g": 1.2, "confidence": 0.9, "portionUncertainty": "low"}]}
            """ + "\n```";

        var parsed = FoodAnalysisResponseParser.Parse(json, 8);

        Assert.Equal("Mercimek çorbası script", parsed.Items[0].Name);
        Assert.Equal("bÇorba/b", parsed.MealName);
    }
}
