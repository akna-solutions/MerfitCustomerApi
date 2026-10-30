using MerfitCustomerApi.Business.Services.ContentModeration;

namespace MerfitCustomerApi.Business.Interfaces.Services;

/// <summary>
/// Kullanicinin yazdigi ve baska kullanicilara gorunebilen metinleri (ad, soyad, kullanici adi, profil aciklamasi)
/// merkezi sozlukle (<see cref="ModerationLexicon"/>) kufur, hakaret, cinsel icerik, tehdit, ayrimcilik ve sistem
/// hesabi taklidi icin kontrol eder. Tamamen yerel ve deterministiktir: yapay zeka veya harici servis kullanmaz.
/// </summary>
public interface IContentModerationService
{
    /// <summary>
    /// Metni alan turune gore kontrol eder. Null/bos metin her zaman izinlidir; zorunluluk kontrolu DTO
    /// validasyonunun isidir.
    /// </summary>
    ModerationResult Check(string? content, ContentModerationType type);
}
