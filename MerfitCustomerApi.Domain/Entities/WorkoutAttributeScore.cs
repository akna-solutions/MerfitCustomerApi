using MerfitCustomerApi.Domain.Entities.Enums;
using MerfitCustomerApi.Domain.Common;

namespace MerfitCustomerApi.Domain.Entities;

/// <summary>
/// Bir antrenmanin tek bir ozellik turundeki (orn. Hypertrophy) 0-100 arasi katalog puanini temsil eder.
/// Her antrenman, her ozellik turu icin en fazla bir kayda sahip olabilir.
/// </summary>
public class WorkoutAttributeScore : BaseEntity
{
    /// <summary>
    /// Gecerli en dusuk puan.
    /// </summary>
    public const int MinScore = 0;

    /// <summary>
    /// Gecerli en yuksek puan.
    /// </summary>
    public const int MaxScore = 100;

    /// <summary>
    /// Puanin ait oldugu antrenmanin kimligi.
    /// </summary>
    public long WorkoutId { get; set; }

    /// <summary>
    /// Puanlanan ozellik turu.
    /// </summary>
    public WorkoutAttributeType AttributeType { get; set; }

    /// <summary>
    /// Ozellik puani (0-100).
    /// </summary>
    public int Score { get; set; }
}
