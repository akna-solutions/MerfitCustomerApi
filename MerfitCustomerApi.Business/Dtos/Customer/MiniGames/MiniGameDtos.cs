using System.ComponentModel.DataAnnotations;
using MerfitCustomerApi.Business.Services.MiniGames;

namespace MerfitCustomerApi.Business.Dtos.Customer.MiniGames;

public class StartMiniGameSessionRequest
{
    /// <summary>Istemcideki oyun motorunun surumu; sunucununkiyle ayni olmalidir.</summary>
    public int EngineVersion { get; set; }
}

/// <summary>
/// Oyun sonucu. Skor istemcinin iddiasidir; sunucu oyunu Seed + FlapTicks ile yeniden oynatir ve yalnizca
/// kendi hesapladigi skoru odullendirir.
/// </summary>
public class FinishMiniGameSessionRequest
{
    [Range(0, int.MaxValue)]
    public int Score { get; set; }

    /// <summary>Oyunun surdugu simulasyon adimi (tick) sayisi.</summary>
    [Range(0, DumbbellDashSimulator.MaxTicks)]
    public int Ticks { get; set; }

    /// <summary>Ziplamanin uygulandigi tick'ler (kesin artan).</summary>
    [MaxLength(DumbbellDashSimulator.MaxTicks)]
    public List<int> FlapTicks { get; set; } = new();
}

public class MiniGameRewardTierDto
{
    public int MinScore { get; set; }
    public int Points { get; set; }
}

public class MiniGameRewardRulesDto
{
    public List<MiniGameRewardTierDto> Tiers { get; set; } = new();
    public int MaxPointsPerGame { get; set; }
    public int DailyPointLimit { get; set; }
}

public class MiniGameSessionDto
{
    /// <summary>false ise odul sistemi kapali; oyun puansiz oynanir ve SessionId/Seed bostur.</summary>
    public bool RewardsEnabled { get; set; }

    public long? SessionId { get; set; }

    /// <summary>32 bit isaretsiz seed.</summary>
    public long? Seed { get; set; }

    public int EngineVersion { get; set; }
    public DateTime? ExpiresAt { get; set; }

    public MiniGameRewardRulesDto Rules { get; set; } = new();

    /// <summary>Bugun (yerel gun) mini oyundan kazanilan puan.</summary>
    public int EarnedToday { get; set; }

    public int RemainingToday { get; set; }
}

public static class MiniGameResultStatus
{
    /// <summary>Puan verildi.</summary>
    public const string Rewarded = "Rewarded";
    /// <summary>Skor ilk odul esiginin altinda.</summary>
    public const string NoReward = "NoReward";
    /// <summary>Skor odul esigindeydi ama gunluk limit doldugu icin puan verilmedi.</summary>
    public const string DailyLimitReached = "DailyLimitReached";
    /// <summary>Sonuc dogrulanamadi.</summary>
    public const string Rejected = "Rejected";
    /// <summary>Sonuc oturum suresi dolduktan sonra geldi.</summary>
    public const string Expired = "Expired";
    /// <summary>Bu oturum yerine yeni bir oyun baslatilmisti.</summary>
    public const string Abandoned = "Abandoned";
}

public class MiniGameResultDto
{
    public long SessionId { get; set; }

    /// <summary><see cref="MiniGameResultStatus"/> degerlerinden biri.</summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>Sunucunun yeniden oynatmayla hesapladigi skor (Rejected/Expired/Abandoned ise null).</summary>
    public int? VerifiedScore { get; set; }

    /// <summary>Skorun odul tablosundaki karsiligi (gunluk limit uygulanmadan once).</summary>
    public int TierPoints { get; set; }

    /// <summary>Bu oyun icin verilen MB FIT puani.</summary>
    public int AwardedPoints { get; set; }

    /// <summary>Gunluk limit bu oyunda puani azalttiysa true.</summary>
    public bool DailyLimitReached { get; set; }

    /// <summary>Kullanicinin tum zamanlar MB FIT puani: bu oyunun puani eklenmeden onceki ve sonraki.</summary>
    public int PreviousTotalPoints { get; set; }
    public int TotalPoints { get; set; }

    public int EarnedToday { get; set; }
    public int RemainingToday { get; set; }

    /// <summary>Bir sonraki odul esigi (en ust esige ulasildiysa null).</summary>
    public MiniGameRewardTierDto? NextTier { get; set; }
}
