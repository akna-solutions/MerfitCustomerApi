namespace MerfitCustomerApi.Business.Services.MiniGames;

/// <summary>
/// Dumbbell Dash oyun motorunun sunucu kopyasi. MerfitNativeApp'teki
/// src/shared/miniGame/dumbbellDash/engine.ts ile BIREBIR ayni kurallari uygular; istemcinin gonderdigi
/// skor yerine oyunu seed + zipla tick'leri ile bastan oynatip skoru kendisi hesaplamak icin kullanilir.
///
/// Determinizm icin tum degerler tamsayidir, rastgelelik seed'li mulberry32'dir ve adim sirasi
/// (zipla -> yer cekimi -> hareket -> engel uretimi -> skor -> carpisma) istemciyle aynidir. Iki taraf
/// ayni "golden" test verisiyle dogrulanir (DumbbellDashGolden.json). Kurallar degisirse EngineVersion
/// artirilmali ve iki repo birlikte guncellenmelidir.
/// </summary>
public static class DumbbellDashSimulator
{
    public const int EngineVersion = 1;

    public const int TickRate = 60;
    public const int WorldW = 1_400_000;
    public const int WorldH = 1_000_000;
    public const int GroundH = 70_000;
    public const int GroundY = WorldH - GroundH;

    public const int PlayerX = 330_000;
    public const int PlayerHalf = 38_000;
    public const int StartY = 450_000;

    public const int Gravity = 600;
    public const int FlapVy = -10_500;
    public const int MaxFallVy = 15_000;

    public const int ObstacleW = 120_000;
    public const int BarInset = 30_000;
    public const int PlateH = 64_000;
    public const int GapH = 300_000;
    public const int ObstacleSpacing = 560_000;
    public const int GapCenterMin = 230_000;
    public const int GapCenterMax = GroundY - 230_000;
    public const int MaxGapDelta = 280_000;

    public const int BaseSpeed = 4_800;
    public const int SpeedPerPoint = 40;
    public const int SpeedRampCap = 40;

    /// <summary>Tek oyunun ust siniri (10 dk).</summary>
    public const int MaxTicks = TickRate * 60 * 10;

    private sealed class Obstacle
    {
        public int X;
        public int GapTop;
        public int GapBottom;
        public bool Passed;
    }

    private sealed class State
    {
        public int Tick;
        public int Y = StartY;
        public int Vy;
        public int Score;
        public bool Alive = true;
        public readonly List<Obstacle> Obstacles = new();
        public int LastGapCenter;
        public uint Rng;
    }

    /// <summary>mulberry32; istemcideki nextRandom ile ayni bitleri uretir.</summary>
    public static uint NextRandom(ref uint rng)
    {
        unchecked
        {
            rng += 0x6D2B79F5u;
            var t = rng;
            t = (t ^ (t >> 15)) * (t | 1u);
            t ^= t + (t ^ (t >> 7)) * (t | 61u);
            return t ^ (t >> 14);
        }
    }

    private static int NextGapCenter(State state, int? previous)
    {
        const int range = GapCenterMax - GapCenterMin;
        var center = GapCenterMin + (int)(NextRandom(ref state.Rng) % (uint)(range + 1));
        if (previous.HasValue)
        {
            center = Math.Min(Math.Max(center, previous.Value - MaxGapDelta), previous.Value + MaxGapDelta);
        }

        return center;
    }

    private static void SpawnObstacle(State state, int x, int? previousCenter)
    {
        var center = NextGapCenter(state, previousCenter);
        state.Obstacles.Add(new Obstacle { X = x, GapTop = center - GapH / 2, GapBottom = center + GapH / 2 });
        state.LastGapCenter = center;
    }

    private static bool OverlapsX(int left, int right) => left < PlayerX + PlayerHalf && right > PlayerX - PlayerHalf;

    private static bool HitsObstacle(int y, Obstacle obstacle)
    {
        var top = y - PlayerHalf;
        var bottom = y + PlayerHalf;
        var x = obstacle.X;

        if (OverlapsX(x + BarInset, x + ObstacleW - BarInset) && (top < obstacle.GapTop || bottom > obstacle.GapBottom))
        {
            return true;
        }

        if (OverlapsX(x, x + ObstacleW))
        {
            var hitsTopPlate = top < obstacle.GapTop && bottom > obstacle.GapTop - PlateH;
            var hitsBottomPlate = bottom > obstacle.GapBottom && top < obstacle.GapBottom + PlateH;
            if (hitsTopPlate || hitsBottomPlate)
            {
                return true;
            }
        }

        return false;
    }

    private static void Step(State state, bool flap)
    {
        if (!state.Alive)
        {
            return;
        }

        if (flap)
        {
            state.Vy = FlapVy;
        }

        state.Vy = Math.Min(state.Vy + Gravity, MaxFallVy);
        state.Y += state.Vy;

        var speed = BaseSpeed + Math.Min(state.Score, SpeedRampCap) * SpeedPerPoint;
        foreach (var obstacle in state.Obstacles)
        {
            obstacle.X -= speed;
        }

        while (state.Obstacles.Count > 0 && state.Obstacles[0].X + ObstacleW < 0)
        {
            state.Obstacles.RemoveAt(0);
        }

        if (state.Obstacles.Count == 0)
        {
            SpawnObstacle(state, WorldW, state.LastGapCenter);
        }
        else
        {
            var last = state.Obstacles[^1];
            if (last.X <= WorldW - ObstacleSpacing)
            {
                SpawnObstacle(state, last.X + ObstacleSpacing, state.LastGapCenter);
            }
        }

        foreach (var obstacle in state.Obstacles)
        {
            if (!obstacle.Passed && obstacle.X + ObstacleW < PlayerX - PlayerHalf)
            {
                obstacle.Passed = true;
                state.Score++;
            }
        }

        if (state.Y - PlayerHalf < 0 || state.Y + PlayerHalf > GroundY)
        {
            state.Alive = false;
        }
        else
        {
            foreach (var obstacle in state.Obstacles)
            {
                if (HitsObstacle(state.Y, obstacle))
                {
                    state.Alive = false;
                    break;
                }
            }
        }

        state.Tick++;
        if (state.Tick >= MaxTicks)
        {
            state.Alive = false;
        }
    }

    /// <summary>
    /// Oyunu seed ve zipla tick'leriyle bastan oynatir. Tick listesi kesin artan, negatif olmayan
    /// tamsayilar olmali ve tamami oyun bitmeden once kullanilmis olmalidir; aksi halde gecersizdir.
    /// </summary>
    public static DumbbellDashReplayResult Replay(uint seed, IReadOnlyList<int> flapTicks)
    {
        for (var i = 0; i < flapTicks.Count; i++)
        {
            if (flapTicks[i] < 0 || (i > 0 && flapTicks[i] <= flapTicks[i - 1]))
            {
                return DumbbellDashReplayResult.Invalid("invalid_input");
            }
        }

        var state = new State { Rng = seed };
        SpawnObstacle(state, WorldW, null);

        var index = 0;
        while (state.Alive)
        {
            var flap = index < flapTicks.Count && flapTicks[index] == state.Tick;
            if (flap)
            {
                index++;
            }

            Step(state, flap);
        }

        return index < flapTicks.Count
            ? DumbbellDashReplayResult.Invalid("input_after_end")
            : new DumbbellDashReplayResult(true, state.Score, state.Tick, null);
    }
}

public record DumbbellDashReplayResult(bool Valid, int Score, int Ticks, string? Reason)
{
    public static DumbbellDashReplayResult Invalid(string reason) => new(false, 0, 0, reason);
}
