using MerfitCustomerApi.Domain.Common;
using MerfitCustomerApi.Domain.Entities.Enums;

namespace MerfitCustomerApi.Domain.Entities;

/// <summary>
/// Bir egzersizin calistirdigi kas gruplari (ozellikle yardimci kaslar). Ana kas Exercise.PrimaryMuscleGroupId'de
/// tutulmaya devam eder; bu tablo eksik olan yardimci kas bilgisini ekler. Yeni egzersiz olusturmaz.
/// </summary>
public class ExerciseMuscleGroup : BaseEntity
{
    /// <summary>
    /// Egzersizin kimligi.
    /// </summary>
    public long ExerciseId { get; set; }

    /// <summary>
    /// Kas grubunun kimligi.
    /// </summary>
    public long MuscleGroupId { get; set; }

    /// <summary>
    /// Kasin bu egzersizdeki rolu.
    /// </summary>
    public ExerciseMuscleRole Role { get; set; }
}
