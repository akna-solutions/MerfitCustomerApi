using MerfitCustomerApi.Domain.Common;

namespace MerfitCustomerApi.Domain.Entities;

/// <summary>
/// Bir WorkoutPlanDay icindeki tek bir egzersizin kullaniciya ozel set/tekrar/dinlenme/sure
/// degerlerini tasir. WorkoutExercise (katalogdaki genel antrenman icerigi) ile karistirilmamali:
/// WorkoutPlanExercise, WorkoutPlanGenerator tarafindan uretilen KISISEL plana ait bir snapshot'tir.
/// Ileride progressive overload (kademeli yuk artirimi) eklendiginde bu satirlar guncellenecektir.
/// </summary>
public class WorkoutPlanExercise : BaseEntity
{
    /// <summary>Iliskili antrenman plani gununun kimligi.</summary>
    public long WorkoutPlanDayId { get; set; }

    /// <summary>Iliskili egzersizin kimligi.</summary>
    public long ExerciseId { get; set; }

    /// <summary>Egzersizin gun icindeki sirasi.</summary>
    public int Order { get; set; }

    /// <summary>Kullaniciya ozel hedef set sayisi.</summary>
    public int Sets { get; set; }

    /// <summary>Kullaniciya ozel set basina hedef tekrar sayisi.</summary>
    public int? Reps { get; set; }

    /// <summary>Kullaniciya ozel setler arasi dinlenme suresi (saniye).</summary>
    public int? RestSeconds { get; set; }

    /// <summary>Sureye dayali egzersizler icin kullaniciya ozel hedef sure (saniye).</summary>
    public int? DurationSeconds { get; set; }
}
