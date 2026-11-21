using MerfitCustomerApi.Domain.Common;

namespace MerfitCustomerApi.Domain.Entities;

/// <summary>
/// Yorum fotografi (store.ProductReviewImage). MBFit'te harici bir dosya deposu olmadigi icin dogrulanmis ve meta
/// verisi (EXIF/GPS) temizlenmis gorsel veritabaninda saklanir; StorageKey rastgele uretilir, kullanicinin dosya adi
/// hic kullanilmaz. Gorsel GET /api/store/review-images/{StorageKey} ile sunulur. Listeleme sorgulari Content
/// sutununu okumaz.
/// </summary>
public class ProductReviewImage : BaseEntity
{
    public long ReviewId { get; set; }

    /// <summary>32 karakterlik rastgele hex anahtar (URL'de kullanilir).</summary>
    public string StorageKey { get; set; } = string.Empty;

    /// <summary>image/jpeg, image/png veya image/webp (icerikten tespit edilir).</summary>
    public string ContentType { get; set; } = string.Empty;

    public int ByteSize { get; set; }

    public int Width { get; set; }

    public int Height { get; set; }

    public int SortOrder { get; set; }

    public byte[] Content { get; set; } = Array.Empty<byte>();
}
