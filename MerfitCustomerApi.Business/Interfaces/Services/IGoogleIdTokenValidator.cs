namespace MerfitCustomerApi.Business.Interfaces.Services;

/// <summary>
/// Mobil uygulamadan gelen Google ID token'ini dogrular (imza, iss, aud, sure).
/// </summary>
public interface IGoogleIdTokenValidator
{
    /// <summary>Google istemci kimlikleri yapilandirilmis mi (bkz. GoogleAuthOptions.ClientIds).</summary>
    bool IsConfigured { get; }

    /// <summary>
    /// Token gecerliyse icindeki kimligi doner.
    /// </summary>
    /// <exception cref="Domain.Exceptions.UnauthorizedException">Token gecersiz, suresi dolmus veya baska bir uygulamaya aitse.</exception>
    /// <exception cref="Domain.Exceptions.ExternalServiceUnavailableException">Google yapilandirilmamissa veya Google'in anahtarlarina ulasilamiyorsa.</exception>
    Task<GoogleIdentity> ValidateAsync(string idToken, CancellationToken cancellationToken = default);
}

/// <summary>Dogrulanmis Google ID token'indan okunan kimlik.</summary>
/// <param name="Subject">Google'in degismeyen kullanici kimligi ("sub").</param>
/// <param name="Email">Google hesabinin e-posta adresi.</param>
/// <param name="EmailVerified">Google'a gore e-posta adresi dogrulanmis mi ("email_verified").</param>
public sealed record GoogleIdentity(string Subject, string Email, bool EmailVerified, string? GivenName, string? FamilyName);
