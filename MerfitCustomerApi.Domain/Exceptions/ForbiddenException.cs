namespace MerfitCustomerApi.Domain.Exceptions;

/// <summary>
/// Kullanici kimligi dogrulanmis olsa da ilgili islemi yapmaya yetkisi olmadiginda firlatilir;
/// controller/middleware katmaninda 403 Forbidden olarak isaretlenir.
/// </summary>
public class ForbiddenException : Exception
{
    public ForbiddenException(string message) : base(message)
    {
    }
}
