namespace MerfitCustomerApi.Domain.Exceptions;

/// <summary>
/// Kimlik dogrulama basarisiz oldugunda (yanlis e-posta/parola, pasif hesap vb.) firlatilir.
/// </summary>
public class UnauthorizedException : Exception
{
    public UnauthorizedException(string message) : base(message)
    {
    }
}
