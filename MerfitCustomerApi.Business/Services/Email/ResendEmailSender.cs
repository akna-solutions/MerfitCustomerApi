using MerfitCustomerApi.Business.Common;
using MerfitCustomerApi.Business.Interfaces.Services;
using MerfitCustomerApi.Domain.Exceptions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Resend;

namespace MerfitCustomerApi.Business.Services.Email;

/// <summary>
/// Resend'in resmi .NET SDK'si ile e-posta gonderen <see cref="IEmailSender"/>. Uygulamada Resend'e bagli
/// tek siniftir. Loglara alici adresi, konu, govde veya API anahtari yazilmaz; yalnizca hata turu ve durum kodu.
/// </summary>
public sealed class ResendEmailSender : IEmailSender
{
    public const string NotConfiguredMessage = "E-posta servisi henüz yapılandırılmadı. Lütfen daha sonra tekrar dene.";
    public const string UnavailableMessage = "E-posta şu anda gönderilemedi. Lütfen birkaç dakika sonra tekrar dene.";

    // Yeniden denemenin ise yaramayacagi, yonetici mudahalesi gerektiren hata turleri.
    private static readonly HashSet<ErrorType> ConfigurationErrors = new()
    {
        ErrorType.MissingApiKey,
        ErrorType.InvalidApiKey,
        ErrorType.RestrictedApiKey,
        ErrorType.InvalidFromAddress,
        ErrorType.InvalidAccess,
        ErrorType.SecurityError,
    };

    private readonly IServiceProvider _services;
    private readonly ResendEmailOptions _options;
    private readonly ILogger<ResendEmailSender> _logger;

    /// <param name="services">
    /// IResend yalnizca gonderim aninda cozulur; anahtar tanimli degilken Resend istemcisi hic olusturulmaz.
    /// </param>
    public ResendEmailSender(IServiceProvider services, IOptions<ResendEmailOptions> options, ILogger<ResendEmailSender> logger)
    {
        _services = services;
        _options = options.Value;
        _logger = logger;
    }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_options.ApiKey);

    public async Task SendAsync(OutgoingEmail email, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        if (!IsConfigured)
        {
            _logger.LogError("E-posta gonderilemedi: Resend:ApiKey tanimli degil (User Secrets veya Resend__ApiKey ortam degiskeni).");
            throw new EmailDeliveryException(NotConfiguredMessage, isConfigurationError: true);
        }

        var message = new EmailMessage
        {
            From = _options.From,
            To = email.To,
            Subject = email.Subject,
            HtmlBody = email.HtmlBody,
            TextBody = email.TextBody,
        };
        if (!string.IsNullOrWhiteSpace(_options.ReplyTo))
        {
            message.ReplyTo = _options.ReplyTo;
        }

        var resend = (IResend)_services.GetService(typeof(IResend))!;
        var maxAttempts = Math.Max(1, _options.MaxSendAttempts);

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await resend.EmailSendAsync(idempotencyKey, message, cancellationToken);
                return;
            }
            catch (ResendException ex) when (!ConfigurationErrors.Contains(ex.ErrorType) && IsTransient(ex) && attempt < maxAttempts)
            {
                _logger.LogWarning("Resend gecici hata ({ErrorType}, {StatusCode}); deneme {Attempt}/{MaxAttempts}.",
                    ex.ErrorType, (int?)ex.StatusCode, attempt, maxAttempts);
            }
            catch (ResendException ex)
            {
                var isConfigurationError = ConfigurationErrors.Contains(ex.ErrorType)
                    || (ex.ErrorType == ErrorType.ValidationError && ex.StatusCode == System.Net.HttpStatusCode.Forbidden);
                // 403 validation_error: gonderici domain'i Resend'de dogrulanmamis (DNS) ya da test modunda
                // yalnizca hesap sahibine gonderim yapilabiliyor.
                _logger.LogError("Resend e-posta gonderimi basarisiz ({ErrorType}, {StatusCode}, yapilandirma hatasi: {IsConfigurationError}).",
                    ex.ErrorType, (int?)ex.StatusCode, isConfigurationError);
                var retryAfter = ex.Limits?.RetryAfter is int seconds && seconds > 0 ? TimeSpan.FromSeconds(seconds) : (TimeSpan?)null;
                throw new EmailDeliveryException(isConfigurationError ? NotConfiguredMessage : UnavailableMessage, isConfigurationError, ex, retryAfter);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
            {
                if (attempt < maxAttempts)
                {
                    _logger.LogWarning("Resend'e ulasilamadi ({ExceptionType}); deneme {Attempt}/{MaxAttempts}.", ex.GetType().Name, attempt, maxAttempts);
                }
                else
                {
                    _logger.LogError("Resend'e ulasilamadi ({ExceptionType}); e-posta gonderilemedi.", ex.GetType().Name);
                    throw new EmailDeliveryException(UnavailableMessage, isConfigurationError: false, ex);
                }
            }

            await Task.Delay(_options.RetryDelay, cancellationToken);
        }
    }

    // Ucretsiz planin gunluk/aylik kotasi dolduysa yeniden denemek ise yaramaz (kota sifirlanana kadar).
    private static bool IsTransient(ResendException ex) =>
        ex.ErrorType is not (ErrorType.DailyQuotaExceeded or ErrorType.MonthlyQuotaExceeded)
        && (ex.IsTransient
        || ex.ErrorType is ErrorType.RateLimitExceeded or ErrorType.InternalServerError or ErrorType.HttpSendFailed or ErrorType.MissingResponse
        || (int?)ex.StatusCode >= 500);
}
