namespace MerfitCustomerApi.Domain.Entities.Enums;

/// <summary>
/// MB Shop urun yorumunun yayin durumu; yalnizca sunucu degistirir. Kullanici yorumu olusturdugunda (moderasyondan
/// gecerse) Published olur; admin Hidden yapabilir ve tekrar yayinlayabilir. Deleted son durumdur (kullanici veya admin
/// sildi) ve hicbir yerde gosterilmez. Kullanici basina urun basina yalnizca bir Deleted olmayan yorum bulunabilir.
/// </summary>
public enum ProductReviewStatus
{
    Published,
    Hidden,
    Deleted,
}

/// <summary>Yorum raporlama sebebi.</summary>
public enum ProductReviewReportReason
{
    /// <summary>Spam / reklam.</summary>
    Spam,
    /// <summary>Hakaret veya saldirgan icerik.</summary>
    Offensive,
    /// <summary>Yaniltici bilgi.</summary>
    Misleading,
    /// <summary>Urunle alakasiz icerik.</summary>
    Irrelevant,
    /// <summary>Diger (aciklama zorunlu).</summary>
    Other,
}

/// <summary>Raporun admin tarafindaki durumu.</summary>
public enum ProductReviewReportStatus
{
    /// <summary>Incelenmeyi bekliyor.</summary>
    Open,
    /// <summary>Yorum gizlendi/silindi; rapor hakli bulundu.</summary>
    Resolved,
    /// <summary>Incelendi, yorum yayinda kaldi.</summary>
    Dismissed,
}
