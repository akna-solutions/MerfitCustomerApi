namespace MerfitCustomerApi.Domain.Exceptions;

/// <summary>
/// Istek, model dogrulamasi (Data Annotations) disinda kalan is kurallarini ihlal ettiginde firlatilir.
/// Alan bazinda hata mesajlarini tasir; controller katmaninda 400 Bad Request'e cevrilir.
/// </summary>
public class AppValidationException : Exception
{
    /// <summary>
    /// Alan adi -> hata mesajlari eslemesi.
    /// </summary>
    public IDictionary<string, string[]> Errors { get; }

    public AppValidationException(string message) : base(message)
    {
        Errors = new Dictionary<string, string[]> { [string.Empty] = new[] { message } };
    }

    public AppValidationException(string field, string message) : base(message)
    {
        Errors = new Dictionary<string, string[]> { [field] = new[] { message } };
    }
}