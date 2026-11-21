using MerfitCustomerApi.Business.Services.Energy;
using MerfitCustomerApi.Domain.Entities.Enums;
using Xunit;

namespace MerfitCustomerApi.Business.Tests.Energy;

/// <summary>Antrenman kalorisinin tek formulu: tur siniflandirma, MET, net kalori ve sure kirpma.</summary>
public class WorkoutCalorieEstimatorTests
{
    [Theory]
    [InlineData("Koşu", null, null, WorkoutEnergyKind.Running)]
    [InlineData(null, "Bisiklet", null, WorkoutEnergyKind.Cycling)]
    [InlineData(null, null, "tempolu-yuruyus", WorkoutEnergyKind.Walking)]
    [InlineData("Sabah Yogası", null, null, WorkoutEnergyKind.Mobility)]
    [InlineData("HIIT Kardiyo", null, null, WorkoutEnergyKind.Cardio)]
    [InlineData("Göğüs Günü", "Kuvvet", "kuvvet", WorkoutEnergyKind.Strength)]
    public void Classify_UsesCategoryAndTitleKeywords(string? title, string? category, string? slug, WorkoutEnergyKind expected)
    {
        Assert.Equal(expected, WorkoutCalorieEstimator.Classify(title, category, slug, null));
    }

    [Fact]
    public void Classify_FallsBackToAttributeScores()
    {
        var cardio = new Dictionary<WorkoutAttributeType, int> { [WorkoutAttributeType.Cardio] = 80, [WorkoutAttributeType.Strength] = 30 };
        var strength = new Dictionary<WorkoutAttributeType, int> { [WorkoutAttributeType.Cardio] = 40, [WorkoutAttributeType.Strength] = 85 };

        Assert.Equal(WorkoutEnergyKind.Cardio, WorkoutCalorieEstimator.Classify("Tam Vücut", null, null, cardio));
        Assert.Equal(WorkoutEnergyKind.Strength, WorkoutCalorieEstimator.Classify("Tam Vücut", null, null, strength));
    }

    [Fact]
    public void EstimateNetKcal_SubtractsRestingMet()
    {
        // Kosu, ileri: MET 11 -> (11 - 1) x 70 kg x 0,5 saat = 350
        Assert.Equal(350, WorkoutCalorieEstimator.EstimateNetKcal(WorkoutEnergyKind.Running, DifficultyLevel.Advanced, 70, 1800));
        // Agirlik, baslangic: MET 3,5 -> 2,5 x 60 x 1 = 150
        Assert.Equal(150, WorkoutCalorieEstimator.EstimateNetKcal(WorkoutEnergyKind.Strength, DifficultyLevel.Beginner, 60, 3600));
    }

    [Fact]
    public void EstimateNetKcal_WithoutWeightOrDuration_ReturnsNull()
    {
        Assert.Null(WorkoutCalorieEstimator.EstimateNetKcal(WorkoutEnergyKind.Strength, DifficultyLevel.Beginner, null, 3600));
        Assert.Null(WorkoutCalorieEstimator.EstimateNetKcal(WorkoutEnergyKind.Strength, DifficultyLevel.Beginner, 70, null));
    }

    [Fact]
    public void ActiveSeconds_PrefersReportedDuration_ClampsToMax()
    {
        var start = new DateTime(2026, 11, 20, 8, 0, 0, DateTimeKind.Utc);

        Assert.Equal(1800, WorkoutCalorieEstimator.ActiveSeconds(1800, start, start.AddHours(2), 240));
        Assert.Equal(3600, WorkoutCalorieEstimator.ActiveSeconds(null, start, start.AddHours(1), 240));
        Assert.Equal(240 * 60, WorkoutCalorieEstimator.ActiveSeconds(99_999, start, start.AddDays(1), 240));
        Assert.Null(WorkoutCalorieEstimator.ActiveSeconds(null, start, null, 240));
    }
}
