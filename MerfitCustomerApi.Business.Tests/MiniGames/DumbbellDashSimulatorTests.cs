using System.Text.Json;
using MerfitCustomerApi.Business.Services.MiniGames;
using Xunit;

namespace MerfitCustomerApi.Business.Tests.MiniGames;

/// <summary>
/// Sunucudaki replay motoru, MerfitNativeApp'teki engine.ts ile ayni sonucu uretmeli. DumbbellDashGolden.json
/// istemci motoruyla uretilmistir (ayni dosya istemci testlerinde de kullanilir); bir taraf degisip digeri
/// degismezse bu test kirilir.
/// </summary>
public class DumbbellDashSimulatorTests
{
    private sealed record GoldenRng(uint Seed, uint[] Values);
    private sealed record GoldenGame(uint Seed, int[] FlapTicks, int Score, int Ticks);
    private sealed record Golden(int EngineVersion, GoldenRng[] Rng, GoldenGame[] Games);

    private static readonly Golden Data = JsonSerializer.Deserialize<Golden>(
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "MiniGames", "DumbbellDashGolden.json")),
        new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

    public static IEnumerable<object[]> Games => Data.Games.Select(g => new object[] { g.Seed, g.Score });

    [Fact]
    public void EngineVersion_MatchesClient()
    {
        Assert.Equal(DumbbellDashSimulator.EngineVersion, Data.EngineVersion);
    }

    [Fact]
    public void Random_MatchesClientMulberry32()
    {
        foreach (var vector in Data.Rng)
        {
            var state = vector.Seed;
            var values = vector.Values.Select(_ => DumbbellDashSimulator.NextRandom(ref state)).ToArray();
            Assert.Equal(vector.Values, values);
        }
    }

    [Theory]
    [MemberData(nameof(Games))]
    public void Replay_ProducesSameScoreAndTicksAsClient(uint seed, int expectedScore)
    {
        var game = Data.Games.Single(g => g.Seed == seed);

        var result = DumbbellDashSimulator.Replay(game.Seed, game.FlapTicks);

        Assert.True(result.Valid);
        Assert.Equal(expectedScore, result.Score);
        Assert.Equal(game.Ticks, result.Ticks);
    }

    [Fact]
    public void Replay_WithoutInput_FallsToGroundWithZeroScore()
    {
        var result = DumbbellDashSimulator.Replay(12345, Array.Empty<int>());

        Assert.True(result.Valid);
        Assert.Equal(0, result.Score);
        Assert.True(result.Ticks is > 0 and < DumbbellDashSimulator.TickRate);
    }

    [Fact]
    public void Replay_SameInputOnDifferentSeed_DoesNotReproduceScore()
    {
        var game = Data.Games.Single(g => g.Score == 52);

        var result = DumbbellDashSimulator.Replay(game.Seed + 1, game.FlapTicks);

        Assert.False(result.Valid && result.Score == game.Score);
    }

    [Theory]
    [InlineData(new[] { 5, 5 })]
    [InlineData(new[] { 10, 3 })]
    [InlineData(new[] { -1 })]
    public void Replay_RejectsNonIncreasingOrNegativeTicks(int[] ticks)
    {
        var result = DumbbellDashSimulator.Replay(1, ticks);

        Assert.False(result.Valid);
        Assert.Equal("invalid_input", result.Reason);
    }

    [Fact]
    public void Replay_RejectsInputAfterGameOver()
    {
        var result = DumbbellDashSimulator.Replay(1, new[] { 0, 5_000 });

        Assert.False(result.Valid);
        Assert.Equal("input_after_end", result.Reason);
    }

    [Fact]
    public void Replay_StopsAtMaxTicks()
    {
        var game = Data.Games.Single(g => g.Ticks == DumbbellDashSimulator.MaxTicks);

        var result = DumbbellDashSimulator.Replay(game.Seed, game.FlapTicks);

        Assert.Equal(DumbbellDashSimulator.MaxTicks, result.Ticks);
    }
}
