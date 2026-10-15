using MerfitCustomerApi.Business.Common;
using MerfitCustomerApi.Business.Services.BodyComposition;
using MerfitCustomerApi.Domain.Entities;
using MerfitCustomerApi.Domain.Entities.Enums;
using MerfitCustomerApi.Domain.Exceptions;
using Xunit;

namespace MerfitCustomerApi.Business.Tests.BodyComposition;

/// <summary>
/// US Navy tahmini, yag orani degerlendirmesi (olculen > tahmini, eksik veride sonuc yok, BMI kullanilmaz)
/// ve Yag Kaybi hedefindeki dusuk yag orani onay kurali.
/// </summary>
public class BodyFatAssessmentTests
{
    private static readonly BodyCompositionOptions Options = new();
    private static readonly DateTime Now = new(2026, 10, 15, 12, 0, 0, DateTimeKind.Utc);

    private static DateTime BornYearsAgo(int years) => Now.Date.AddYears(-years).AddDays(-10);

    private static BodyMeasurement Measurement(
        int daysAgo = 0,
        decimal? weight = null,
        decimal? bodyFat = null,
        decimal? neck = null,
        decimal? waist = null,
        decimal? hip = null,
        decimal? estimated = null) => new()
    {
        Id = daysAgo + 1,
        UserId = 1,
        RecordedAt = Now.AddDays(-daysAgo),
        WeightKg = weight,
        BodyFatPercentage = bodyFat,
        NeckCm = neck,
        WaistCm = waist,
        HipCm = hip,
        EstimatedBodyFatPercentage = estimated,
    };

    // ---- US Navy tahmini ----

    [Theory]
    [InlineData(178, 38, 86, 17.2)]
    [InlineData(180, 40, 74, 4.6)]
    public void UsNavy_Male_MatchesReferenceFormula(double height, double neck, double waist, double expected)
    {
        Assert.Equal((decimal)expected, BodyFatEstimator.EstimateUsNavy(Gender.Male, (decimal)height, (decimal)neck, (decimal)waist, null));
    }

    [Theory]
    [InlineData(165, 33, 72, 98, 26.9)]
    [InlineData(168, 34, 64, 88, 16.0)]
    public void UsNavy_Female_MatchesReferenceFormula(double height, double neck, double waist, double hip, double expected)
    {
        Assert.Equal((decimal)expected, BodyFatEstimator.EstimateUsNavy(Gender.Female, (decimal)height, (decimal)neck, (decimal)waist, (decimal)hip));
    }

    [Fact]
    public void UsNavy_ReturnsNull_WhenDataIsInsufficientOrImplausible()
    {
        Assert.Null(BodyFatEstimator.EstimateUsNavy(Gender.Male, null, 38, 86, null));       // boy yok
        Assert.Null(BodyFatEstimator.EstimateUsNavy(Gender.Male, 178, null, 86, null));      // boyun yok
        Assert.Null(BodyFatEstimator.EstimateUsNavy(Gender.Female, 165, 33, 72, null));      // kadinda kalca yok
        Assert.Null(BodyFatEstimator.EstimateUsNavy(Gender.Other, 178, 38, 86, 95));         // formul tanimsiz
        Assert.Null(BodyFatEstimator.EstimateUsNavy(null, 178, 38, 86, 95));                 // cinsiyet yok
        Assert.Null(BodyFatEstimator.EstimateUsNavy(Gender.Male, 178, 40, 40, null));        // bel <= boyun
    }

    // ---- Degerlendirme ----

    [Fact]
    public void RecentMeasuredValue_TakesPrecedenceOverEstimate()
    {
        var measurements = new[]
        {
            Measurement(daysAgo: 1, neck: 40, waist: 74),   // tahmin ~4.6 (dusuk)
            Measurement(daysAgo: 5, bodyFat: 15),           // olculen 15 (saglikli)
        };

        var result = BodyFatAssessor.Assess(Gender.Male, BornYearsAgo(25), 180, measurements, Now, Options);

        Assert.Equal(BodyFatSource.Measured, result.Reading!.Source);
        Assert.Equal(15m, result.Reading.Percentage);
        Assert.Equal(BodyFatCategory.Healthy, result.Category);
    }

    [Fact]
    public void OldMeasuredValue_IsIgnored_AndRecentEstimateIsUsed_AndLabelledEstimated()
    {
        var measurements = new[]
        {
            Measurement(daysAgo: 200, bodyFat: 15),
            Measurement(daysAgo: 2, neck: 40, waist: 74),
        };

        var result = BodyFatAssessor.Assess(Gender.Male, BornYearsAgo(25), 180, measurements, Now, Options);

        Assert.Equal(BodyFatSource.Estimated, result.Reading!.Source);
        Assert.Equal(BodyFatEstimator.UsNavyMethod, result.Reading.Method);
        Assert.Equal(Options.EstimatedMarginOfErrorPercent, result.Reading.MarginOfErrorPercent);
        Assert.Equal(4.6m, result.Reading.Percentage);
        Assert.True(result.IsLow);
    }

