using MerfitCustomerApi.Domain.Entities.Enums;

namespace MerfitCustomerApi.Business.Common;

/// <summary>
/// Tek bir FitnessGoal icin, antrenman ozellik puanlarinin (WorkoutAttributeType) yuzde agirliklari.
/// Agirliklar negatif olamaz ve toplamlari tam olarak 100 olmalidir
/// (bkz. <see cref="WorkoutPlanGenerationOptionsValidator"/>).
///
/// Not: Varsayilan degerler urunun ilk surumu icin konfigure edilebilir baslangic degerleridir;
/// bilimsel olarak dogrulanmis katsayilar degildir.
/// </summary>
public class GoalAttributeWeights
{
    /// <summary>Agirliklarin olmasi gereken toplam (yuzde).</summary>
    public const int RequiredTotal = 100;

    public int Hypertrophy { get; set; }
    public int Strength { get; set; }
    public int Cardio { get; set; }
    public int Endurance { get; set; }
    public int Mobility { get; set; }
    public int CoreStability { get; set; }

    /// <summary>Tum ozellik agirliklarinin toplami.</summary>
    public int Total => Hypertrophy + Strength + Cardio + Endurance + Mobility + CoreStability;

    /// <summary>Verilen ozellik turunun yuzde agirligini dondurur.</summary>
    public int GetWeight(WorkoutAttributeType attributeType) => attributeType switch
    {
        WorkoutAttributeType.Hypertrophy => Hypertrophy,
        WorkoutAttributeType.Strength => Strength,
        WorkoutAttributeType.Cardio => Cardio,
        WorkoutAttributeType.Endurance => Endurance,
        WorkoutAttributeType.Mobility => Mobility,
        WorkoutAttributeType.CoreStability => CoreStability,
        _ => throw new ArgumentOutOfRangeException(nameof(attributeType), attributeType, "Tanimsiz antrenman ozellik turu."),
    };
}
