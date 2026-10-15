using MerfitCustomerApi.Business.Common;
using MerfitCustomerApi.Business.Dtos.Customer.WorkoutSessions;
using MerfitCustomerApi.Business.Services.WorkoutSessions;
using Xunit;

namespace MerfitCustomerApi.Business.Tests.WorkoutSessions;

/// <summary>Cift progresyon: agirlik yalnizca tum setlerde aralik ust sinirina ulasildiginda artar.</summary>
public class ProgressiveOverloadAdvisorTests
{
    private static readonly ProgressiveOverloadOptions Options = new();

    private static List<CustomerWorkoutSetLogDto> Sets(decimal? weight, params int[] reps) =>
        reps.Select((r, i) => new CustomerWorkoutSetLogDto { SetNumber = i + 1, WeightKg = weight, Reps = r }).ToList();

    [Fact]
    public void AllSetsAtTopOfRange_SuggestsSmallWeightIncrease_AndResetsRepsToBottom()
    {
        var result = ProgressiveOverloadAdvisor.Suggest(3, 8, 12, Sets(40m, 12, 12, 12), Options)!;

        Assert.Equal(ProgressiveOverloadAdvisor.ActionIncreaseWeight, result.Action);
        Assert.Equal(40m, result.PreviousWeightKg);
        Assert.Equal(2m, result.IncreaseKg);          // %5 = 2 kg
        Assert.Equal(42m, result.SuggestedWeightKg);
        Assert.Equal((8, 12), (result.TargetRepsMin, result.TargetRepsMax));
        Assert.Contains("42 kg", result.Message);
    }

    [Theory]
    [InlineData(10, 1)]    // %5 = 0.5 -> en az 1 kg
    [InlineData(100, 2.5)] // %5 = 5 -> en fazla 2.5 kg
    [InlineData(37.5, 2)]  // %5 = 1.875 -> 0.5 adima yuvarlanir
    public void Increase_IsClampedAndRounded(double working, double expected)
    {
        Assert.Equal((decimal)expected, ProgressiveOverloadAdvisor.IncreaseFor((decimal)working, Options));
    }

    [Fact]
    public void MissedTopOfRangeInAnySet_KeepsWeight()
    {
        var result = ProgressiveOverloadAdvisor.Suggest(3, 8, 12, Sets(40m, 12, 12, 10), Options)!;

        Assert.Equal(ProgressiveOverloadAdvisor.ActionKeepWeight, result.Action);
        Assert.Equal(40m, result.SuggestedWeightKg);
        Assert.Null(result.IncreaseKg);
    }

    [Fact]
    public void FewerSetsThanTarget_KeepsWeight()
    {
        var result = ProgressiveOverloadAdvisor.Suggest(4, 8, 12, Sets(40m, 12, 12, 12), Options)!;

        Assert.Equal(ProgressiveOverloadAdvisor.ActionKeepWeight, result.Action);
    }

    [Fact]
    public void NearFailureRpe_KeepsWeight()
    {
        var sets = Sets(40m, 12, 12, 12);
        sets[2].Rpe = 10m;

        var result = ProgressiveOverloadAdvisor.Suggest(3, 8, 12, sets, Options)!;

        Assert.Equal(ProgressiveOverloadAdvisor.ActionKeepWeight, result.Action);
    }

    [Fact]
    public void DifferentWeightsAcrossSets_UseTheLowestAsWorkingWeight()
    {
        var sets = Sets(40m, 12, 12, 12);
        sets[2].WeightKg = 35m;

        var result = ProgressiveOverloadAdvisor.Suggest(3, 8, 12, sets, Options)!;

        Assert.Equal(35m, result.PreviousWeightKg);
        Assert.Equal(37m, result.SuggestedWeightKg); // 35 * %5 = 1.75 -> 2
    }

    [Fact]
    public void Bodyweight_AtTopOfRange_SuggestsMoreReps()
    {
        var result = ProgressiveOverloadAdvisor.Suggest(3, 10, 15, Sets(null, 15, 15, 15), Options)!;

        Assert.Equal(ProgressiveOverloadAdvisor.ActionIncreaseReps, result.Action);
        Assert.Null(result.SuggestedWeightKg);
    }

    [Fact]
    public void NoHistory_SuggestsStartingInRange()
    {
        var result = ProgressiveOverloadAdvisor.Suggest(3, 8, 12, null, Options)!;

        Assert.Equal(ProgressiveOverloadAdvisor.ActionStart, result.Action);
        Assert.Null(result.SuggestedWeightKg);
    }

    [Fact]
    public void DurationBasedExercise_HasNoSuggestion()
    {
        Assert.Null(ProgressiveOverloadAdvisor.Suggest(3, null, null, Sets(null), Options));
    }

    [Fact]
    public void SingleRepTarget_WithoutRange_ProgressesWhenTargetIsHit()
    {
        var result = ProgressiveOverloadAdvisor.Suggest(3, 10, null, Sets(50m, 10, 10, 10), Options)!;

        Assert.Equal(ProgressiveOverloadAdvisor.ActionIncreaseWeight, result.Action);
        Assert.Equal(52.5m, result.SuggestedWeightKg);
    }
}
