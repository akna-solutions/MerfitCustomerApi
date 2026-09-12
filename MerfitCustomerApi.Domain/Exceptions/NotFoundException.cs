namespace MerfitCustomerApi.Domain.Exceptions;

/// <summary>
/// Istenen kaynak (kayit) bulunamadiginda firlatilir; controller/middleware katmaninda
/// 404 Not Found olarak isaretlenir.
/// </summary>
public class NotFoundException : Exception
{
    public NotFoundException(string message) : base(message)
    {
    }

    /// <summary>
    /// "{entityName} bulunamadi (Id: {id})" formatinda standart bir mesaj uretir.
    /// </summary>
    public NotFoundException(string entityName, object id)
        : base($"{entityName} bulunamadi (Id: {id}).")
    {
    }
}
