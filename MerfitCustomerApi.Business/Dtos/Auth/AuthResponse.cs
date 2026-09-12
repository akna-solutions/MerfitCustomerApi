namespace MerfitCustomerApi.Business.Dtos.Auth;

/// <summary>
/// Basarili kayit/giris isleminden sonra doner. RN tarafi bu token'lari alip
/// kullaniciyi dogrudan dashboard'a yonlendirebilir (SuccessScreen -> handleStartTraining).
/// </summary>
public class AuthResponse
{
    public long UserId { get; set; }

    public string Email { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    /// <summary>API isteklerinde Authorization: Bearer header'inda kullanilacak JWT.</summary>
    public string AccessToken { get; set; } = string.Empty;

    /// <summary>Access token suresi doldugunda yenilemek icin kullanilacak jeton.</summary>
    public string RefreshToken { get; set; } = string.Empty;

    /// <summary>AccessToken'in son gecerlilik tarihi (UTC).</summary>
    public DateTime AccessTokenExpiresAt { get; set; }
}