    [Fact]
    public void WeightAndHeightOnly_ProducesNoResult_BmiIsNeverUsedAsBodyFat()
    {
        var measurements = new[] { Measurement(weight: 50) }; // BMI ~15.4 ama yag orani DEGILDIR

        var result = BodyFatAssessor.Assess(Gender.Male, BornYearsAgo(25), 180, measurements, Now, Options);

        Assert.Equal(BodyFatCategory.Unknown, result.Category);
        Assert.Null(result.Reading);
        Assert.Contains("bodyFat", result.Missing);
        Assert.Contains("neck", result.Missing);
        Assert.Contains("waist", result.Missing);
        Assert.False(result.IsLow);
    }

    [Fact]
    public void MissingAgeOrGender_ProducesNoCategory_EvenWithVeryLowValue()
    {
        var measurements = new[] { Measurement(bodyFat: 5) };

        var noAge = BodyFatAssessor.Assess(Gender.Male, null, 180, measurements, Now, Options);
        var noGender = BodyFatAssessor.Assess(null, BornYearsAgo(25), 180, measurements, Now, Options);
        var minor = BodyFatAssessor.Assess(Gender.Male, BornYearsAgo(16), 180, measurements, Now, Options);

        Assert.Equal(BodyFatCategory.Unknown, noAge.Category);
        Assert.Contains("age", noAge.Missing);
        Assert.Equal(BodyFatCategory.Unknown, noGender.Category);
        Assert.Contains("gender", noGender.Missing);
        Assert.Equal(BodyFatCategory.Unknown, minor.Category);
        Assert.Contains("age", minor.Missing);
    }

    [Theory]
    [InlineData(Gender.Male, 25, 7.9, BodyFatCategory.Low)]
    [InlineData(Gender.Male, 25, 8, BodyFatCategory.Healthy)]
    [InlineData(Gender.Male, 25, 20.5, BodyFatCategory.High)]
    [InlineData(Gender.Male, 45, 10, BodyFatCategory.Low)]
    [InlineData(Gender.Male, 65, 12, BodyFatCategory.Low)]
    [InlineData(Gender.Female, 30, 20, BodyFatCategory.Low)]
    [InlineData(Gender.Female, 30, 25, BodyFatCategory.Healthy)]
    [InlineData(Gender.Female, 50, 22, BodyFatCategory.Low)]
    [InlineData(Gender.Female, 70, 37, BodyFatCategory.High)]
    public void Thresholds_DependOnAgeAndSex(Gender gender, int age, double bodyFat, BodyFatCategory expected)
    {
        var result = BodyFatAssessor.Assess(gender, BornYearsAgo(age), 170, new[] { Measurement(bodyFat: (decimal)bodyFat) }, Now, Options);

        Assert.Equal(expected, result.Category);
    }

    [Fact]
    public void ConfiguredRanges_ReplaceDefaults()
    {
        var options = new BodyCompositionOptions
        {
            Ranges = { new BodyFatRange { Gender = Gender.Male, MinAge = 18, MaxAge = 120, LowBelow = 12, HighAbove = 25 } },
        };

        var result = BodyFatAssessor.Assess(Gender.Male, BornYearsAgo(25), 180, new[] { Measurement(bodyFat: 10) }, Now, options);

        Assert.True(result.IsLow);
        Assert.Equal(12m, result.LowThreshold);
    }

    // ---- Yag Kaybi uyarisi ----

    private static BodyFatAssessment Low() =>
        BodyFatAssessor.Assess(Gender.Male, BornYearsAgo(25), 180, new[] { Measurement(bodyFat: 6) }, Now, Options);

    [Fact]
    public void LoseWeight_WithLowBodyFat_RequiresExplicitConfirmation()
    {
        var ex = Assert.Throws<LowBodyFatConfirmationRequiredException>(() =>
            LowBodyFatGoalGuard.EnsureConfirmed(FitnessGoal.LoseWeight, Low(), confirmed: null));
        Assert.Equal(LowBodyFatGoalGuard.WarningMessage, ex.Message);
        Assert.Throws<LowBodyFatConfirmationRequiredException>(() =>
            LowBodyFatGoalGuard.EnsureConfirmed(FitnessGoal.LoseWeight, Low(), confirmed: false));

        Assert.True(LowBodyFatGoalGuard.EnsureConfirmed(FitnessGoal.LoseWeight, Low(), confirmed: true));
    }

    [Fact]
    public void OtherGoals_OrUnknownBodyFat_DoNotRequireConfirmation()
    {
        Assert.False(LowBodyFatGoalGuard.EnsureConfirmed(FitnessGoal.BuildMuscle, Low(), confirmed: null));
        Assert.False(LowBodyFatGoalGuard.EnsureConfirmed(FitnessGoal.GetStronger, Low(), confirmed: null));
        Assert.False(LowBodyFatGoalGuard.EnsureConfirmed(FitnessGoal.LoseWeight, BodyFatAssessment.Unknown, confirmed: null));
    }

    [Fact]
    public void WarningText_IsGentleAndMentionsEstimate()
    {
        Assert.Contains("düşük aralıkta görünüyor", LowBodyFatGoalGuard.WarningMessage);
        Assert.Contains("tahmini bir değer olabilir", LowBodyFatGoalGuard.WarningMessage);
    }
}
