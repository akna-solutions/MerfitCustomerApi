namespace MerfitCustomerApi.Business.Interfaces.Services;

/// <summary>
/// Yemek fotografini analiz eden goruntu modeli saglayicisi (ilk uygulama: Gemini). Saglayici yalnizca
/// fotografi ve sabit talimatlari gonderir, sabit JSON semasina uyan ham metni dondurur ve hatayi
/// <see cref="NutritionAiProviderException"/> ile siniflandirir. Yanitin dogrulanmasi, katalog eslestirmesi,
/// kota ve kayit FoodAnalysisService'tedir; baska bir modele gecmek yalnizca yeni bir uygulama eklemektir.
/// </summary>
public interface IFoodImageAnalysisProvider
{
    /// <summary>Yapilandirmadaki adi (orn. Gemini).</summary>
    string Name { get; }

    /// <summary>Anahtar ve model ayarli mi?</summary>
    bool IsConfigured { get; }

    string Model { get; }

    /// <exception cref="NutritionAiProviderException">Saglayici hatasi, kota veya zaman asimi.</exception>
    /// <exception cref="OperationCanceledException">Istemci istegi iptal ettiyse.</exception>
    Task<FoodImageAnalysisProviderResult> AnalyzeAsync(FoodImageAnalysisProviderRequest request, CancellationToken cancellationToken = default);
}

/// <param name="Image">Fotografin baytlari. Loglanmaz, saklanmaz.</param>
/// <param name="MimeType">image/jpeg | image/png | image/webp.</param>
/// <param name="Instructions">Sabit sistem talimatlari.</param>
/// <param name="ResponseSchema">Saglayiciya verilecek yapilandirilmis cikti semasi (JSON Schema alt kumesi).</param>
public sealed record FoodImageAnalysisProviderRequest(
    ReadOnlyMemory<byte> Image,
    string MimeType,
    string Instructions,
    object ResponseSchema);

/// <param name="Json">Modelin yapilandirilmis ciktisi (ham JSON metni; dogrulanmamis).</param>
/// <param name="Refused">Saglayici guvenlik nedeniyle yanit vermediyse true.</param>
public sealed record FoodImageAnalysisProviderResult(
    string Json,
    int InputTokens,
    int OutputTokens,
    string? StopReason,
    string Model,
    bool Refused,
    string Provider);
