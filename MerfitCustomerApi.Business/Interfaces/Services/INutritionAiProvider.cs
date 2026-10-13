namespace MerfitCustomerApi.Business.Interfaces.Services;

/// <summary>
/// Tek bir yapay zeka saglayicisi (Groq, Gemini, Anthropic). Saglayici yalnizca istegi gonderir ve hatayi
/// <see cref="NutritionAiProviderException"/> ile siniflandirir; yeniden deneme, kota bekleme ve yedek
/// saglayiciya gecis NutritionAiProviderRouter'dadir. Yeni bir saglayici eklemek is mantigini degistirmez.
/// </summary>
public interface INutritionAiProvider
{
    /// <summary>Yapilandirmadaki adi (Groq | Gemini | Anthropic).</summary>
    string Name { get; }

    /// <summary>Anahtar ve model ayarli mi?</summary>
    bool IsConfigured { get; }

    /// <summary>Kullanimi ucretli mi? Ucretli saglayiciya otomatik gecis varsayilan olarak kapalidir.</summary>
    bool IsPaid { get; }

    string Model { get; }

    /// <exception cref="NutritionAiProviderException">Saglayici hatasi, kota veya zaman asimi.</exception>
    /// <exception cref="OperationCanceledException">Istemci istegi iptal ettiyse.</exception>
    Task<NutritionAssistantModelResult> CompleteAsync(NutritionAssistantModelRequest request, CancellationToken cancellationToken = default);
}

public enum NutritionAiFailureKind
{
    /// <summary>429 / kota: Retry-After kadar beklenmeli.</summary>
    RateLimited,
    /// <summary>Zaman asimi.</summary>
    Timeout,
    /// <summary>5xx, baglanti hatasi: sinirli yeniden denenebilir.</summary>
    Transient,
    /// <summary>400/401/403/404: anahtar, model adi ya da istek hatasi. Yeniden denenmez.</summary>
    Rejected,
}

/// <summary>Saglayici hatasi. Mesaj kullaniciya gosterilmez ve API anahtari, istek/yanit icerigi tasimaz.</summary>
public sealed class NutritionAiProviderException : Exception
{
    public NutritionAiProviderException(string provider, NutritionAiFailureKind kind, int? statusCode = null, TimeSpan? retryAfter = null, string? errorCode = null, Exception? innerException = null)
        : base($"{provider} saglayici hatasi: {kind}{(statusCode is null ? string.Empty : $" (HTTP {statusCode})")}{(errorCode is null ? string.Empty : $" [{errorCode}]")}", innerException)
    {
        Provider = provider;
        Kind = kind;
        StatusCode = statusCode;
        RetryAfter = retryAfter;
        ErrorCode = errorCode;
    }

    public string Provider { get; }
    public NutritionAiFailureKind Kind { get; }
    public int? StatusCode { get; }
    public TimeSpan? RetryAfter { get; }

    /// <summary>Saglayicinin hata kodu/tipi (orn. "rate_limit_exceeded"); icerik degil.</summary>
    public string? ErrorCode { get; }
}
