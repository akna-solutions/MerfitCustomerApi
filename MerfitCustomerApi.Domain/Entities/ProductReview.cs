using MerfitCustomerApi.Domain.Common;
using MerfitCustomerApi.Domain.Entities.Enums;

namespace MerfitCustomerApi.Domain.Entities;

/// <summary>
/// MB Shop urun yorumu (store.ProductReview). Kullanici kimligi her zaman JWT'den gelir. Yayin durumu, onayli alisveris
/// etiketi ve faydali oy sayaclari yalnizca sunucuda belirlenir; istemciden gelen deger kabul edilmez.
/// (ProductId, UserId) icin Deleted olmayan tek kayit olabilir (filtreli unique index).
/// </summary>
public class ProductReview : BaseEntity
{
    public long ProductId { get; set; }

    public long UserId { get; set; }

    /// <summary>1-5 yildiz.</summary>
    public int Rating { get; set; }

    public string? Title { get; set; }

    public string Comment { get; set; } = string.Empty;

    /// <summary>
    /// Kullanicinin bu urunu iceren, teslim edilmis ve iptal/iade edilmemis bir siparisi var mi. Yorum yazilirken ve
    /// ilgili siparisin durumu admin tarafinda degistiginde sunucuda yeniden hesaplanir.
    /// </summary>
    public bool IsVerifiedPurchase { get; set; }

    public ProductReviewStatus Status { get; set; } = ProductReviewStatus.Published;

    /// <summary>"Faydali" oy sayisi (ProductReviewVote'tan turetilir, siralama icin saklanir).</summary>
    public int HelpfulCount { get; set; }

    /// <summary>"Faydali degil" oy sayisi.</summary>
    public int NotHelpfulCount { get; set; }

    /// <summary>Kullanici yorumu (metin/puan) en son ne zaman duzenledi; admin islemleri bunu degistirmez.</summary>
    public DateTime? EditedAt { get; set; }

    public DateTime? DeletedAt { get; set; }

    /// <summary>Admin gizleme/silme notu (kullaniciya gosterilmez).</summary>
    public string? ModerationNote { get; set; }

    public DateTime? ModeratedAt { get; set; }

    public long? ModeratedByUserId { get; set; }
}
