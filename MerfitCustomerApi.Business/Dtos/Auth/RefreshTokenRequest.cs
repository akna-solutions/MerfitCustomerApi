using System.ComponentModel.DataAnnotations;

namespace MerfitCustomerApi.Business.Dtos.Auth;

/// <summary>
/// Oturum yenileme (POST /api/auth/refresh) ve cikis (POST /api/auth/logout) istegi.
/// </summary>
public class RefreshTokenRequest
{
    /// <summary>Giris/kayit veya bir onceki yenileme yanitindaki refresh token.</summary>
    [Required(ErrorMessage = "Yenileme jetonu zorunludur.")]
    public string RefreshToken { get; set; } = string.Empty;
}
