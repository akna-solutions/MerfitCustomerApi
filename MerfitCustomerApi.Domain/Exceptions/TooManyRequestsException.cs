namespace MerfitCustomerApi.Domain.Exceptions;

/// <summary>
/// Kullanici bir islem icin tanimli kullanim sinirini astiginda firlatilir (HTTP 429).
/// </summary>
public class TooManyRequestsException : Exception
{
    public TooManyRequestsException(string message) : base(message)
    {
    }
}
