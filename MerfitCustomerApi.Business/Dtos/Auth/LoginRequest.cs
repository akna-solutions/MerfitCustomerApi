using System.ComponentModel.DataAnnotations;

namespace MerfitCustomerApi.Business.Dtos.Auth;

/// <summary>
/// Giris (login) istegi icin kullanilan DTO.
/// </summary>
public class LoginRequest
{
    /// <summary>Kullanicinin e-posta adresi veya kullanici adi.</summary>
    [Required(ErrorMessage = "E-posta veya kullanici adi zorunludur.")]
    public string EmailOrUsername { get; set; } = string.Empty;

    /// <summary>Kullanicinin parolasi.</summary>
    [Required(ErrorMessage = "Parola alani zorunludur.")]
    public string Password { get; set; } = string.Empty;
}
