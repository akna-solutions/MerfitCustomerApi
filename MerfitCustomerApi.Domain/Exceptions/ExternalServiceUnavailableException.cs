namespace MerfitCustomerApi.Domain.Exceptions;

/// <summary>
/// Bagimli olunan dis servise (orn. Open Food Facts) ulasilamadiginda firlatilir; istemciye 503 doner.
/// </summary>
public class ExternalServiceUnavailableException : Exception
{
    public ExternalServiceUnavailableException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
