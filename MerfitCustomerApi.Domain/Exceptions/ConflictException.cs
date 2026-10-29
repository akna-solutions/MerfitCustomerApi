namespace MerfitCustomerApi.Domain.Exceptions;

/// <summary>
/// Istenen islem mevcut bir kaynakla cakistiginda (orn. e-posta zaten kayitli) firlatilir.
/// </summary>
public class ConflictException : Exception
{
    public ConflictException(string message) : base(message)
    {
    }

    /// <summary>
    /// Istemcinin ayirt edebilmesi icin makine-okunur kod (orn. "PRICE_CHANGED"); yanittaki Errors listesine yazilir.
    /// </summary>
    public ConflictException(string message, string code) : base(message)
    {
        Code = code;
    }

    public string? Code { get; }
}