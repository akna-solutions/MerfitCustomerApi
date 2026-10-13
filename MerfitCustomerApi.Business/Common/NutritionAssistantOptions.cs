namespace MerfitCustomerApi.Business.Common;

/// <summary>
/// appsettings.json icindeki "NutritionAssistant" bolumu: beslenme asistaninin yapay zeka saglayicilari,
/// kullanim/maliyet sinirlari ve sohbet baglami ayarlari.
///
/// Varsayilan saglayici Groq'tur (ucretsiz kota ile baslanabilir). Gemini istege bagli yedek, Anthropic ise
/// ucretli secenek olarak desteklenir. API anahtarlari buraya YAZILMAZ; ortam degiskeni ile verilir:
/// GROQ_API_KEY / GEMINI_API_KEY / ANTHROPIC_API_KEY (ya da NutritionAssistant__Groq__ApiKey gibi).
/// Anahtar yoksa asistan "kullanilamiyor" doner, beslenme ekraninin geri kalani etkilenmez.
/// </summary>
public class NutritionAssistantOptions
{
    public const string SectionName = "NutritionAssistant";

    /// <summary>false ise sohbet uc noktasi 503 doner (degerlendirme karti calismaya devam eder).</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Aktif saglayici: Groq | Gemini | Anthropic.</summary>
    public string Provider { get; set; } = NutritionAiProviderNames.Groq;

    /// <summary>
    /// Aktif saglayici kota/hata nedeniyle yanit veremezse denenecek yedek saglayici (bos: yedek yok).
    /// Ucretli bir saglayici yedek olarak yalnizca <see cref="AllowPaidFallback"/> true ise kullanilir.
    /// </summary>
    public string? FallbackProvider { get; set; }

    /// <summary>Ucretli saglayiciya otomatik gecis. Varsayilan kapali: beklenmeyen maliyet olusmaz.</summary>
    public bool AllowPaidFallback { get; set; }

    public NutritionAiProviderSettings Groq { get; set; } = new()
    {
        BaseUrl = "https://api.groq.com/openai/v1/",
        Model = "openai/gpt-oss-120b",
        ReasoningEffort = "low",
        Paid = false,
    };

    public NutritionAiProviderSettings Gemini { get; set; } = new()
    {
        BaseUrl = "https://generativelanguage.googleapis.com/v1beta/",
        Model = "gemini-flash-latest",
        Paid = false,
    };

    public NutritionAiProviderSettings Anthropic { get; set; } = new()
    {
        Model = "claude-opus-5-5",
        ReasoningEffort = "low",
        Paid = true,
    };

    /// <summary>Yanit basina en fazla cikti token'i (modelin dusunme token'lari dahil); kota/maliyet ust siniri.</summary>
    public int MaxOutputTokens { get; set; } = 1024;

    /// <summary>Yanit cesitliligi (0-1). Beslenme onerilerinde dusuk tutulur.</summary>
    public double Temperature { get; set; } = 0.4;

    /// <summary>Tek saglayici cagrisinin zaman asimi (saniye). Mobil istemci 60 sn bekler.</summary>
    public int TimeoutSeconds { get; set; } = 25;

    /// <summary>Gecici hatalarda (5xx, baglanti, kisa Retry-After'li 429) ayni saglayicida en fazla ek deneme.</summary>
    public int MaxRetries { get; set; } = 1;

    /// <summary>429'daki Retry-After bu sureden kisaysa beklenip tekrar denenir; uzunsa saglayici bu sure boyunca atlanir.</summary>
    public int MaxRetryAfterWaitSeconds { get; set; } = 3;

    /// <summary>429 Retry-After icermiyorsa saglayicinin atlanacagi sure (saniye).</summary>
    public int DefaultCooldownSeconds { get; set; } = 30;

    /// <summary>Bu sunucu orneginden saglayici basina ayni anda en fazla istek (ucretsiz kotanin dakikalik sinirlarini korur).</summary>
    public int MaxConcurrentRequestsPerProvider { get; set; } = 4;

    /// <summary>Eszamanlilik sirasinda en fazla bekleme (saniye); asilirsa "yogun" yaniti doner.</summary>
    public int QueueTimeoutSeconds { get; set; } = 8;

    /// <summary>Kullanici basina dakikada en fazla sohbet istegi (HTTP hiz siniri).</summary>
    public int PerUserRequestsPerMinute { get; set; } = 6;

    /// <summary>IP basina dakikada en fazla sohbet istegi (HTTP hiz siniri; vekil sunucu arkasinda tum istekler tek IP gorunebilir).</summary>
    public int PerIpRequestsPerMinute { get; set; } = 60;

    /// <summary>Modele birebir gonderilen en fazla onceki mesaj sayisi (kullanici + asistan).</summary>
    public int HistoryMessageLimit { get; set; } = 10;

    /// <summary>
    /// Pencere disinda kalan eski kullanici mesajlarindan, tercihlerin unutulmamasi icin kisaltilarak
    /// eklenen en fazla not sayisi (ek model cagrisi yapilmaz).
    /// </summary>
    public int EarlierUserNoteLimit { get; set; } = 8;

    /// <summary>Kullanici mesajinin en fazla karakter sayisi.</summary>
    public int MaxUserMessageLength { get; set; } = 1000;

    /// <summary>Kullanici basina son 24 saatte gonderilebilecek en fazla mesaj.</summary>
    public int DailyMessageLimitPerUser { get; set; } = 40;

    /// <summary>Bir gunluk sohbette en fazla kullanici mesaji.</summary>
    public int MaxUserMessagesPerConversation { get; set; } = 30;

    /// <summary>Sohbetler son mesajdan bu kadar gun sonra silinir.</summary>
    public int RetentionDays { get; set; } = 30;

    /// <summary>Sohbet acilisinda istemciye dondurulen en fazla gecmis mesaj.</summary>
    public int MaxReturnedMessages { get; set; } = 60;

    /// <summary>"Bugun" ve gunun saatini belirlemek icin kullanicinin varsayilan saat dilimi.</summary>
    public string TimeZone { get; set; } = "Europe/Istanbul";

    /// <summary>Gecmise donuk sohbet acilabilecek en fazla gun (bugunden geriye).</summary>
    public int MaxPastDays { get; set; } = 30;

    public NutritionAiProviderSettings? SettingsFor(string? provider) => provider?.Trim().ToLowerInvariant() switch
    {
        "groq" => Groq,
        "gemini" => Gemini,
        "anthropic" => Anthropic,
        _ => null,
    };
}

/// <summary>Tek bir yapay zeka saglayicisinin ayarlari.</summary>
public class NutritionAiProviderSettings
{
    /// <summary>API anahtari. Yalnizca ortam degiskeni / gizli yapilandirma ile verilir, loglanmaz.</summary>
    public string? ApiKey { get; set; }

    /// <summary>API adresi (vekil sunucu ya da test icin degistirilebilir).</summary>
    public string? BaseUrl { get; set; }

    /// <summary>Model kimligi. Saglayicinin guncel model listesine gore yapilandirmadan degistirilir.</summary>
    public string Model { get; set; } = string.Empty;

    /// <summary>Dusunen modellerde dusunme derinligi (low | medium | high); bos ise gonderilmez.</summary>
    public string? ReasoningEffort { get; set; }

    /// <summary>Bu saglayicinin kullanimi ucretli mi? Ucretli saglayiciya otomatik gecis varsayilan olarak kapalidir.</summary>
    public bool Paid { get; set; }
}

public static class NutritionAiProviderNames
{
    public const string Groq = "Groq";
    public const string Gemini = "Gemini";
    public const string Anthropic = "Anthropic";
}
