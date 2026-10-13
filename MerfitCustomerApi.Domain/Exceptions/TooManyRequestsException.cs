namespace MerfitCustomerApi.Domain.Exceptions;

/// <summary>
/// Kullanici bir islem icin tanimli kullanim sinirini astiginda firlatilir (HTTP 429).
/// </summary>
public class TooManyRequestsException : Exception
{
    public TooManyRequestsException(string message, TimeSpan? retryAfter = null) : base(message)
    {
        RetryAfter = retryAfter;
    }

    /// <summary>Biliniyorsa islemin ne zaman tekrar denenebilecegi; yanitta Retry-After olarak doner.</summary>
    public TimeSpan? RetryAfter { get; }
}
