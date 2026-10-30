using MerfitCustomerApi.Business.Dtos.Auth;

namespace MerfitCustomerApi.Business.Interfaces.Services;

/// <summary>
/// Kimlik dogrulama (kayit, giris vb.) is kurallarini yuruten servis sozlesmesi.
/// </summary>
public interface IAuthService
{
    /// <summary>
    /// Yeni bir kullanici hesabi ve fitness profili olusturur, ardindan token cifti doner.
    /// </summary>
    /// <exception cref="Domain.Exceptions.ConflictException">E-posta zaten kayitliysa firlatilir.</exception>
    /// <exception cref="Domain.Exceptions.AppValidationException">Is kurali ihlallerinde firlatilir.</exception>
    Task<AuthResponse> RegisterAsync(RegisterRequest request, string? ipAddress);

    /// <summary>
    /// Var olan bir kullaniciyi e-posta veya kullanici adi ve parola ile dogrular, ardindan token cifti doner.
    /// </summary>
    /// <exception cref="Domain.Exceptions.UnauthorizedException">Bilgiler hatali veya hesap pasifse firlatilir.</exception>
    Task<AuthResponse> LoginAsync(LoginRequest request, string? ipAddress);

    /// <summary>
    /// Kayit ekranindaki anlik kontrol: kullanici adinin bicimi gecerli mi ve bos mu. Hata firlatmaz; sonucu
    /// <see cref="UsernameAvailabilityResponse.Available"/> ve kullaniciya gosterilecek mesajla doner.
    /// </summary>
    Task<UsernameAvailabilityResponse> CheckUsernameAvailabilityAsync(string? username, CancellationToken cancellationToken = default);

    /// <summary>
    /// Google ID token'i ile giris: bagli hesap (veya ayni dogrulanmis e-postayla kayitli hesap) varsa token cifti
    /// doner; yoksa RegistrationRequired=true ve onboarding'i on dolduracak Google profil bilgileri doner.
    /// </summary>
    /// <exception cref="Domain.Exceptions.UnauthorizedException">Token gecersizse veya hesap pasifse.</exception>
    /// <exception cref="Domain.Exceptions.ConflictException">E-posta baska bir hesapta kayitli ve Google e-postayi dogrulanmis saymiyorsa.</exception>
    Task<GoogleSignInResponse> GoogleSignInAsync(GoogleSignInRequest request, string? ipAddress, CancellationToken cancellationToken = default);

    /// <summary>
    /// Onboarding alanlari + Google ID token ile parolasiz yeni hesap olusturur ve token cifti doner. Hesap bu
    /// arada olusmus veya baglanabilir bir hesap varsa yeni hesap acmadan onunla giris yapar (IsNewUser=false).
    /// </summary>
    Task<AuthResponse> GoogleRegisterAsync(GoogleRegisterRequest request, string? ipAddress, CancellationToken cancellationToken = default);

    /// <summary>
    /// Refresh token ile oturumu yeniler: token'i kullanildi isaretler ve yeni bir access/refresh token cifti doner
    /// (token rotasyonu). Yeni refresh token JwtSettings.RefreshTokenExpirationDays gun gecerlidir (kayan sure).
    /// </summary>
    /// <exception cref="Domain.Exceptions.UnauthorizedException">Token bulunamazsa, suresi dolmus/iptal edilmisse, daha once kullanilmissa veya hesap pasifse.</exception>
    Task<AuthResponse> RefreshAsync(RefreshTokenRequest request, string? ipAddress, CancellationToken cancellationToken = default);

    /// <summary>
    /// Cikis: verilen refresh token'i iptal eder. Token bulunamazsa veya zaten gecersizse sessizce gecer.
    /// </summary>
    Task LogoutAsync(RefreshTokenRequest request, string? ipAddress, CancellationToken cancellationToken = default);
}
