namespace MerfitCustomerApi.Business.Common.Pagination;

/// <summary>
/// Tum admin liste (GET) uc noktalarinin ortak sorgu parametrelerini tasir.
/// Entity'ye ozel filtreler (search haric) alt siniflarda ek property olarak eklenir.
/// </summary>
public class PagedRequest
{
    private const int MaxPageSize = 100;
    private const int DefaultPageSize = 20;

    private int _page = 1;
    private int _pageSize = DefaultPageSize;

    /// <summary>1 tabanli sayfa numarasi. 1'den kucuk degerler 1'e sabitlenir.</summary>
    public int Page
    {
        get => _page;
        set => _page = value < 1 ? 1 : value;
    }

    /// <summary>
    /// Sayfa basina kayit sayisi. Guvenlik/performans amaciyla en fazla 100 ile sinirlandirilir
    /// (bkz. madde 39/40 - "pageSize icin maksimum deger belirle").
    /// </summary>
    public int PageSize
    {
        get => _pageSize;
        set => _pageSize = value switch
        {
            < 1 => DefaultPageSize,
            > MaxPageSize => MaxPageSize,
            _ => value,
        };
    }

    /// <summary>Serbest metin arama terimi (entity'ye gore hangi alanlarda aranacagi servis katmaninda belirlenir).</summary>
    public string? Search { get; set; }

    /// <summary>Siralama yapilacak alan adi; servis katmaninda whitelist'e karsi dogrulanir (SQL injection/hatali alan riskine karsi).</summary>
    public string? SortBy { get; set; }

    /// <summary>Siralama yonu: "asc" veya "desc" (varsayilan "asc" olarak yorumlanir).</summary>
    public string? SortDirection { get; set; }

    /// <summary>SortDirection'in "desc" olup olmadigini (case-insensitive) belirtir.</summary>
    public bool IsDescending => string.Equals(SortDirection, "desc", StringComparison.OrdinalIgnoreCase);
}
