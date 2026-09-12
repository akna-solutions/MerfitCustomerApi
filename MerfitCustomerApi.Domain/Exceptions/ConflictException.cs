namespace MerfitCustomerApi.Domain.Exceptions;

/// <summary>
/// Istenen islem mevcut bir kaynakla cakistiginda (orn. e-posta zaten kayitli) firlatilir.
/// </summary>
public class ConflictException : Exception
{
    public ConflictException(string message) : base(message)
    {
    }
}