using MerfitCustomerApi.Domain.Entities.Enums;

namespace MerfitCustomerApi.Business.Common;

/// <summary>
/// Haftalik antrenman gunu sayisini kullanici secmez; hedef ve deneyime gore algoritma belirler.
/// Kullanici yalnizca antrenman yapabilecegi gunleri isaretler; bu gunler arasindan dinlenme
/// araliklari en dengeli olacak sekilde gun secilir. Tamamen deterministik ve kural tabanlidir.
/// </summary>
public static class TrainingDayPlanner
{
    public const int MinTrainingDays = 2;
    public const int MaxTrainingDays = 6;

    /// <summary>
    /// Hedef gun sayisi: Baslangic 3, Orta 4, Ileri 5; yag kaybi hedefinde kardiyo icin +1 (en fazla 6).
    /// </summary>
    public static int TargetTrainingDays(FitnessGoal goal, ExperienceLevel experience)
    {
        var days = experience switch
        {
            ExperienceLevel.Intermediate => 4,
            ExperienceLevel.Advanced => 5,
            _ => 3,
        };
        if (FitnessGoals.Normalize(goal) == FitnessGoal.LoseWeight)
        {
            days++;
        }
        return Math.Clamp(days, MinTrainingDays, MaxTrainingDays);
    }

    /// <summary>
    /// Musait gunlerden (bos ise haftanin tum gunleri) en fazla <paramref name="targetDays"/> gun secer.
    /// Secim sirasiyla: (1) iki antrenman arasindaki en kisa ara en uzun olsun, (2) arka arkaya antrenman
    /// gunu serisi en kisa olsun, (3) en uzun dinlenme arasi en kisa olsun; esitlikte haftanin daha erken
    /// gunleri tercih edilir. Hafta dongusel kabul edilir (Pazar'dan sonra Pazartesi gelir).
    /// </summary>
    public static IReadOnlyList<DayOfWeek> ChooseDays(IEnumerable<DayOfWeek> availableDays, int targetDays)
    {
        var available = availableDays
            .Distinct()
            .OrderBy(TrainingDayCodes.MondayFirstIndex)
            .ToList();
        if (available.Count == 0)
        {
            available = TrainingDayCodes.All
                .Select(code => TrainingDayCodes.TryParseCode(code, out var d) ? d : DayOfWeek.Monday)
                .ToList();
        }

        if (targetDays <= 0 || available.Count <= targetDays)
        {
            return available;
        }

        var positions = available.Select(TrainingDayCodes.MondayFirstIndex).ToArray();
        int[]? best = null;
        (int MinGap, int MaxRun, int MaxGap) bestScore = (-1, int.MaxValue, int.MaxValue);

        foreach (var combination in Combinations(positions.Length, targetDays))
        {
            var selected = combination.Select(i => positions[i]).ToArray();
            var score = Score(selected);
            var better = score.MinGap > bestScore.MinGap
                || (score.MinGap == bestScore.MinGap && score.MaxRun < bestScore.MaxRun)
                || (score.MinGap == bestScore.MinGap && score.MaxRun == bestScore.MaxRun && score.MaxGap < bestScore.MaxGap);
            if (better)
            {
                best = combination;
                bestScore = score;
            }
        }

        return best!.Select(i => available[i]).ToList();
    }

    private static (int MinGap, int MaxRun, int MaxGap) Score(int[] sortedPositions)
    {
        var gaps = new int[sortedPositions.Length];
        for (var i = 0; i < sortedPositions.Length; i++)
        {
            var next = i + 1 < sortedPositions.Length ? sortedPositions[i + 1] : sortedPositions[0] + 7;
            gaps[i] = next - sortedPositions[i];
        }

        // En uzun arka arkaya antrenman serisi (dongusel): ardisik gap==1 sayisi + 1.
        var maxRun = 1;
        if (gaps.All(g => g == 1))
        {
            maxRun = gaps.Length;
        }
        else
        {
            var start = Array.FindIndex(gaps, g => g != 1);
            var run = 1;
            for (var k = 1; k <= gaps.Length; k++)
            {
                var gap = gaps[(start + k) % gaps.Length];
                run = gap == 1 ? run + 1 : 1;
                maxRun = Math.Max(maxRun, run);
            }
        }

        return (gaps.Min(), maxRun, gaps.Max());
    }

    /// <summary>n elemandan k'li kombinasyonlar, sozluk sirasiyla (en fazla C(7,3) = 35).</summary>
    private static IEnumerable<int[]> Combinations(int n, int k)
    {
        var indices = Enumerable.Range(0, k).ToArray();
        while (true)
        {
            yield return (int[])indices.Clone();
            var i = k - 1;
            while (i >= 0 && indices[i] == n - k + i)
            {
                i--;
            }
            if (i < 0)
            {
                yield break;
            }
            indices[i]++;
            for (var j = i + 1; j < k; j++)
            {
                indices[j] = indices[j - 1] + 1;
            }
        }
    }
}
