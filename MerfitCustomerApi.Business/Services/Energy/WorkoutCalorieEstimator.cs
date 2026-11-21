using MerfitCustomerApi.Domain.Entities;
using MerfitCustomerApi.Domain.Entities.Enums;
using MerfitCustomerApi.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace MerfitCustomerApi.Business.Services.Energy;

/// <summary>Kalori tahmini icin antrenman turu.</summary>
public enum WorkoutEnergyKind
{
    /// <summary>Agirlik / direnc antrenmani (varsayilan).</summary>
    Strength,
    /// <summary>Genel kardiyo / HIIT.</summary>
    Cardio,
    Running,
    Cycling,
    Walking,
    /// <summary>Mobilite, esneme, yoga.</summary>
    Mobility,
}

/// <summary>Kalori tahmini icin bir antrenmanin (katalog) bilgileri.</summary>
public sealed record WorkoutEnergyProfile(string Title, DifficultyLevel Difficulty, WorkoutEnergyKind Kind);

/// <summary>
/// Antrenman kalorisinin TEK hesaplama noktasi. Oturum tamamlanirken sunucu bu degeri WorkoutSession.CaloriesBurned'e
/// yazar; takvim, ana sayfa, ilerleme ve kalori dengesi ayni saklanan degeri okur (uygulama genelinde tutarli).
///
/// Formul: NET kcal = (MET - 1) x kilo (kg) x aktif sure (saat). MET degerleri Compendium of Physical Activities
/// yaklasik degerleridir; zorluk seviyesi yogunluk olarak kullanilir. "-1" dinlenme metabolizmasidir: o sure icin
/// bazal metabolizma zaten BMR'de sayildigindan antrenman kalorisinden dusulur (BMR ile cifte sayim olmaz).
/// </summary>
public static class WorkoutCalorieEstimator
{
    /// <summary>Tur ve zorluga gore MET.</summary>
    public static decimal Met(WorkoutEnergyKind kind, DifficultyLevel difficulty) => (kind, difficulty) switch
    {
        (WorkoutEnergyKind.Running, DifficultyLevel.Beginner) => 8.3m,
        (WorkoutEnergyKind.Running, DifficultyLevel.Intermediate) => 9.8m,
        (WorkoutEnergyKind.Running, _) => 11.0m,
        (WorkoutEnergyKind.Cycling, DifficultyLevel.Beginner) => 6.8m,
        (WorkoutEnergyKind.Cycling, DifficultyLevel.Intermediate) => 8.0m,
        (WorkoutEnergyKind.Cycling, _) => 10.0m,
        (WorkoutEnergyKind.Walking, DifficultyLevel.Beginner) => 3.5m,
        (WorkoutEnergyKind.Walking, DifficultyLevel.Intermediate) => 4.3m,
        (WorkoutEnergyKind.Walking, _) => 5.0m,
        (WorkoutEnergyKind.Cardio, DifficultyLevel.Beginner) => 6.0m,
        (WorkoutEnergyKind.Cardio, DifficultyLevel.Intermediate) => 7.0m,
        (WorkoutEnergyKind.Cardio, _) => 8.0m,
        (WorkoutEnergyKind.Mobility, DifficultyLevel.Beginner) => 2.5m,
        (WorkoutEnergyKind.Mobility, DifficultyLevel.Intermediate) => 3.0m,
        (WorkoutEnergyKind.Mobility, _) => 3.5m,
        (_, DifficultyLevel.Beginner) => 3.5m,
        (_, DifficultyLevel.Intermediate) => 5.0m,
        _ => 6.0m,
    };

    private static readonly (string Keyword, WorkoutEnergyKind Kind)[] Keywords =
    {
        ("kosu", WorkoutEnergyKind.Running), ("running", WorkoutEnergyKind.Running), ("jogging", WorkoutEnergyKind.Running),
        ("bisiklet", WorkoutEnergyKind.Cycling), ("cycling", WorkoutEnergyKind.Cycling), ("spinning", WorkoutEnergyKind.Cycling), ("bike", WorkoutEnergyKind.Cycling),
        ("yuruyus", WorkoutEnergyKind.Walking), ("walking", WorkoutEnergyKind.Walking), ("walk", WorkoutEnergyKind.Walking),
        ("yoga", WorkoutEnergyKind.Mobility), ("mobilite", WorkoutEnergyKind.Mobility), ("mobility", WorkoutEnergyKind.Mobility),
        ("esneme", WorkoutEnergyKind.Mobility), ("stretch", WorkoutEnergyKind.Mobility), ("pilates", WorkoutEnergyKind.Mobility),
        ("kardiyo", WorkoutEnergyKind.Cardio), ("cardio", WorkoutEnergyKind.Cardio), ("hiit", WorkoutEnergyKind.Cardio),
    };

