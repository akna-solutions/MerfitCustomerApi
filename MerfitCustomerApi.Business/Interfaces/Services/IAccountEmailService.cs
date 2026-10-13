using MerfitCustomerApi.Business.Dtos.Auth;
using MerfitCustomerApi.Domain.Entities;

namespace MerfitCustomerApi.Business.Interfaces.Services;

/// <summary>
/// E-posta dogrulama ve sifre sifirlama akislari: tek kullanimlik, sureli jetonlarin uretilmesi,
/// e-posta ile gonderilmesi ve dogrulanmasi.
/// </summary>
public interface IAccountEmailService
{
    /// <summary>
    /// Kayittan hemen sonra dogrulama e-postasi gonderir. Kaydi asla bozmaz: gonderilemezse false doner
    /// (kullanici uygulamadan tekrar isteyebilir).
    /// </summary>
    Task<bool> TrySendVerificationAfterRegistrationAsync(ApplicationUser user, string? firstName, string? ipAddress, CancellationToken cancellationToken = default);

    /// <summary>Giris yapmis kullaniciya yeni dogrulama e-postasi gonderir.</summary>
    /// <exception cref="Domain.Exceptions.TooManyRequestsException">Bekleme suresi dolmadiysa (Retry-After ile).</exception>
    /// <exception cref="Domain.Exceptions.EmailDeliveryException">E-posta gonderilemezse (503).</exception>
    Task<ResendVerificationEmailResponse> ResendVerificationEmailAsync(long userId, string? ipAddress, CancellationToken cancellationToken = default);

    /// <summary>Dogrulama jetonunu kullanir ve e-postayi dogrulanmis isaretler.</summary>
    /// <exception cref="Domain.Exceptions.AppValidationException">Jeton gecersiz, suresi dolmus veya kullanilmissa.</exception>
    Task<ConfirmEmailResponse> ConfirmEmailAsync(string token, CancellationToken cancellationToken = default);

    /// <summary>
    /// Hesap varsa sifre sifirlama e-postasi gonderir. Hesap olsun olmasin ayni genel yaniti doner
    /// (hesap kesfi engellenir).
    /// </summary>
    Task<AccountActionResponse> RequestPasswordResetAsync(string email, string? ipAddress, CancellationToken cancellationToken = default);

    /// <summary>Sifre sifirlama jetonunun hala kullanilabilir oldugunu dogrular (ekran acilisinda).</summary>
    Task ValidatePasswordResetTokenAsync(string token, CancellationToken cancellationToken = default);

    /// <summary>Jetonu kullanip yeni sifreyi kaydeder; tum oturumlari (refresh token) iptal eder.</summary>
    Task<AccountActionResponse> ResetPasswordAsync(ResetPasswordRequest request, string? ipAddress, CancellationToken cancellationToken = default);
}
