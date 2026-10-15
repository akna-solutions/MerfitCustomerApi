using MerfitCustomerApi.Business.Common;
using MerfitCustomerApi.Domain.Entities.Enums;
using Xunit;

namespace MerfitCustomerApi.Business.Tests.BodyComposition;

/// <summary>Kullaniciya yalnizca uc hedef sunulur; eski hedefler guvenli sekilde eslenir.</summary>
public class FitnessGoalsTests
{
    [Fact]
    public void OnlyThreePrimaryGoals_AreOffered()
    {
        Assert.Equal(new[] { FitnessGoal.BuildMuscle, FitnessGoal.GetStronger, FitnessGoal.LoseWeight }, FitnessGoals.Primary);
    }

    [Theory]
    [InlineData(FitnessGoal.LoseWeight, FitnessGoal.LoseWeight)]
    [InlineData(FitnessGoal.GetStronger, FitnessGoal.GetStronger)]
    [InlineData(FitnessGoal.BuildMuscle, FitnessGoal.BuildMuscle)]
    [InlineData(FitnessGoal.MaintainWeight, FitnessGoal.BuildMuscle)]
    [InlineData(FitnessGoal.ImproveFitness, FitnessGoal.BuildMuscle)]
    [InlineData(FitnessGoal.ImproveEndurance, FitnessGoal.BuildMuscle)]
    public void LegacyGoals_MapToPrimaryGoals(FitnessGoal input, FitnessGoal expected)
    {
        Assert.Equal(expected, FitnessGoals.Normalize(input));
        Assert.True(FitnessGoals.IsPrimary(FitnessGoals.Normalize(input)));
    }

    [Fact]
    public void MissingGoal_DefaultsToBuildMuscle_AndEnumOrderIsUnchanged()
    {
        Assert.Equal(FitnessGoal.BuildMuscle, FitnessGoals.Normalize((FitnessGoal?)null));
        // Enum degerleri silinmez/yeniden siralanmaz (veritabaninda string saklanir, istemciler sayisal deger de gorebilir).
        Assert.Equal(
            new[] { "LoseWeight", "BuildMuscle", "MaintainWeight", "ImproveEndurance", "GetStronger", "ImproveFitness" },
            Enum.GetNames<FitnessGoal>());
    }

    [Fact]
    public void Labels_AreTheThreeUserFacingNames()
    {
        Assert.Equal("Kas Geliştirme", FitnessGoals.Label(FitnessGoal.BuildMuscle));
        Assert.Equal("Güçlenme", FitnessGoals.Label(FitnessGoal.GetStronger));
        Assert.Equal("Yağ Kaybı", FitnessGoals.Label(FitnessGoal.LoseWeight));
        Assert.Equal("Kas Geliştirme", FitnessGoals.Label(FitnessGoal.MaintainWeight));
    }
}
