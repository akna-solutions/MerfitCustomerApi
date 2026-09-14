using System.ComponentModel.DataAnnotations;

namespace MerfitCustomerApi.Business.Dtos.Auth;

/// <summary>
/// Kayit (register) istegi icin kullanilan DTO.
/// Alanlar, MerfitNativeApp'teki onboarding akisinin (NameStep, GenderStep, AgeStep, HeightStep,
/// WeightStep, GoalStep, ActivityStep, ExperienceStep, FrequencyStep, EquipmentStep, AccountStep)
/// topladigi tek parcali OnboardingData objesiyle birebir eslesecek sekilde tasarlanmistir.
/// </summary>
public class RegisterRequest
{
    /// <summary>Kullanicinin tam adi (NameStep).</summary>
    [Required(ErrorMessage = "Ad alani zorunludur.")]
    [StringLength(200, MinimumLength = 2, ErrorMessage = "Ad 2-200 karakter arasinda olmalidir.")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Kullanicinin kendi belirledigi kullanici adi (AccountStep). UserProfile.Username'e
    /// dogrudan yazilir; benzersizligi AuthService.RegisterAsync icinde (uygulama seviyesinde)
    /// ve UserProfileConfiguration'daki unique index'te (veritabani seviyesinde) korunur.
    /// </summary>
    [Required(ErrorMessage = "Kullanici adi zorunludur.")]
    [StringLength(30, MinimumLength = 3, ErrorMessage = "Kullanici adi 3-30 karakter arasinda olmalidir.")]
    [RegularExpression("^[a-zA-Z0-9_]+$", ErrorMessage = "Kullanici adi yalnizca harf, rakam ve alt cizgi (_) icerebilir.")]
    public string Username { get; set; } = string.Empty;

    /// <summary>Kullanicinin e-posta adresi (AccountStep).</summary>
    [Required(ErrorMessage = "E-posta alani zorunludur.")]
    [EmailAddress(ErrorMessage = "Gecerli bir e-posta adresi giriniz.")]
    [StringLength(500)]
    public string Email { get; set; } = string.Empty;

    /// <summary>Kullanicinin parolasi (AccountStep). RN tarafinda minimum 6 karakter kontrolu var.</summary>
    [Required(ErrorMessage = "Parola alani zorunludur.")]
    [StringLength(100, MinimumLength = 6, ErrorMessage = "Parola en az 6 karakter olmalidir.")]
    public string Password { get; set; } = string.Empty;

    /// <summary>Parola tekrari (AccountStep). Business katmaninda Password ile karsilastirilir.</summary>
    [Required(ErrorMessage = "Parola tekrari zorunludur.")]
    public string ConfirmPassword { get; set; } = string.Empty;

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
    /// lose_weight | build_muscle | get_stronger | improve_fitness | maintain_weight | improve_endurance (GoalStep).
    /// </summary>
    public string? Goal { get; set; }

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