using MerfitCustomerApi.Business.Dtos.Auth;
using MerfitCustomerApi.Domain.Entities;

namespace MerfitCustomerApi.Business.Interfaces.Services;

/// <summary>
/// E-posta dogrulama ve sifre sifirlama akislari: 6 haneli tek kullanimlik, sureli kodlarin uretilmesi,
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

    /// <summary>Giris yapmis kullanicinin dogrulama kodunu kontrol eder ve e-postayi dogrulanmis isaretler.</summary>
    /// <exception cref="Domain.Exceptions.AppValidationException">Kod hatali, suresi dolmus, kullanilmis veya deneme siniri asilmissa.</exception>
    Task<ConfirmEmailResponse> ConfirmEmailAsync(long userId, string code, CancellationToken cancellationToken = default);

    /// <summary>
    /// Hesap varsa sifre sifirlama e-postasi gonderir. Hesap olsun olmasin ayni genel yaniti doner
    /// (hesap kesfi engellenir).
    /// </summary>
    Task<AccountActionResponse> RequestPasswordResetAsync(string email, string? ipAddress, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sifre sifirlama kodunu kullanmadan kontrol eder (yeni sifre ekranina gecmeden once). Bir deneme hakki harcar.
    /// </summary>
    Task VerifyPasswordResetCodeAsync(string email, string code, CancellationToken cancellationToken = default);

    /// <summary>Kodu kullanip yeni sifreyi kaydeder; tum oturumlari (refresh token) iptal eder.</summary>
    Task<AccountActionResponse> ResetPasswordAsync(ResetPasswordRequest request, string? ipAddress, CancellationToken cancellationToken = default);
}
