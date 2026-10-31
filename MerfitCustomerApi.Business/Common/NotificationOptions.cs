namespace MerfitCustomerApi.Business.Common;

/// <summary>
/// appsettings.json "Notifications" bolumu: bildirim planlayici ve gondericisinin ayarlari.
/// Bolum atlanirsa buradaki varsayilanlar kullanilir.
/// </summary>
public class NotificationOptions
{
    public const string SectionName = "Notifications";

    /// <summary>false ise NotificationBackgroundWorker calismaz (orn. bakim veya test ortami). Birden fazla API ornegi ayni anda guvenle calisabilir; kilitler veritabanindadir.</summary>
    public bool WorkerEnabled { get; set; } = true;

    /// <summary>
    /// Kullanicinin saat dilimi bilinmiyorsa kullanilan IANA saat dilimi. Varsayilan MB FIT puanlamasindaki
    /// (MerfitScoring:TimeZoneOffsetHours = 3) gun sinirlariyla ayni olan Europe/Istanbul'dur.
    /// </summary>
    public string DefaultTimeZone { get; set; } = "Europe/Istanbul";

    /// <summary>Gonderim kuyrugunun kontrol araligi.</summary>
    public TimeSpan DispatchInterval { get; set; } = TimeSpan.FromSeconds(15);

    /// <summary>Kural planlamasinin calisma araligi.</summary>
    public TimeSpan RulePlanningInterval { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>Saglayici makbuzlarinin kontrol araligi.</summary>
    public TimeSpan ReceiptCheckInterval { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>Bir turda sahiplenilen en fazla gonderim sayisi.</summary>
    public int DispatchBatchSize { get; set; } = 100;

    /// <summary>Sahiplenilen gonderimin kilit suresi; calisan cokerse bu sureden sonra baska calisan alir.</summary>
    public TimeSpan DispatchLockDuration { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>Gecici saglayici hatalarinda en fazla deneme sayisi.</summary>
    public int MaxDeliveryAttempts { get; set; } = 5;

    /// <summary>Ilk yeniden deneme gecikmesi; her denemede iki katina cikar (ust sinir RetryMaxDelay).</summary>
    public TimeSpan RetryBaseDelay { get; set; } = TimeSpan.FromSeconds(30);

    public TimeSpan RetryMaxDelay { get; set; } = TimeSpan.FromHours(1);

    /// <summary>
    /// Kural saati gectikten sonra en fazla bu sure icinde planlanir ve gonderilir; servis bu sureden uzun
    /// kapali kalirsa o gunun hatirlatmasi gonderilmez (bayat hatirlatma onlenir).
    /// </summary>
    public TimeSpan RuleSendWindow { get; set; } = TimeSpan.FromHours(2);

    /// <summary>Kullanici basina yerel gun icinde gonderilecek en fazla MB Fit push sayisi (muaf turler haric).</summary>
    public int DailyPushLimit { get; set; } = 4;

    /// <summary>
    /// MB Shop icin ayri gunluk push limiti (muaf turler haric). MB Shop kampanyalari MB Fit hatirlatmalarinin
    /// limitini tuketmez, MB Fit bildirimleri de MB Shop'unkini.
    /// </summary>
    public int ShopDailyPushLimit { get; set; } = 2;

    /// <summary>Gunluk limite takilmayan bildirim turleri. Siparis durumu bir islem bildirimidir, sinirlanmaz.</summary>
    public List<string> DailyLimitExemptTypes { get; set; } = new() { "System", "Reward", "StoreOrderUpdate" };

    /// <summary>Planlamada tek seferde islenen kullanici sayisi.</summary>
    public int PlanningUserBatchSize { get; set; } = 500;

    /// <summary>Expo makbuzlari bu sureden sonra sorgulanir (Expo onerisi: ~15 dk).</summary>
    public TimeSpan ReceiptDelay { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>Bu sureden eski kabul edilmis denemelerin makbuzu artik sorgulanmaz (Expo makbuzlari ~24 saat tutar).</summary>
    public TimeSpan ReceiptMaxAge { get; set; } = TimeSpan.FromHours(24);

    public ExpoPushOptions Expo { get; set; } = new();
}

/// <summary>"Notifications:Expo" bolumu.</summary>
public class ExpoPushOptions
{
    public string BaseUrl { get; set; } = "https://exp.host";

    /// <summary>
    /// Expo hesabinda "Enhanced Push Security" aciksa gereken erisim jetonu. Koda veya appsettings'e
    /// yazilmaz; ortam degiskeni ile verilir: Notifications__Expo__AccessToken.
    /// </summary>
    public string? AccessToken { get; set; }

    public int TimeoutSeconds { get; set; } = 15;

    /// <summary>Android bildirim kanali (mobil uygulama ayni kimlikle olusturur).</summary>
    public string AndroidChannelId { get; set; } = "default";

    /// <summary>MB Shop bildirimlerinin Android kanali (mobil uygulama "MB Shop" adiyla olusturur).</summary>
    public string ShopAndroidChannelId { get; set; } = "mbshop";
}
