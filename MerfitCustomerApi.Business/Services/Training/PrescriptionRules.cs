using MerfitCustomerApi.Domain.Entities.Enums;

namespace MerfitCustomerApi.Business.Services.Training;

/// <summary>
/// Hedef x mekanik x deneyim -> tekrar araligi, hedef RIR ve dinlenme. Hicbir kural her sette tukenisi
/// (RIR 0) zorunlu kilmaz; en dusuk hedef RIR 1'dir. Baslangic seviyesinde teknik ogrenimi icin RIR daha yuksektir.
/// </summary>
public static class PrescriptionRules
{
    public sealed record RepScheme(int RepsMin, int RepsMax, int TargetRir, int RestSeconds);

    /// <param name="isMainLift">Gunun ilk bilesik hareketi (kuvvet hedefinde daha dusuk tekrar).</param>
    public static RepScheme For(TrainingFocus focus, ExerciseMechanicType mechanic, ExperienceLevel experience, bool isMainLift)
    {
        var compound = mechanic == ExerciseMechanicType.Compound;
        var scheme = focus switch
        {
            TrainingFocus.Strength when compound && isMainLift => new RepScheme(3, 6, 2, 180),
            TrainingFocus.Strength when compound => new RepScheme(5, 8, 2, 150),
            TrainingFocus.Strength => new RepScheme(8, 12, 2, 90),
            TrainingFocus.Hypertrophy when compound => new RepScheme(6, 10, 2, 120),
            TrainingFocus.Hypertrophy => new RepScheme(10, 15, 1, 75),
            TrainingFocus.FatLoss when compound => new RepScheme(8, 12, 2, 90),
            TrainingFocus.FatLoss => new RepScheme(12, 15, 2, 60),
            TrainingFocus.Endurance when compound => new RepScheme(12, 15, 3, 60),
            TrainingFocus.Endurance => new RepScheme(15, 20, 3, 45),
            _ when compound => new RepScheme(8, 12, 3, 90),
            _ => new RepScheme(10, 15, 2, 60),
        };

        return experience switch
        {
            // Baslangic: daha guvenli aralik (kuvvette 3-6 yerine 5-8) ve +1 RIR (en fazla 3).
            ExperienceLevel.Beginner => scheme with
            {
                RepsMin = focus == TrainingFocus.Strength && compound ? Math.Max(scheme.RepsMin, 5) : scheme.RepsMin,
                RepsMax = focus == TrainingFocus.Strength && compound ? Math.Max(scheme.RepsMax, 8) : scheme.RepsMax,
                TargetRir = Math.Min(3, scheme.TargetRir + 1),
            },
            // Ileri seviye: hipertrofi bilesiklerinde 1 RIR'a kadar.
            ExperienceLevel.Advanced when focus == TrainingFocus.Hypertrophy && compound => scheme with { TargetRir = 1 },
            _ => scheme,
        };
    }

    /// <summary>Sureye dayali (plank gibi) egzersizlerde varsayilan sure.</summary>
    public static int TimeBasedSeconds(TrainingFocus focus, ExperienceLevel experience) => experience switch
    {
        ExperienceLevel.Beginner => 30,
        ExperienceLevel.Intermediate => 40,
        _ => focus == TrainingFocus.Endurance ? 60 : 45,
    };

    public static string RirLabel(int rir) => rir switch
    {
        <= 0 => "Tükeniş",
        1 => "Çok zor",
        2 => "Zorlayıcı ama kontrollü",
        3 => "Orta",
        _ => "Rahat",
    };
}
