using MerfitCustomerApi.Domain.Common;
using MerfitCustomerApi.Domain.Entities.Enums;

namespace MerfitCustomerApi.Domain.Entities;

/// <summary>
/// Kullanicinin antrenman motoru icin verdigi tercihler. UserProfile'da bulunmayan alanlari tutar;
/// gun sayisi, sure, deneyim ve ekipman UserProfile / UserEquipment'tan okunmaya devam eder.
/// Kimlik listeleri mevcut desende (PreferredTrainingDays, ExcludedFoodIds) virgulle ayrilmis metin olarak saklanir.
/// </summary>
public class UserTrainingPreference : BaseEntity
{
    /// <summary>
    /// Kullanicinin kimligi (kullanici basina tek kayit).
    /// </summary>
    public long UserId { get; set; }

    /// <summary>
    /// Antrenman odagi. Bos ise profil hedefinden turetilir.
    /// </summary>
    public TrainingFocus? Focus { get; set; }

    /// <summary>
    /// Kullanicinin antrenman motoru icin sectigi haftalik antrenman gunu sayisi (2-7). Bos ise
    /// UserProfile.TrainingDaysPerWeek kullanilir. Musait gunler UserProfile.PreferredTrainingDays'ten okunur.
    /// </summary>
    public int? DaysPerWeek { get; set; }

    /// <summary>
    /// Oncelikli kas grubu kimlikleri.
    /// </summary>
    public string? PriorityMuscleGroupIds { get; set; }

    /// <summary>
    /// Tercih edilen egzersiz kimlikleri.
    /// </summary>
    public string? PreferredExerciseIds { get; set; }

    /// <summary>
    /// Kacinilmak istenen egzersiz kimlikleri. Motor bu egzersizleri hic secmez.
    /// </summary>
    public string? AvoidedExerciseIds { get; set; }

    /// <summary>
    /// Kullanicinin kisitli / zorlamak istemedigi kas grubu kimlikleri (ana kasi bunlar olan hareketler secilmez).
    /// </summary>
    public string? LimitedMuscleGroupIds { get; set; }

    /// <summary>
    /// Kullanicinin bildirdigi fiziksel kisitlama notu (serbest metin; motor yorumlamaz, teshis uretilmez).
    /// </summary>
    public string? LimitationsNote { get; set; }
}
