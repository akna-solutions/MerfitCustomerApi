namespace MerfitCustomerApi.Business.Dtos.Customer.Store;

/// <summary>
/// Urun detayindaki iki oneri satiri. Iki liste birbirini tekrar etmez ve baglam urununu icermez.
/// </summary>
public class StoreProductRecommendationsDto
{
    public long ProductId { get; set; }

    /// <summary>Icerik benzerligi (kategori, marka, fiyat, ad) ve birlikte satin alinma ile "Benzer Urunler".</summary>
    public List<StoreProductListItemDto> Similar { get; set; } = new();

    /// <summary>"Senin Icin Onerilenler": birlikte alinanlar, kullanicinin ilgi alanlari ve populerlik.</summary>
    public List<StoreProductListItemDto> Recommended { get; set; } = new();

    /// <summary>
    /// false ise kullanici gizlilik ayarlarinda kisisellestirilmis onerileri kapatmistir; Recommended yalnizca
    /// urunun kendisine ve genel populerlige dayanir.
    /// </summary>
    public bool Personalized { get; set; }
}
