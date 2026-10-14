using System.ComponentModel.DataAnnotations;

namespace MerfitCustomerApi.Business.Dtos.Auth;

/// <summary>
/// POST /api/auth/google istegi: mobil uygulamanin Google Sign-In ile aldigi ID token.
/// </summary>
public class GoogleSignInRequest
{
    /// <summary>Google'in imzaladigi ID token (JWT). Sunucuda imza, aud, iss ve sure kontrol edilir.</summary>
    [Required(ErrorMessage = "Google kimlik bilgisi zorunludur.")]
    [StringLength(8192)]
    public string IdToken { get; set; } = string.Empty;
}

/// <summary>
/// POST /api/auth/google yaniti. Google hesabi bir MB FIT hesabina bagliysa (veya ayni dogrulanmis e-postayla
/// kayitli bir hesaba simdi baglandiysa) <see cref="Auth"/> doludur. Hesap yoksa
/// <see cref="RegistrationRequired"/> true olur ve uygulama onboarding akisina <see cref="Profile"/>
/// bilgileriyle gecip sonunda POST /api/auth/google/register cagirir.
/// </summary>
public class GoogleSignInResponse
{
    public bool RegistrationRequired { get; set; }

    /// <summary>Giris basariliysa token cifti; kayit gerekiyorsa null.</summary>
    public AuthResponse? Auth { get; set; }

    /// <summary>Kayit gerekiyorsa onboarding'i on doldurmak icin Google profil bilgileri; aksi halde null.</summary>
    public GoogleProfileDto? Profile { get; set; }
}

/// <summary>Google ID token'indan okunan, onboarding'i on doldurmak icin kullanilan profil bilgileri.</summary>
public class GoogleProfileDto
{
    public string Email { get; set; } = string.Empty;

    public string? FirstName { get; set; }

    public string? LastName { get; set; }
}

/// <summary>
/// POST /api/auth/google/register istegi: onboarding alanlari + Google ID token. E-posta token'dan alinir,
/// parola sorulmaz (hesap Google ile giris yapar; istenirse "Sifremi unuttum" ile sonradan parola belirlenebilir).
/// </summary>
public class GoogleRegisterRequest : AccountProfileRequest
{
    /// <summary>Google'in imzaladigi ID token (JWT).</summary>
    [Required(ErrorMessage = "Google kimlik bilgisi zorunludur.")]
    [StringLength(8192)]
    public string IdToken { get; set; } = string.Empty;
}
