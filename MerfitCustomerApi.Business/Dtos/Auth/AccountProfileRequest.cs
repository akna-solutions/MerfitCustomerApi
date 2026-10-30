using System.ComponentModel.DataAnnotations;

namespace MerfitCustomerApi.Business.Dtos.Auth;

/// <summary>
/// E-posta/sifre ile kayit (<see cref="RegisterRequest"/>) ve Google ile kayit (<see cref="GoogleRegisterRequest"/>)
/// isteklerinin ortak onboarding alanlari. Alanlar, MerfitNativeApp'teki onboarding akisinin (NameStep,
/// LastNameStep, GenderStep, AgeStep, HeightStep, WeightStep, GoalStep, ActivityStep, ExperienceStep,
/// FrequencyStep, EquipmentStep, AccountStep) topladigi tek parcali OnboardingData objesiyle birebir eslesir.
/// Fitness alanlari <see cref="FitnessProfileRequest"/>'ten gelir; <see cref="ShoppingOnly"/> true ise yok sayilir.
/// </summary>
public abstract class AccountProfileRequest : FitnessProfileRequest
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

    /// <summary>
    /// Kullanici yalnizca MB Shop'ta alisveris icin kayit oluyorsa true: boy, kilo, hedef gibi fitness alanlari
    /// sorulmaz ve gonderilse de yok sayilir; kisisellestirme kayitlari (hedef, beslenme hedefi, plan isi)
    /// olusturulmaz. Kullanici MB Fit'i kullanmak istediginde POST /api/profile/fitness ile profilini tamamlar.
    /// Gonderilmezse (eski istemciler) false kabul edilir ve fitness alanlari eskisi gibi zorunludur.
    /// </summary>
    public bool? ShoppingOnly { get; set; }
}
