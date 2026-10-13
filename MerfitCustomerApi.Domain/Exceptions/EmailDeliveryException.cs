namespace MerfitCustomerApi.Domain.Exceptions;

/// <summary>
/// E-posta gonderilemediginde firlatilir; istemciye 503 doner. <see cref="IsConfigurationError"/> true ise
/// sorun gecici degildir (API anahtari yok/gecersiz, gonderici domain'i dogrulanmamis) ve yeniden denemek
/// yararsizdir; yonetici yapilandirmayi duzeltmelidir.
/// </summary>
public class EmailDeliveryException : ExternalServiceUnavailableException
{
    public EmailDeliveryException(string message, bool isConfigurationError, Exception? innerException = null, TimeSpan? retryAfter = null)
        : base(message, innerException, retryAfter)
    {
        IsConfigurationError = isConfigurationError;
    }

    public bool IsConfigurationError { get; }
}