    /// <summary>
    /// Turu belirler: once kategori adi/slug'i ve antrenman basligindaki acik ifadeler (kosu, bisiklet, yuruyus, yoga,
    /// kardiyo), yoksa katalogdaki ozellik puanlari (Kardiyo veya Mobilite en yuksek ve en az 50 ise), aksi halde
    /// agirlik antrenmani.
    /// </summary>
    public static WorkoutEnergyKind Classify(
        string? title, string? categoryName, string? categorySlug, IReadOnlyDictionary<WorkoutAttributeType, int>? scores)
    {
        foreach (var text in new[] { categorySlug, categoryName, title })
        {
            var normalized = Normalize(text);
            if (normalized.Length == 0)
            {
                continue;
            }

            foreach (var (keyword, kind) in Keywords)
            {
                if (normalized.Contains(keyword, StringComparison.Ordinal))
                {
                    return kind;
                }
            }
        }

        if (scores is { Count: > 0 })
        {
            var top = scores.OrderByDescending(s => s.Value).First();
            if (top.Value >= 50)
            {
                if (top.Key == WorkoutAttributeType.Cardio)
                {
                    return WorkoutEnergyKind.Cardio;
                }

                if (top.Key == WorkoutAttributeType.Mobility)
                {
                    return WorkoutEnergyKind.Mobility;
                }
            }
        }

        return WorkoutEnergyKind.Strength;
    }

    /// <summary>
    /// Kaloriye sayilan aktif sure (saniye): bildirilen sure, yoksa baslangic-bitis farki; 0 ile maxMinutes arasina kirpilir.
    /// Sure hic belirlenemezse null.
    /// </summary>
    public static int? ActiveSeconds(int? durationSeconds, DateTime startedAtUtc, DateTime? completedAtUtc, int maxMinutes)
    {
        int? seconds = durationSeconds is > 0
            ? durationSeconds
            : completedAtUtc is { } end && end > startedAtUtc ? (int)Math.Min(int.MaxValue, (end - startedAtUtc).TotalSeconds) : null;
        return seconds is null ? null : Math.Clamp(seconds.Value, 0, maxMinutes * 60);
    }

    /// <summary>Net (dinlenme ustu) tahmini kalori. Kilo veya sure yoksa null (uydurma deger uretilmez).</summary>
    public static int? EstimateNetKcal(WorkoutEnergyKind kind, DifficultyLevel difficulty, decimal? weightKg, int? activeSeconds)
    {
        if (weightKg is not > 0 || activeSeconds is not > 0)
        {
            return null;
        }

        var hours = activeSeconds.Value / 3600m;
        var met = Met(kind, difficulty);
        return (int)Math.Round((met - 1m) * weightKg.Value * hours, 0, MidpointRounding.AwayFromZero);
    }

    /// <summary>Verilen antrenmanlarin tur/zorluk bilgisini toplu okur (antrenman basina sorgu yoktur).</summary>
    public static async Task<Dictionary<long, WorkoutEnergyProfile>> LoadProfilesAsync(
        IUnitOfWork unitOfWork, IReadOnlyCollection<long> workoutIds, CancellationToken cancellationToken)
    {
        if (workoutIds.Count == 0)
        {
            return new Dictionary<long, WorkoutEnergyProfile>();
        }

        var workouts = await unitOfWork.Repository<Workout>().GetQueryable()
            .Where(w => workoutIds.Contains(w.Id))
            .Select(w => new { w.Id, w.Title, w.Difficulty, w.CategoryId })
            .ToListAsync(cancellationToken);
        var categoryIds = workouts.Select(w => w.CategoryId).Distinct().ToList();
        var categories = await unitOfWork.Repository<WorkoutCategory>().GetQueryable()
            .Where(c => categoryIds.Contains(c.Id))
            .Select(c => new { c.Id, c.Name, c.Slug })
            .ToListAsync(cancellationToken);
        var categoryById = categories.ToDictionary(c => c.Id);
        var scores = (await unitOfWork.Repository<WorkoutAttributeScore>().GetQueryable()
                .Where(s => workoutIds.Contains(s.WorkoutId))
                .Select(s => new { s.WorkoutId, s.AttributeType, s.Score })
                .ToListAsync(cancellationToken))
            .GroupBy(s => s.WorkoutId)
            .ToDictionary(g => g.Key, g => (IReadOnlyDictionary<WorkoutAttributeType, int>)g
                .GroupBy(s => s.AttributeType)
                .ToDictionary(x => x.Key, x => x.Max(s => s.Score)));

        return workouts.ToDictionary(
            w => w.Id,
            w =>
            {
                categoryById.TryGetValue(w.CategoryId, out var category);
                var kind = Classify(w.Title, category?.Name, category?.Slug, scores.GetValueOrDefault(w.Id));
                return new WorkoutEnergyProfile(w.Title, w.Difficulty, kind);
            });
    }

    /// <summary>Kucuk harf + Turkce karakterleri sadelestirir ("Koşu" -> "kosu").</summary>
    private static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var chars = value.Trim().ToLowerInvariant().Select(c => c switch
        {
            'ş' => 's',
            'ı' => 'i',
            'İ' => 'i',
            'ö' => 'o',
            'ü' => 'u',
            'ç' => 'c',
            'ğ' => 'g',
            _ => c,
        });
        return new string(chars.ToArray());
    }
}
