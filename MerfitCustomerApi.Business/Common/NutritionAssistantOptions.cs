namespace MerfitCustomerApi.Business.Common;

/// <summary>
/// appsettings.json icindeki "NutritionAssistant" bolumu: beslenme asistaninin yapay zeka saglayicisi
/// (Anthropic Claude), maliyet/kullanim sinirlari ve sohbet baglami ayarlari.
/// API anahtari buraya YAZILMAZ; NutritionAssistant__ApiKey (ya da ANTHROPIC_API_KEY) ortam degiskeni ile verilir.
/// Anahtar yoksa asistan "kullanilamiyor" doner, beslenme ekraninin geri kalani etkilenmez.
/// </summary>
public class NutritionAssistantOptions
{
    public const string SectionName = "NutritionAssistant";

    /// <summary>false ise sohbet uc noktasi 503 doner (degerlendirme karti calismaya devam eder).</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Anthropic API anahtari. Yalnizca ortam degiskeni / gizli yapilandirma ile verilir.</summary>
    public string? ApiKey { get; set; }

    /// <summary>Anthropic API adresi (vekil sunucu ya da test icin degistirilebilir).</summary>
    public string? BaseUrl { get; set; }

    /// <summary>Kullanilacak Claude modeli.</summary>
    public string Model { get; set; } = "claude-opus-5-5";

    /// <summary>Modelin dusunme/yanit derinligi: low | medium | high. Sohbet icin low yeterli ve ucuzdur.</summary>
    public string Effort { get; set; } = "low";

    /// <summary>Yanit basina en fazla cikti token'i (dusunme dahil); maliyet ust siniri.</summary>
    public int MaxOutputTokens { get; set; } = 2048;

    /// <summary>Model cagrisinin zaman asimi (saniye). Mobil istemcinin istek zaman asimindan kisa olmalidir.</summary>
    public int TimeoutSeconds { get; set; } = 40;

    /// <summary>Saglayici reddederse (refusal) istegin sunucu tarafinda uygun modele yonlendirilmesi.</summary>
    public bool EnableRefusalFallback { get; set; } = true;

    /// <summary>Modele baglam olarak gonderilen en fazla onceki mesaj sayisi (kullanici + asistan).</summary>
    public int HistoryMessageLimit { get; set; } = 12;

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
}
