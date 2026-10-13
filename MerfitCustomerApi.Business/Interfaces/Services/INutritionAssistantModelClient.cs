using MerfitCustomerApi.Domain.Entities.Enums;

namespace MerfitCustomerApi.Business.Interfaces.Services;

/// <summary>
/// Beslenme asistaninin yapay zeka erisim sozlesmesi. Is kurallari saglayicidan bagimsizdir; varsayilan
/// uygulama NutritionAiProviderRouter'dir: yapilandirilan saglayiciyi (Groq, Gemini, Anthropic) secer,
/// eszamanlilik/kota/yeniden deneme/yedek saglayici kurallarini uygular.
/// </summary>
public interface INutritionAssistantModelClient
{
    /// <summary>Anahtar ve ayarlar mevcut mu?</summary>
    bool IsConfigured { get; }

    /// <summary>Kullanilan model adi (kullanim kaydi icin).</summary>
    string Model { get; }

    /// <exception cref="Domain.Exceptions.ExternalServiceUnavailableException">Saglayici hatasi / zaman asimi.</exception>
    /// <exception cref="OperationCanceledException">Istemci istegi iptal ettiyse.</exception>
    Task<NutritionAssistantModelResult> CompleteAsync(NutritionAssistantModelRequest request, CancellationToken cancellationToken = default);
}

/// <param name="Instructions">Sabit sistem talimatlari (onbelleklenebilir).</param>
/// <param name="Context">Bu istege ozel guncel beslenme baglami.</param>
/// <param name="Messages">Eskiden yeniye konusma; son eleman yeni kullanici mesajidir.</param>
public sealed record NutritionAssistantModelRequest(
    string Instructions,
    string Context,
    IReadOnlyList<NutritionAssistantModelMessage> Messages);

public sealed record NutritionAssistantModelMessage(NutritionAssistantMessageRole Role, string Content);

/// <param name="Refused">Saglayici guvenlik nedeniyle yanit vermediyse true.</param>
/// <param name="Provider">Yaniti ureten saglayici (kullanim kaydi icin).</param>
public sealed record NutritionAssistantModelResult(
    string Text,
    int InputTokens,
    int OutputTokens,
    string? StopReason,
    string Model,
    bool Refused,
    string Provider = "");
