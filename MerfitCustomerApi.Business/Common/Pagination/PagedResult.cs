namespace MerfitCustomerApi.Business.Common.Pagination;

/// <summary>
/// Sayfalanmis liste sonuclari icin ortak zarf. <see cref="Common.Responses.ApiResponse{T}"/> icinde
/// "Data" alani olarak kullanilir; boylece tum admin liste uc noktalari ayni sekli doner:
/// { isSuccess, data: { items, page, pageSize, totalCount, totalPages, hasNextPage, hasPreviousPage } }.
/// </summary>
/// <typeparam name="T">Sayfadaki oge (liste DTO'su) turu.</typeparam>
public class PagedResult<T>
{
    /// <summary>Mevcut sayfadaki ogeler.</summary>
    public IReadOnlyList<T> Items { get; set; } = Array.Empty<T>();

    /// <summary>1 tabanli mevcut sayfa numarasi.</summary>
    public int Page { get; set; }

    /// <summary>Sayfa basina oge sayisi.</summary>
    public int PageSize { get; set; }

    /// <summary>Filtrelere uyan toplam kayit sayisi (sayfalama oncesi).</summary>
    public long TotalCount { get; set; }

    /// <summary>Toplam sayfa sayisi.</summary>
    public int TotalPages => PageSize <= 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);

    /// <summary>Bir sonraki sayfanin olup olmadigi.</summary>
    public bool HasNextPage => Page < TotalPages;

    /// <summary>Bir onceki sayfanin olup olmadigi.</summary>
    public bool HasPreviousPage => Page > 1;

    public static PagedResult<T> Create(IReadOnlyList<T> items, int page, int pageSize, long totalCount) => new()
    {
        Items = items,
        Page = page,
        PageSize = pageSize,
        TotalCount = totalCount,
    };
}
