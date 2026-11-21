using MerfitCustomerApi.Business.Common.Pagination;

namespace MerfitCustomerApi.Business.Dtos.Customer.Store;

/// <summary>
/// Urun degerlendirme ozeti. Yalnizca yayindaki (Published) yorumlardan hesaplanir.
/// </summary>
public class StoreReviewSummaryDto
{
    public long ProductId { get; set; }

    /// <summary>5 uzerinden ortalama, bir ondalik basamaga yuvarlanmis; yorum yoksa 0.</summary>
    public decimal AverageRating { get; set; }

    public int ReviewCount { get; set; }

    /// <summary>Yildiz -> yorum sayisi; 5'ten 1'e her yildiz her zaman bulunur.</summary>
    public List<StoreReviewDistributionItem> Distribution { get; set; } = new();

    public int WithPhotosCount { get; set; }

    public int VerifiedPurchaseCount { get; set; }

    /// <summary>Kullanicinin bu urune ait aktif (silinmemis) yorumu; yoksa null.</summary>
    public long? MyReviewId { get; set; }

    /// <summary>Kullanici bu urune satin alma onayli yorum yazarsa rozet alir mi (bilgilendirme amacli).</summary>
    public bool HasVerifiedPurchase { get; set; }
}

public class StoreReviewDistributionItem
{
    public int Rating { get; set; }
    public int Count { get; set; }

    /// <summary>Toplam yorum icindeki yuzde (0-100, tam sayi).</summary>
    public int Percentage { get; set; }
}

public class StoreReviewAuthorDto
{
    /// <summary>Gosterilen ad (orn. "Ayse K."); e-posta hicbir zaman donmez.</summary>
    public string DisplayName { get; set; } = string.Empty;

    public string? AvatarUrl { get; set; }
}

public class StoreReviewImageDto
{
    public long Id { get; set; }

    /// <summary>Sunucuya gore goreli yol (orn. "/api/store/review-images/9f..."); istemci API adresiyle birlestirir.</summary>
    public string Url { get; set; } = string.Empty;

    public int Width { get; set; }
    public int Height { get; set; }
}

public class StoreReviewDto
{
    public long Id { get; set; }
    public long ProductId { get; set; }
    public int Rating { get; set; }
    public string? Title { get; set; }
    public string Comment { get; set; } = string.Empty;
    public bool IsVerifiedPurchase { get; set; }

    /// <summary>"published" | "hidden" (yalnizca yorumun sahibine gizli yorum donebilir).</summary>
    public string Status { get; set; } = "published";

    public DateTime CreatedAt { get; set; }
    public DateTime? EditedAt { get; set; }
    public StoreReviewAuthorDto Author { get; set; } = new();
    public List<StoreReviewImageDto> Images { get; set; } = new();
    public int HelpfulCount { get; set; }
    public int NotHelpfulCount { get; set; }

    /// <summary>Kullanicinin oyu: true faydali, false faydali degil, null oy yok.</summary>
    public bool? MyVote { get; set; }

    public bool IsMine { get; set; }
    public bool ReportedByMe { get; set; }
}

/// <summary>
/// GET /api/store/products/{id}/reviews. Sort: newest (varsayilan) | oldest | rating_desc | rating_asc | helpful.
/// Rating verilirse yalnizca o yildiz; WithPhotos yalnizca fotografli; VerifiedOnly yalnizca onayli alisveris.
/// </summary>
public class StoreReviewQuery : PagedRequest
{
    public string? Sort { get; set; }
    public int? Rating { get; set; }
    public bool WithPhotos { get; set; }
    public bool VerifiedOnly { get; set; }
}

/// <summary>Yorum olusturma/duzenleme. UserId, onay ve yayin durumu istemciden alinmaz.</summary>
public class UpsertStoreReviewRequest
{
    public int? Rating { get; set; }
    public string? Title { get; set; }
    public string? Comment { get; set; }
}

public class VoteStoreReviewRequest
{
    public bool? IsHelpful { get; set; }
}

public class ReportStoreReviewRequest
{
    /// <summary>spam | offensive | misleading | irrelevant | other.</summary>
    public string? Reason { get; set; }

    /// <summary>"other" icin zorunlu.</summary>
    public string? Description { get; set; }
}

public class ReportStoreReviewResponse
{
    /// <summary>Kullanici bu yorumu daha once raporlamissa true (yeni kayit olusmaz).</summary>
    public bool AlreadyReported { get; set; }
}

public class StoreReviewVoteResponse
{
    public long ReviewId { get; set; }
    public int HelpfulCount { get; set; }
    public int NotHelpfulCount { get; set; }
    public bool? MyVote { get; set; }
}

/// <summary>Sunucuya yuklenen, dogrulanmis gorsel (controller -> servis).</summary>
public sealed record StoreReviewImageUpload(byte[] Content, string? DeclaredContentType);

/// <summary>GET /api/store/review-images/{key} yaniti.</summary>
public sealed record StoreReviewImageFile(byte[] Content, string ContentType);
