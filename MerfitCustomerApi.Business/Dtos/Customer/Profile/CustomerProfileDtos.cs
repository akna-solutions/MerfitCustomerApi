using System.ComponentModel.DataAnnotations;
using MerfitCustomerApi.Business.Services.Nutrition;

namespace MerfitCustomerApi.Business.Dtos.Customer.Profile;

/// <summary>
/// MerfitNativeApp'in shared/profile/types.ts::ProfileData tipi ile birebir eslesen response.
/// Uygulamadaki tum Profile alt ekranlari (Goals, Body Measurements, Notifications, Privacy, ...)
/// tek bir ProfileContext uzerinden bu sekli okuyup gunceller.
/// </summary>
public class CustomerProfileResponse
{
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;

    /// <summary>E-posta adresi dogrulandi mi (ApplicationUser.EmailConfirmed). E-posta degisince false olur.</summary>
    public bool EmailConfirmed { get; set; }

    /// <summary>
    /// Fitness profili (boy, kilo, hedef...) dolu mu. Yalnizca alisveris icin kayit olan kullanicida false'tur;
    /// uygulama MB Fit ekranlarindan once profili tamamlatir (POST /api/profile/fitness).
    /// </summary>
    public bool FitnessProfileCompleted { get; set; }

    /// <summary>"yyyy-MM-dd".</summary>
    public string DateOfBirth { get; set; } = string.Empty;

    /// <summary>"Erkek" | "Kadın" | "Diğer" | "Belirtmek istemiyorum".</summary>
    public string Gender { get; set; } = string.Empty;

    public int Age { get; set; }
    public decimal Height { get; set; }
    public decimal Weight { get; set; }
    public decimal TargetWeight { get; set; }

    /// <summary>
    /// "BUILD_MUSCLE" (Kas Gelistirme) | "GET_STRONGER" (Guclenme) | "LOSE_WEIGHT" (Yag Kaybi). Eski hedefler
    /// (kiloyu koruma, dayaniklilik, genel fitness) bu uc hedeften birine eslenerek dondurulur.
    /// </summary>
    public string Goal { get; set; } = string.Empty;

    /// <summary>"BEGINNER" | "INTERMEDIATE" | "ADVANCED".</summary>
    public string ExperienceLevel { get; set; } = string.Empty;

    /// <summary>
    /// "FULL_GYM" | "HOME_GYM" | "BODYWEIGHT" | "MINIMAL_EQUIPMENT" - TrainingLocation ve
    /// UserEquipment kayitlarindan turetilen kaba bir kategori (bkz. ProfileService.DeriveEquipmentCategory).
    /// Salt okunur; PUT ile geri yazilmaz (antrenman yeri icin TrainingLocation kullanilir).
    /// </summary>
    public string Equipment { get; set; } = string.Empty;

    /// <summary>"gym" | "home" | "outdoor"; kayitta secilmediyse null.</summary>
    public string? TrainingLocation { get; set; }

    /// <summary>Kayitta secilen haftalik antrenman gunu sayisi (UserProfile.TrainingDaysPerWeek).</summary>
    public int TrainingDaysPerWeek { get; set; }

    public int WorkoutDurationMin { get; set; }

    /// <summary>
    /// "Mon".."Sun" - kullanicinin sectigi gunler (UserProfile.PreferredTrainingDays); secim
    /// yoksa aktif antrenman programindaki (WorkoutPlanDay) gunler. Ikisi de yoksa bos liste.
    /// </summary>
    public List<string> TrainingDays { get; set; } = new();

    /// <summary>"sedentary" | "light" | "moderate" | "active" | "athlete"; secilmediyse null.</summary>
    public string? ActivityLevel { get; set; }

    /// <summary>"none" | "vegetarian" | "vegan"; belirtilmediyse null.</summary>
    public string? DietaryPreference { get; set; }

    /// <summary>
    /// Beyan edilen besin alerjileri: "gluten", "milk", "egg", "treeNuts", "peanut", "soy", "fish",
    /// "shellfish", "sesame". Beslenme programi bu alerjenleri iceren ya da alerjen bilgisi
    /// dogrulanmamis besinleri onermez.
    /// </summary>
    public List<string> Allergies { get; set; } = new();

    /// <summary>Beslenme programinda istenmeyen besinlerin kimlikleri.</summary>
    public List<long> ExcludedFoodIds { get; set; } = new();

    /// <summary>"metric" | "imperial".</summary>
    public string UnitSystem { get; set; } = string.Empty;

    /// <summary>"dark" | "light" | "system".</summary>
    public string Appearance { get; set; } = string.Empty;

    public CustomerNotificationSettingsDto Notifications { get; set; } = new();
    public CustomerPrivacySettingsDto Privacy { get; set; } = new();
    public CustomerProfileStatsDto Stats { get; set; } = new();
}

public class CustomerNotificationSettingsDto
{
    public bool WorkoutReminders { get; set; }
    public bool DailyGoalReminder { get; set; }
    public bool StreakReminder { get; set; }
    public bool ProgressUpdates { get; set; }
    public bool LeaderboardUpdates { get; set; }
    public bool ProductUpdates { get; set; }
}

