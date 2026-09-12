namespace MerfitCustomerApi.Business.Common.Responses;

/// <summary>
/// Tum Admin API uc noktalarinin dondurdugu ortak, tutarli response zarfi (envelope).
/// Veri tasimayan (orn. 204 yerine 200 donen action) uc noktalar icin kullanilir.
/// </summary>
public class ApiResponse
{
    /// <summary>Islemin basarili olup olmadigi.</summary>
    public bool IsSuccess { get; set; }

    /// <summary>Basarisiz istekte kullaniciya gosterilecek ozet hata mesaji.</summary>
    public string? ErrorMessage { get; set; }

    /// <summary>Alan bazinda veya coklu hata mesajlari (orn. validasyon hatalari).</summary>
    public List<string> Errors { get; set; } = new();

    /// <summary>Loglama/destek taleplerinde istegi izlemek icin benzersiz kimlik.</summary>
    public string TraceId { get; set; } = Guid.NewGuid().ToString("N");

    public static ApiResponse Success() => new() { IsSuccess = true };

    public static ApiResponse Fail(string errorMessage, IEnumerable<string>? errors = null) => new()
    {
        IsSuccess = false,
        ErrorMessage = errorMessage,
        Errors = errors?.ToList() ?? new List<string>(),
    };
}

/// <summary>
/// Veri (data) tasiyan Admin API response zarfi. Tum admin GET/POST/PUT/PATCH uc noktalari
/// bu tipi (veya <see cref="PagedResult{T}"/> ile parametrelendirilmis halini) doner.
/// </summary>
/// <typeparam name="T">Donen veri turu (DTO veya PagedResult&lt;Dto&gt;).</typeparam>
public class ApiResponse<T> : ApiResponse
{
    /// <summary>Islem basariliysa donen veri; basarisizsa null.</summary>
    public T? Data { get; set; }

    public static ApiResponse<T> Success(T data) => new()
    {
        IsSuccess = true,
        Data = data,
    };

    public static new ApiResponse<T> Fail(string errorMessage, IEnumerable<string>? errors = null) => new()
    {
        IsSuccess = false,
        ErrorMessage = errorMessage,
        Errors = errors?.ToList() ?? new List<string>(),
    };
}
