using MerfitCustomerApi.Domain.Entities.Enums;

namespace MerfitCustomerApi.Business.Common;

/// <summary>
/// Kullaniciya sunulan uc ana fitness hedefi ve eski hedeflerin bunlara eslenmesi.
///
/// FitnessGoal enum'u veritabaninda metin olarak saklanir ve eski istemciler hala eski kodlari
/// gonderebilir; bu yuzden eski uyeler (MaintainWeight, ImproveEndurance, ImproveFitness) silinmez ya
/// da yeniden siralanmaz. Bunun yerine her okuma/yazma noktasinda <see cref="Normalize(FitnessGoal)"/>
/// ile uc ana hedeften birine cevrilir. Mevcut kayitlar ayrica
/// 20261015085544_NormalizeLegacyFitnessGoals veri migration'i ile ayni eslemeye gore guncellenir.
///
/// Esleme:
/// - LoseWeight -> Yag Kaybi, BuildMuscle -> Kas Gelistirme, GetStronger -> Guclenme (ayni kalir).
/// - MaintainWeight, ImproveFitness, ImproveEndurance -> Kas Gelistirme. Kas Gelistirme hipertrofi
///   merkezli varsayilan programdir ve kalori acigi uygulamaz; boylece eski kullanicilardan hicbiri
///   istemeden kilo verme (kalori acigi) planina gecirilmez. Kalori fazlasi da vucut yag orani yuksek
///   gorunuyorsa uygulanmaz (bkz. NutritionCalculator).
/// </summary>
public static class FitnessGoals
{
    /// <summary>Hedef bilinmiyorsa kullanilan ana hedef.</summary>
    public const FitnessGoal Default = FitnessGoal.BuildMuscle;

    /// <summary>Kullaniciya gosterilen uc ana hedef, gosterim sirasiyla.</summary>
    public static readonly IReadOnlyList<FitnessGoal> Primary = new[]
    {
        FitnessGoal.BuildMuscle,
        FitnessGoal.GetStronger,
        FitnessGoal.LoseWeight,
    };

    /// <summary>Hedef uc ana hedeften biri mi.</summary>
    public static bool IsPrimary(FitnessGoal goal) =>
        goal is FitnessGoal.BuildMuscle or FitnessGoal.GetStronger or FitnessGoal.LoseWeight;

    /// <summary>Herhangi bir (eski dahil) hedefi uc ana hedeften birine cevirir.</summary>
    public static FitnessGoal Normalize(FitnessGoal goal) => goal switch
    {
        FitnessGoal.LoseWeight => FitnessGoal.LoseWeight,
        FitnessGoal.GetStronger => FitnessGoal.GetStronger,
        _ => FitnessGoal.BuildMuscle,
    };

    /// <summary>Hedef yoksa <see cref="Default"/>, varsa <see cref="Normalize(FitnessGoal)"/>.</summary>
    public static FitnessGoal Normalize(FitnessGoal? goal) => goal.HasValue ? Normalize(goal.Value) : Default;

    /// <summary>Kullaniciya gosterilen Turkce hedef adi.</summary>
    public static string Label(FitnessGoal goal) => Normalize(goal) switch
    {
        FitnessGoal.LoseWeight => "Yağ Kaybı",
        FitnessGoal.GetStronger => "Güçlenme",
        _ => "Kas Geliştirme",
    };
}
