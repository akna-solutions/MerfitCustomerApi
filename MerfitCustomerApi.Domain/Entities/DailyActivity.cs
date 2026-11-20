using MerfitCustomerApi.Domain.Common;

namespace MerfitCustomerApi.Domain.Entities;

/// <summary>
/// Kullanicinin bir yerel gundeki adim aktivitesi (adim sayar). Kullanici + gun basina tek satir vardir
/// (benzersiz index); senkronizasyon satiri yeniden yazmaz, saatlik kovalari "buyuk olan kalir" kuraliyla
/// birlestirir. Boylece ayni istek tekrar gonderildiginde adimlar iki kez eklenmez.
///
/// Telefonun sensorundeki kumulatif sayac (orn. Android TYPE_STEP_COUNTER) buraya hic yazilmaz; istemci
/// yalnizca o gunun saatlik toplamlarini gonderir ve TotalSteps sunucuda saatlik kovalarin toplamindan
/// hesaplanir.
/// </summary>
public class DailyActivity : BaseEntity
{
    /// <summary>Kaydin sahibi (JWT'deki kullanici; istemciden alinmaz).</summary>
    public long UserId { get; set; }

    /// <summary>Cihazin yerel saat dilimine gore gun (yyyy-MM-dd).</summary>
    public DateOnly Date { get; set; }

    /// <summary>Gunluk toplam adim; her zaman HourlySteps toplamina esittir.</summary>
    public int TotalSteps { get; set; }

    /// <summary>Tahmini yurume mesafesi (metre). Sunucuda profil boyundan hesaplanir.</summary>
    public int DistanceMeters { get; set; }

    /// <summary>Tahmini yakilan kalori (kcal). Sunucuda profil kilosundan hesaplanir.</summary>
    public int CaloriesBurned { get; set; }

    /// <summary>Bu gun icin gecerli gunluk adim hedefi (gun bitince degismez).</summary>
    public int DailyGoal { get; set; }

    /// <summary>Yerel saat 00-23 icin adim sayilari (24 eleman).</summary>
    public int[] HourlySteps { get; set; } = new int[24];

    /// <summary>Son senkronizasyonda istemcinin bildirdigi UTC farki (dakika). Gun icinde gorulen en buyuk deger tutulur.</summary>
    public int UtcOffsetMinutes { get; set; }

    /// <summary>Verinin kaynagi (pedometer, healthkit, health_connect).</summary>
    public string Source { get; set; } = string.Empty;

    /// <summary>Gonderen platform (ios, android).</summary>
    public string? Platform { get; set; }

    /// <summary>
    /// Makullik kontrollerinden birine takilan gun. Kullanici adimlarini gormeye devam eder; ancak bu gun
    /// basari (rozet) ve MB FIT puani hesaplarina katilmaz. Bir kez isaretlenen gun isaretli kalir.
    /// </summary>
    public bool IsSuspicious { get; set; }

    /// <summary>Isaretlenme sebebi (orn. "hour_limit", "future_hour").</summary>
    public string? SuspiciousReason { get; set; }

    /// <summary>Son basarili senkronizasyon ani (UTC).</summary>
    public DateTime LastSyncedAt { get; set; }

    /// <summary>
    /// Iyimser eszamanlilik belirteci (PostgreSQL xmin). Ayni gunu ayni anda guncelleyen iki senkronizasyondan
    /// biri catismaya duser ve guncel satirla yeniden birlestirilir; boylece bir istegin saatleri kaybolmaz.
    /// </summary>
    public uint Version { get; set; }
}
