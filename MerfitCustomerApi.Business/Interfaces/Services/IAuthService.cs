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
}