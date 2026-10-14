using MerfitCustomerApi.Business.Dtos.Customer.FoodAnalysis;

namespace MerfitCustomerApi.Business.Interfaces.Services;

/// <summary>Yemek fotografindan kalori/makro analizi ve kullanici onayindan sonra gunluge kayit.</summary>
public interface IFoodAnalysisService
{
    /// <summary>
    /// Fotografi dogrular, goruntu modeline gonderir, donen JSON'u dogrular ve yemekleri besin katalogu ile
    /// eslestirir. Hicbir ogun kaydi olusturmaz. Fotograf saklanmaz.
    /// </summary>
    /// <exception cref="Domain.Exceptions.AppValidationException">Bos/bozuk/desteklenmeyen dosya ya da fotografta yemek yok.</exception>
    /// <exception cref="Domain.Exceptions.TooManyRequestsException">Gunluk analiz kotasi doldu.</exception>
    /// <exception cref="Domain.Exceptions.ExternalServiceUnavailableException">Saglayici hatasi, gecersiz model yaniti, zaman asimi.</exception>
    Task<FoodAnalysisResultDto> AnalyzeAsync(long userId, FoodImageUpload upload, CancellationToken cancellationToken = default);

    /// <summary>
    /// Kullanicinin onayladigi yemekleri, sunucudaki analiz kaydindaki besin degerleri ve onaylanan gramajla
    /// mevcut ogun kayit mekanizmasi (Meal/MealItem) uzerinden gunluge ekler. Ayni analiz ikinci kez kaydedilmez;
    /// tekrar gelen istek ilk kaydin sonucunu dondurur.
    /// </summary>
    /// <exception cref="Domain.Exceptions.NotFoundException">Analiz yoksa ya da baska kullaniciya aitse.</exception>
    Task<ConfirmFoodAnalysisResponse> ConfirmAsync(long userId, long analysisId, ConfirmFoodAnalysisRequest request, CancellationToken cancellationToken = default);
}

/// <param name="Content">Yuklenen dosyanin baytlari.</param>
/// <param name="ContentType">Istemcinin bildirdigi tur (yalnizca bilgi; gercek tur dosya imzasindan belirlenir).</param>
public sealed record FoodImageUpload(ReadOnlyMemory<byte> Content, string? ContentType);