public class CustomerPrivacySettingsDto
{
    public bool ProfileVisibleOnLeaderboard { get; set; }
    public bool ShareWorkoutStatistics { get; set; }
    public bool PersonalizedRecommendations { get; set; }
}

public class CustomerProfileStatsDto
{
    public int Workouts { get; set; }
    public int Streak { get; set; }
}

/// <summary>
/// PUT /api/profile govdesi. Tum alanlar opsiyoneldir; yalnizca gonderilen (null olmayan)
/// alanlar guncellenir - boylece profil ekranindaki her alt sayfa yalnizca kendi alanini
/// gonderebilir (bkz. shared/profile/ProfileContext.tsx::updateProfile partial patch deseni).
/// </summary>
public class UpdateProfileRequest
{
    [StringLength(100)]
    public string? FirstName { get; set; }

    [StringLength(100)]
    public string? LastName { get; set; }

    [StringLength(30, MinimumLength = 3, ErrorMessage = "Kullanici adi 3-30 karakter arasinda olmalidir.")]
    [RegularExpression("^[a-zA-Z0-9_]+$", ErrorMessage = "Kullanici adi yalnizca harf, rakam ve alt cizgi (_) icerebilir.")]
    public string? Username { get; set; }

    [EmailAddress]
    [StringLength(256)]
    public string? Email { get; set; }

    /// <summary>"yyyy-MM-dd".</summary>
    public string? DateOfBirth { get; set; }

    /// <summary>"Erkek" | "Kadın" | "Diğer" | "Belirtmek istemiyorum".</summary>
    public string? Gender { get; set; }

    [Range(50, 260)]
    public decimal? Height { get; set; }

    [Range(20, 400)]
    public decimal? Weight { get; set; }

    [Range(20, 400)]
    public decimal? TargetWeight { get; set; }

    /// <summary>
    /// "BUILD_MUSCLE" | "GET_STRONGER" | "LOSE_WEIGHT". Eski istemcilerin gonderdigi eski kodlar
    /// (MAINTAIN_WEIGHT, IMPROVE_ENDURANCE, GENERAL_FITNESS) kabul edilir ve ana hedeflere eslenir.
    /// </summary>
    public string? Goal { get; set; }

    /// <summary>
    /// Yag Kaybi secilirken yag orani dusuk aralikta gorunuyorsa API 409 ve LOW_BODY_FAT_CONFIRMATION_REQUIRED
    /// doner. Kullanici uyariyi okuyup "devam et" derse istek bu alan true olarak tekrar gonderilir.
    /// </summary>
    public bool? ConfirmLowBodyFatWarning { get; set; }

    public string? ExperienceLevel { get; set; }

    [Range(5, 240)]
    public int? WorkoutDurationMin { get; set; }

    [Range(1, 7)]
    public int? TrainingDaysPerWeek { get; set; }

    /// <summary>Kullanicinin sectigi antrenman gunleri ("Mon".."Sun"). Bos degilse UserProfile.PreferredTrainingDays'e yazilir ve haftalik gun sayisi bu listeden turetilir.</summary>
    public List<string>? TrainingDays { get; set; }

    /// <summary>"gym" | "home" | "outdoor".</summary>
    public string? TrainingLocation { get; set; }

    /// <summary>"sedentary" | "light" | "moderate" | "active" | "athlete". Gonderilmezse degismez.</summary>
    public string? ActivityLevel { get; set; }

    /// <summary>"none" | "vegetarian" | "vegan". Gonderilmezse degismez.</summary>
    public string? DietaryPreference { get; set; }

    /// <summary>
    /// Besin alerjileri (bkz. CustomerProfileResponse.Allergies). Gonderilmezse (null) degismez; bos liste
    /// tum alerjileri temizler. Bilinmeyen deger reddedilir.
    /// </summary>
    [MaxLength(20)]
    public List<string>? Allergies { get; set; }

    /// <summary>
    /// Beslenme programinda istenmeyen besinlerin kimlikleri. Gonderilmezse (null) degismez; bos liste
    /// temizler. Katalogda olmayan kimlik reddedilir.
    /// </summary>
    [MaxLength(FoodPreferenceFormat.MaxExcludedFoods)]
    public List<long>? ExcludedFoodIds { get; set; }

    public string? UnitSystem { get; set; }
    public string? Appearance { get; set; }

    public UpdateNotificationSettingsRequest? Notifications { get; set; }
    public UpdatePrivacySettingsRequest? Privacy { get; set; }
}

public class UpdateNotificationSettingsRequest
{
    public bool? WorkoutReminders { get; set; }
    public bool? DailyGoalReminder { get; set; }
    public bool? StreakReminder { get; set; }
    public bool? ProgressUpdates { get; set; }
    public bool? LeaderboardUpdates { get; set; }
    public bool? ProductUpdates { get; set; }
}

public class UpdatePrivacySettingsRequest
{
    public bool? ProfileVisibleOnLeaderboard { get; set; }
    public bool? ShareWorkoutStatistics { get; set; }
    public bool? PersonalizedRecommendations { get; set; }
}
