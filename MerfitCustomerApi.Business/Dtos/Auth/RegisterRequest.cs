using System.ComponentModel.DataAnnotations;

namespace MerfitCustomerApi.Business.Dtos.Auth;

/// <summary>
/// Kayit (register) istegi icin kullanilan DTO: ortak onboarding alanlarina (bkz. <see cref="AccountProfileRequest"/>)
/// ek olarak AccountStep'teki e-posta ve parola.
/// </summary>
public class RegisterRequest : AccountProfileRequest
{
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
}
