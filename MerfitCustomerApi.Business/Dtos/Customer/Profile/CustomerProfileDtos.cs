using System.ComponentModel.DataAnnotations;

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

    /// <summary>"yyyy-MM-dd".</summary>
    public string DateOfBirth { get; set; } = string.Empty;

    /// <summary>"Erkek" | "Kadın" | "Diğer" | "Belirtmek istemiyorum".</summary>
    public string Gender { get; set; } = string.Empty;

    public int Age { get; set; }
    public decimal Height { get; set; }
    public decimal Weight { get; set; }
    public decimal TargetWeight { get; set; }

    /// <summary>"LOSE_WEIGHT" | "BUILD_MUSCLE" | "MAINTAIN_WEIGHT" | "IMPROVE_ENDURANCE" | "GET_STRONGER" | "GENERAL_FITNESS".</summary>
    public string Goal { get; set; } = string.Empty;

    /// <summary>"BEGINNER" | "INTERMEDIATE" | "ADVANCED".</summary>
    public string ExperienceLevel { get; set; } = string.Empty;

    /// <summary>
    /// "FULL_GYM" | "HOME_GYM" | "BODYWEIGHT" | "MINIMAL_EQUIPMENT" - kullanicinin gercek
    /// UserEquipment kayitlarindan turetilen kaba bir kategori (bkz. ProfileService.DeriveEquipmentCategory).
    /// Granuler ekipman listesi (onboarding'de toplanan) bu alanda kaybolur; PUT ile geri yazilmaz.
    /// </summary>
    public string Equipment { get; set; } = string.Empty;

    public int WorkoutDurationMin { get; set; }

    /// <summary>
    /// "Mon".."Sun" - UserProfile yalnizca haftalik GUN SAYISINI (TrainingDaysPerWeek) sakladigindan,
    /// buradaki spesifik gunler sadece dogru SAYIYI yansitmak icin turetilmis bir temsildir; hangi
    /// gunlerin secildigi ayrica saklanmaz (bkz. FINAL RAPOR - Remaining Work).
    /// </summary>
    public List<string> TrainingDays { get; set; } = new();

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

    public string? Goal { get; set; }
    public string? ExperienceLevel { get; set; }

    [Range(5, 240)]
    public int? WorkoutDurationMin { get; set; }

    /// <summary>"Mon".."Sun" - yalnizca uzunlugu (haftalik gun sayisi) kalici olarak saklanir.</summary>
    public List<string>? TrainingDays { get; set; }

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
