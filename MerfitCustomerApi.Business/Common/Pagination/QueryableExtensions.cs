using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;

namespace MerfitCustomerApi.Business.Common.Pagination;

/// <summary>
/// Admin liste uc noktalarinda kullanilan, database tarafinda calisan (IQueryable uzerinden)
/// dinamik siralama ve sayfalama yardimcilarini icerir. Tum veriyi memory'e cekmeden
/// Skip/Take ve OrderBy uygular (bkz. madde 5/40 - performans kurallari).
/// </summary>
public static class QueryableExtensions
{
    /// <summary>
    /// Verilen "sortBy" degerini, cagiran servisin tanimladigi guvenli (whitelist) alan
    /// esleme sozlugune karsi dogrulayarak siralama uygular. Sozlukte bulunmayan bir "sortBy"
    /// gelirse (SQL injection/gecersiz alan riskine karsi) <paramref name="defaultKey"/> kullanilir.
    /// </summary>
    /// <typeparam name="T">Sorgulanan entity/projeksiyon turu.</typeparam>
    /// <param name="source">Siralanacak sorgu.</param>
    /// <param name="sortBy">Istemciden gelen alan adi (orn. "createdAt").</param>
    /// <param name="descending">true ise azalan, false ise artan siralama uygulanir.</param>
    /// <param name="allowedSorts">"sortBy" degeri -> siralama ifadesi eslemesi (whitelist).</param>
    /// <param name="defaultKey">allowedSorts icinde eslesme bulunamazsa kullanilacak anahtar.</param>
    public static IOrderedQueryable<T> ApplySort<T>(
        this IQueryable<T> source,
        string? sortBy,
        bool descending,
        IReadOnlyDictionary<string, Expression<Func<T, object?>>> allowedSorts,
        string defaultKey)
    {
        var key = !string.IsNullOrWhiteSpace(sortBy) && allowedSorts.ContainsKey(sortBy!.Trim())
            ? sortBy!.Trim()
            : defaultKey;

        var selector = allowedSorts[key];

        return descending
            ? source.OrderByDescending(selector)
            : source.OrderBy(selector);
    }

    /// <summary>
    /// Sorguyu verilen sayfa/sayfa boyutuna gore Skip/Take ile sinirlar, toplam kayit sayisini
    /// ve sayfa verisini asenkron olarak getirir ve <see cref="PagedResult{T}"/> olarak sarar.
    /// </summary>
    public static async Task<PagedResult<T>> ToPagedResultAsync<T>(
        this IQueryable<T> source,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var totalCount = await source.LongCountAsync(cancellationToken);

        var items = await source
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return PagedResult<T>.Create(items, page, pageSize, totalCount);
    }
}
