using MerfitCustomerApi.Business.Common.Pagination;
using MerfitCustomerApi.Business.Dtos.Customer.Store;

namespace MerfitCustomerApi.Business.Interfaces.Services;

/// <summary>
/// MB Shop urun yorumlari. Tum yazma islemleri JWT'deki kullaniciya aittir; yayin durumu, onayli alisveris etiketi ve
/// oy sayaclari sunucuda belirlenir. Ozet ve liste yalnizca yayindaki yorumlari icerir.
/// </summary>
public interface IStoreReviewService
{
    Task<StoreReviewSummaryDto> GetSummaryAsync(long userId, long productId, CancellationToken cancellationToken = default);

    Task<PagedResult<StoreReviewDto>> GetListAsync(long userId, long productId, StoreReviewQuery query, CancellationToken cancellationToken = default);

    /// <summary>Kullanicinin bu urune ait aktif yorumu (gizlenmis olsa da); yoksa null.</summary>
    Task<StoreReviewDto?> GetMineAsync(long userId, long productId, CancellationToken cancellationToken = default);

    /// <summary>Kullanici urun basina tek aktif yorum yazabilir; ikincisi 409 (REVIEW_EXISTS).</summary>
    Task<StoreReviewDto> CreateAsync(long userId, long productId, UpsertStoreReviewRequest request, CancellationToken cancellationToken = default);

    /// <summary>Yalnizca yorumun sahibi; baskasinin yorumu 403.</summary>
    Task<StoreReviewDto> UpdateAsync(long userId, long reviewId, UpsertStoreReviewRequest request, CancellationToken cancellationToken = default);

    /// <summary>Yalnizca yorumun sahibi; idempotent (zaten silinmisse bir sey yapmaz).</summary>
    Task DeleteAsync(long userId, long reviewId, CancellationToken cancellationToken = default);

    Task<StoreReviewImageDto> AddImageAsync(long userId, long reviewId, StoreReviewImageUpload upload, CancellationToken cancellationToken = default);

    Task RemoveImageAsync(long userId, long reviewId, long imageId, CancellationToken cancellationToken = default);

    /// <summary>Oy verir veya degistirir (idempotent). Kendi yorumuna oy verilemez (403).</summary>
    Task<StoreReviewVoteResponse> VoteAsync(long userId, long reviewId, VoteStoreReviewRequest request, CancellationToken cancellationToken = default);

    /// <summary>Oyu kaldirir (idempotent).</summary>
    Task<StoreReviewVoteResponse> RemoveVoteAsync(long userId, long reviewId, CancellationToken cancellationToken = default);

    /// <summary>Raporlar; ayni kullanicinin tekrar raporu yeni kayit olusturmaz (AlreadyReported = true).</summary>
    Task<ReportStoreReviewResponse> ReportAsync(long userId, long reviewId, ReportStoreReviewRequest request, CancellationToken cancellationToken = default);

    /// <summary>Yayindaki bir yorumun fotografi; yoksa null.</summary>
    Task<StoreReviewImageFile?> GetImageAsync(string storageKey, CancellationToken cancellationToken = default);
}
