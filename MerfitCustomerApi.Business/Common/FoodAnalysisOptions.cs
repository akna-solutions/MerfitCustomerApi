namespace MerfitCustomerApi.Business.Common;

/// <summary>
/// appsettings.json icindeki "FoodAnalysis" bolumu: yemek fotografindan kalori analizi (POST /api/food-analysis/analyze).
///
/// Ilk saglayici Gemini'dir; saglayici <see cref="Interfaces.Services.IFoodImageAnalysisProvider"/> arayuzunun
/// arkasindadir. API anahtari buraya YAZILMAZ: FoodAnalysis__Gemini__ApiKey ya da (beslenme asistaniyla ortak)
/// GEMINI_API_KEY ortam degiskeni ile verilir. Anahtar yoksa analiz ucu 503 doner, uygulamanin geri kalani etkilenmez.
/// </summary>
public class FoodAnalysisOptions
{
    public const string SectionName = "FoodAnalysis";

    /// <summary>false ise analiz ucu 503 doner.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Aktif saglayici. Su an yalnizca Gemini.</summary>
    public string Provider { get; set; } = NutritionAiProviderNames.Gemini;

    public NutritionAiProviderSettings Gemini { get; set; } = new()
    {
        BaseUrl = "https://generativelanguage.googleapis.com/v1beta/",
        Model = "gemini-flash-latest",
        Paid = false,
    };

    /// <summary>Kabul edilen en buyuk fotograf (bayt). Mobil uygulama yuklemeden once ~1280 px JPEG'e kucultur (genelde 150-500 KB).</summary>
    public int MaxImageBytes { get; set; } = 4 * 1024 * 1024;

    /// <summary>Kenar basina en kucuk / en buyuk piksel. Cok kucuk goruntuden anlamli tahmin cikmaz.</summary>
    public int MinImageDimension { get; set; } = 200;

    public int MaxImageDimension { get; set; } = 8000;

    /// <summary>
    /// Tek saglayici cagrisinin zaman asimi (saniye). Mobil istemci 95 sn bekler; sira bekleme (QueueTimeoutSeconds)
    /// ile birlikte bunun altinda kalmalidir.
    /// </summary>
    public int TimeoutSeconds { get; set; } = 60;

    /// <summary>Gecici hatalarda (5xx, baglanti) en fazla ek deneme.</summary>
    public int MaxRetries { get; set; } = 1;

    /// <summary>Yanit basina en fazla cikti token'i (modelin dusunme token'lari dahil).</summary>
    public int MaxOutputTokens { get; set; } = 4096;

    /// <summary>
    /// Gemini 3.x modellerinin dusunme duzeyi (generationConfig.thinkingConfig.thinkingLevel: minimal, low, medium, high).
    /// Varsayilan dusunme gorselli istekte yaniti 40 sn'yi asacak kadar uzatabiliyor; tablo okumak icin "low" yeterli.
    /// Bos birakilirsa alan gonderilmez (thinkingLevel desteklemeyen Gemini 2.5 modelleri icin).
    /// </summary>
    public string? ThinkingLevel { get; set; } = "low";

    /// <summary>Dusuk tutulur: ayni fotografta tutarli sonuc beklenir.</summary>
    public double Temperature { get; set; } = 0.2;

    /// <summary>Bu sunucu orneginden ayni anda en fazla analiz (ucretsiz kotanin dakikalik sinirlarini korur).</summary>
    public int MaxConcurrentRequests { get; set; } = 3;

    /// <summary>Eszamanlilik sirasinda en fazla bekleme (saniye); asilirsa "yogun" yaniti doner.</summary>
    public int QueueTimeoutSeconds { get; set; } = 10;

    /// <summary>Saglayici 429 dondurup Retry-After vermediyse analizlerin durdurulacagi sure (saniye).</summary>
    public int DefaultCooldownSeconds { get; set; } = 30;

    /// <summary>Kullanici basina dakikada en fazla analiz istegi (HTTP hiz siniri).</summary>
    public int PerUserRequestsPerMinute { get; set; } = 4;

    /// <summary>IP basina dakikada en fazla analiz istegi (vekil sunucu arkasinda tum istekler tek IP gorunebilir).</summary>
    public int PerIpRequestsPerMinute { get; set; } = 30;

    /// <summary>Kullanici basina son 24 saatte en fazla basarili analiz. Basarisiz analizler sayilmaz.</summary>
    public int DailyAnalysisLimitPerUser { get; set; } = 15;

    /// <summary>Tek fotograftan dondurulen en fazla yemek.</summary>
    public int MaxItems { get; set; } = 8;

    /// <summary>Analiz sonucu bu sure icinde ogune kaydedilebilir (saat).</summary>
    public int ConfirmWindowHours { get; set; } = 24;

    /// <summary>Analiz sonucunun (yemek adlari, degerler) saklanma suresi (gun). Fotograf hic saklanmaz.</summary>
    public int ResultRetentionDays { get; set; } = 30;

    /// <summary>"Bugun" ve kayit tarihinin gecerli araligini belirlemek icin kullanicinin varsayilan saat dilimi.</summary>
    public string TimeZone { get; set; } = "Europe/Istanbul";

    /// <summary>Gecmise donuk kaydedilebilecek en fazla gun.</summary>
    public int MaxPastDays { get; set; } = 30;

    /// <summary>
    /// Mobilde analiz oncesi gosterilen bilgilendirme. Ucretsiz katmanda saglayici gonderilen icerigi urun
    /// gelistirme icin kullanabilir; metin saglayici/katman degisince buradan guncellenir.
    /// </summary>
    public string DataNotice { get; set; } =
        "Fotoğrafın analiz için Google Gemini'ye gönderilir ve sunucularımızda saklanmaz. Ücretsiz kullanımda Google, " +
        "gönderilen içeriği hizmetlerini geliştirmek için kullanabilir ve inceleyebilir; fotoğrafta yüzün, belgen ya da " +
        "kişisel bilgin görünmesin. Değerler tahminidir, tıbbi beslenme tavsiyesi yerine geçmez.";
}
