using Google.Apis.Auth;
using MerfitCustomerApi.Business.Common;
using MerfitCustomerApi.Business.Interfaces.Services;
using MerfitCustomerApi.Domain.Exceptions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MerfitCustomerApi.Business.Services.Auth;

/// <summary>
/// Google ID token'ini Google.Apis.Auth ile dogrular: imza Google'in yayinladigi acik anahtarlarla (onbellekli)
/// kontrol edilir; iss (accounts.google.com), aud (GoogleAuth:ClientIds) ve exp alanlari zorunludur.
/// </summary>
public class GoogleIdTokenValidator : IGoogleIdTokenValidator
{
    private const string NotConfiguredMessage = "Google ile giriş şu anda kullanılamıyor.";
    private const string InvalidTokenMessage = "Google ile giriş doğrulanamadı. Lütfen tekrar dene.";

    private readonly GoogleAuthOptions _options;
    private readonly ILogger<GoogleIdTokenValidator> _logger;

    public GoogleIdTokenValidator(IOptions<GoogleAuthOptions> options, ILogger<GoogleIdTokenValidator> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    private IReadOnlyList<string> ClientIds =>
        _options.ClientIds.Where(id => !string.IsNullOrWhiteSpace(id)).Select(id => id.Trim()).ToList();

    public bool IsConfigured => ClientIds.Count > 0;

    public async Task<GoogleIdentity> ValidateAsync(string idToken, CancellationToken cancellationToken = default)
    {
        if (!IsConfigured)
        {
            _logger.LogWarning("GoogleAuth:ClientIds tanimli degil; Google ile giris istegi reddedildi.");
            throw new ExternalServiceUnavailableException(NotConfiguredMessage);
        }

        GoogleJsonWebSignature.Payload payload;
        try
        {
            payload = await GoogleJsonWebSignature.ValidateAsync(idToken, new GoogleJsonWebSignature.ValidationSettings
            {
                Audience = ClientIds,
                IssuedAtClockTolerance = _options.ClockSkew,
                ExpirationTimeClockTolerance = _options.ClockSkew,
            });
        }
        catch (InvalidJwtException ex)
        {
            // Token'in kendisi loglanmaz.
            _logger.LogInformation("Gecersiz Google ID token: {Reason}", ex.Message);
            throw new UnauthorizedException(InvalidTokenMessage);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Google acik anahtarlari alinamadi.");
            throw new ExternalServiceUnavailableException(NotConfiguredMessage, ex);
        }

        if (string.IsNullOrWhiteSpace(payload.Subject) || string.IsNullOrWhiteSpace(payload.Email))
        {
            // "email" kapsami istenmeden alinan token'larda e-posta bulunmaz.
            throw new UnauthorizedException(InvalidTokenMessage);
        }

        return new GoogleIdentity(payload.Subject, payload.Email.Trim(), payload.EmailVerified, payload.GivenName, payload.FamilyName);
    }
}
