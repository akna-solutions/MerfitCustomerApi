namespace MerfitCustomerApi.Domain.Exceptions;

/// <summary>
/// Bagimli olunan dis servise (orn. Open Food Facts) ulasilamadiginda firlatilir; istemciye 503 doner.
/// </summary>
public class ExternalServiceUnavailableException : Exception
{
    public ExternalServiceUnavailableException(string message, Exception? innerException = null, TimeSpan? retryAfter = null)
        : base(message, innerException)
    {
        RetryAfter = retryAfter;
    }

    /// <summary>Servisin ne zaman yeniden denenebilecegi biliniyorsa (orn. saglayici kotasi); yanitta Retry-After olarak doner.</summary>
    public TimeSpan? RetryAfter { get; }
}
