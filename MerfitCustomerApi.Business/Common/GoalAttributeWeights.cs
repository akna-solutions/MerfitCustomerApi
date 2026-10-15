using MerfitCustomerApi.Domain.Entities.Enums;

namespace MerfitCustomerApi.Business.Common;

/// <summary>
/// Tek bir FitnessGoal icin uc ana antrenman ozelliginin (Hipertrofi, Kuvvet, Kardiyo) yuzde agirliklari.
/// Agirliklar negatif olamaz ve toplamlari tam olarak 100 olmalidir
/// (bkz. <see cref="WorkoutPlanGenerationOptionsValidator"/>).
///
/// Endurance, Mobility ve CoreStability puanlari katalogda (admin ekraninda ikincil bilgi olarak)
/// durmaya devam eder ancak hedef uyumuna KATILMAZ: agirliklari her zaman 0'dir ve yapilandirilamaz.
/// Boylece Kardiyo ile Dayaniklilik gibi ortusen ozellikler iki kez sayilmaz.
///
/// Not: Varsayilan degerler urunun ilk surumu icin konfigure edilebilir baslangic degerleridir;
/// bilimsel olarak dogrulanmis katsayilar degildir.
/// </summary>
public class GoalAttributeWeights
{
    /// <summary>Agirliklarin olmasi gereken toplam (yuzde).</summary>
    public const int RequiredTotal = 100;

    /// <summary>Hedef uyumunda kullanilan ana ozellikler.</summary>
    public static readonly IReadOnlyList<WorkoutAttributeType> PrimaryAttributes = new[]
    {
        WorkoutAttributeType.Hypertrophy,
        WorkoutAttributeType.Strength,
        WorkoutAttributeType.Cardio,
    };

    public int Hypertrophy { get; set; }
    public int Strength { get; set; }
    public int Cardio { get; set; }

    /// <summary>Ana ozellik agirliklarinin toplami.</summary>
    public int Total => Hypertrophy + Strength + Cardio;

    /// <summary>Verilen ozellik turunun yuzde agirligini dondurur; ana ozellik degilse 0.</summary>
    public int GetWeight(WorkoutAttributeType attributeType) => attributeType switch
    {
        WorkoutAttributeType.Hypertrophy => Hypertrophy,
        WorkoutAttributeType.Strength => Strength,
        WorkoutAttributeType.Cardio => Cardio,
        _ => 0,
    };
}
