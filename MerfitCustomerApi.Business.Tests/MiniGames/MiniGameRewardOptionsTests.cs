using MerfitCustomerApi.Business.Common;
using Xunit;

namespace MerfitCustomerApi.Business.Tests.MiniGames;

public class MiniGameRewardOptionsTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(9, 0)]
    [InlineData(10, 10)]
    [InlineData(19, 10)]
    [InlineData(20, 20)]
    [InlineData(29, 20)]
    [InlineData(30, 30)]
    [InlineData(49, 30)]
    [InlineData(50, 50)]
    [InlineData(400, 50)]
    public void PointsForScore_UsesDefaultTiers(int score, int expected)
    {
        Assert.Equal(expected, new MiniGameRewardOptions().PointsForScore(score));
    }

    [Fact]
    public void PointsForScore_IsCappedByMaxPointsPerGame()
    {
        var options = new MiniGameRewardOptions { MaxPointsPerGame = 3 };

        Assert.Equal(3, options.PointsForScore(80));
    }

    [Fact]
    public void ConfiguredTiers_ReplaceDefaults_AndAreSorted()
    {
        var options = new MiniGameRewardOptions
        {
            RewardTiers = new() { new() { MinScore = 40, Points = 4 }, new() { MinScore = 15, Points = 1 } },
        };

        Assert.Equal(0, options.PointsForScore(14));
        Assert.Equal(1, options.PointsForScore(39));
        Assert.Equal(4, options.PointsForScore(40));
        Assert.Equal(40, options.NextTier(20)!.MinScore);
        Assert.Null(options.NextTier(40));
    }

    [Theory]
    [InlineData(0, 10)]
    [InlineData(12, 20)]
    [InlineData(49, 50)]
    public void NextTier_ReturnsFirstHigherThreshold(int score, int expectedMinScore)
    {
        Assert.Equal(expectedMinScore, new MiniGameRewardOptions().NextTier(score)!.MinScore);
    }

    [Fact]
    public void Validator_AcceptsDefaults()
    {
        Assert.True(new MiniGameRewardOptionsValidator().Validate(null, new MiniGameRewardOptions()).Succeeded);
    }

    [Fact]
    public void Validator_RejectsInvalidConfiguration()
    {
        var options = new MiniGameRewardOptions
        {
            RewardTiers = new() { new() { MinScore = 10, Points = 3 }, new() { MinScore = 20, Points = 1 }, new() { MinScore = 20, Points = 2 } },
            DailyPointLimit = -1,
            SessionTimeout = TimeSpan.Zero,
        };

        var result = new MiniGameRewardOptionsValidator().Validate(null, options);

        Assert.True(result.Failed);
        Assert.Equal(4, result.Failures!.Count());
    }
}
