using System.ComponentModel.DataAnnotations;
using MerfitCustomerApi.Business.Services.BodyComposition;

namespace MerfitCustomerApi.Business.Dtos.Auth;

/// <summary>
/// E-posta/sifre ile kayit (<see cref="RegisterRequest"/>) ve Google ile kayit (<see cref="GoogleRegisterRequest"/>)
/// isteklerinin ortak onboarding alanlari. Alanlar, MerfitNativeApp'teki onboarding akisinin (NameStep,
/// LastNameStep, GenderStep, AgeStep, HeightStep, WeightStep, GoalStep, ActivityStep, ExperienceStep,
/// FrequencyStep, EquipmentStep, AccountStep) topladigi tek parcali OnboardingData objesiyle birebir eslesir.
/// </summary>
public abstract class AccountProfileRequest
{
    /// <summary>Kullanicinin adi (NameStep). UserProfile.FirstName'e dogrudan yazilir.</summary>
    [Required(ErrorMessage = "Ad alani zorunludur.")]
    [StringLength(200, MinimumLength = 2, ErrorMessage = "Ad 2-200 karakter arasinda olmalidir.")]
    public string FirstName { get; set; } = string.Empty;

    /// <summary>Kullanicinin soyadi (LastNameStep). UserProfile.LastName'e dogrudan yazilir.</summary>
    [Required(ErrorMessage = "Soyad alani zorunludur.")]
    [StringLength(200, MinimumLength = 2, ErrorMessage = "Soyad 2-200 karakter arasinda olmalidir.")]
    public string LastName { get; set; } = string.Empty;

    /// <summary>
    /// Kullanicinin kendi belirledigi kullanici adi (AccountStep). UserProfile.Username'e
    /// dogrudan yazilir; benzersizligi AuthService.RegisterAsync icinde (uygulama seviyesinde)
    /// ve UserProfileConfiguration'daki unique index'te (veritabani seviyesinde) korunur.
    /// </summary>
    [Required(ErrorMessage = "Kullanici adi zorunludur.")]
    [StringLength(30, MinimumLength = 3, ErrorMessage = "Kullanici adi 3-30 karakter arasinda olmalidir.")]
    [RegularExpression("^[a-zA-Z0-9_]+$", ErrorMessage = "Kullanici adi yalnizca harf, rakam ve alt cizgi (_) icerebilir.")]
    public string Username { get; set; } = string.Empty;

    /// <summary>"male" | "female" (GenderStep). RN tarafi "other" secenegi sunmuyor.</summary>
    public string? Gender { get; set; }

    /// <summary>Kullanicinin yasi (AgeStep). RN tarafinda 13-100 araligi dogrulaniyor.</summary>
    [Range(13, 100, ErrorMessage = "Yas 13-100 araliginda olmalidir.")]
    public int? Age { get; set; }

    /// <summary>"cm" | "ft_in" (HeightStep).</summary>
    public string HeightUnit { get; set; } = "cm";

    /// <summary>HeightUnit "cm" iken doldurulur.</summary>
    public decimal? HeightCm { get; set; }

    /// <summary>HeightUnit "ft_in" iken doldurulur.</summary>
    public int? HeightFeet { get; set; }

    /// <summary>HeightUnit "ft_in" iken doldurulur.</summary>
    public int? HeightInches { get; set; }

    /// <summary>"kg" | "lb" (WeightStep).</summary>
    public string WeightUnit { get; set; } = "kg";

    /// <summary>Kullanicinin guncel kilosu, WeightUnit birimine gore (WeightStep).</summary>
    public decimal? Weight { get; set; }

    /// <summary>
    /// build_muscle (Kas Gelistirme) | get_stronger (Guclenme) | lose_weight (Yag Kaybi) (GoalStep). Eski
    /// istemcilerin gonderdigi improve_fitness | maintain_weight | improve_endurance kabul edilir ve
    /// Kas Gelistirme'ye eslenir.
    /// </summary>
    public string? Goal { get; set; }

    /// <summary>Opsiyonel: kullanicinin bildigi vucut yag orani (%), BodyStep.</summary>
    [Range(BodyMeasurementLimits.MinBodyFatPercentage, BodyMeasurementLimits.MaxBodyFatPercentage, ErrorMessage = "Vücut yağ oranı %2-75 arasında olmalıdır.")]
    public decimal? BodyFatPercentage { get; set; }

    /// <summary>Opsiyonel: boyun cevresi (cm), yag orani tahmini icin.</summary>
    [Range(BodyMeasurementLimits.MinNeckCm, BodyMeasurementLimits.MaxNeckCm, ErrorMessage = "Boyun çevresi 20-80 cm arasında olmalıdır.")]
    public decimal? NeckCm { get; set; }

    /// <summary>Opsiyonel: bel cevresi (cm), yag orani tahmini icin.</summary>
    [Range(BodyMeasurementLimits.MinWaistCm, BodyMeasurementLimits.MaxWaistCm, ErrorMessage = "Bel çevresi 40-250 cm arasında olmalıdır.")]
    public decimal? WaistCm { get; set; }

    /// <summary>Opsiyonel: kalca cevresi (cm), kadinlarda yag orani tahmini icin.</summary>
    [Range(BodyMeasurementLimits.MinHipCm, BodyMeasurementLimits.MaxHipCm, ErrorMessage = "Kalça çevresi 50-250 cm arasında olmalıdır.")]
    public decimal? HipCm { get; set; }

    /// <summary>
    /// Yag Kaybi secilip olcumlere gore yag orani dusuk araliktaysa kayit 409 (LOW_BODY_FAT_CONFIRMATION_REQUIRED)
    /// ile reddedilir; kullanici uyariyi onaylarsa istek bu alan true olarak tekrar gonderilir.
    /// </summary>
    public bool? ConfirmLowBodyFatWarning { get; set; }

    /// <summary>sedentary | light | moderate | active | athlete (ActivityStep).</summary>
    public string? ActivityLevel { get; set; }

    /// <summary>beginner | intermediate | advanced (ExperienceStep).</summary>
    public string? TrainingExperience { get; set; }

    /// <summary>Haftada kac gun antrenman yapilmak istendigi (FrequencyStep).</summary>
    [Range(1, 7, ErrorMessage = "Antrenman gunu 1-7 araliginda olmalidir.")]
    public int? TrainingDays { get; set; }

    /// <summary>gym | home | outdoor (EquipmentStep).</summary>
    public string? TrainingLocation { get; set; }

    /// <summary>
    /// EquipmentStep'te secilen ekipmanlarin Equipment tablosundaki kimlikleri (Id).
    /// Register sirasinda UserEquipment kayitlarini olusturmak icin kullanilir.
    /// </summary>
    public List<long>? EquipmentIds { get; set; }
}
