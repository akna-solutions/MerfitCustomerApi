namespace MerfitCustomerApi.Business.Interfaces.Services;

/// <summary>
/// Gonderilecek tek bir e-posta. HTML govdesi cagiran tarafta guvenli (encode edilmis) olarak uretilir.
/// </summary>
public sealed record OutgoingEmail(string To, string Subject, string HtmlBody, string TextBody);

/// <summary>
/// E-posta saglayicisi soyutlamasi. Saglayiciya (bugun Resend) ait tum ayrinti yalnizca bu arayuzun
/// implementasyonunda bulunur; baska bir saglayiciya gecis tek bir sinifin degismesiyle yapilir.
/// </summary>
public interface IEmailSender
{
    /// <summary>Saglayici anahtari tanimli mi. False iken <see cref="SendAsync"/> yapilandirma hatasi firlatir.</summary>
    bool IsConfigured { get; }

    /// <summary>
    /// E-postayi gonderir. Gecici hatalarda ayni <paramref name="idempotencyKey"/> ile yeniden dener; boylece
    /// ayni e-posta iki kez gitmez. Gonderilemezse <see cref="Domain.Exceptions.EmailDeliveryException"/> firlatir.
    /// </summary>
    Task SendAsync(OutgoingEmail email, string idempotencyKey, CancellationToken cancellationToken = default);
}
