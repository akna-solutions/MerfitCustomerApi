using Microsoft.Extensions.Options;

namespace MerfitCustomerApi.Business.Common;

/// <summary>
/// appsettings.json "MiniGameRewards" bolumu: mini oyun (Dumbbell Dash) skorlarinin MB FIT puanina
/// donusumu ve suistimal limitleri. Odul degerleri yalnizca buradan yonetilir; mobil uygulama tabloyu
/// oyun oturumu baslatilirken sunucudan alir. Bolum atlanirsa asagidaki varsayilanlar kullanilir.
///
/// Varsayilanlarin gerekcesi (MerfitScoringOptions ile kiyas): tamamlanan antrenman 100, kisisel rekor 25,
/// ogun gunu 10, su gunu 5 puan. Mini oyun bir dinlenme eglencesidir; gunluk en fazla 15 puan (bir antrenmanin
/// %15'i) ile siralamayi antrenmandan daha etkili bir yol haline gelmez, ama gunluk hedef olarak anlamlidir.
/// </summary>
public class MiniGameRewardOptions
{
    public const string SectionName = "MiniGameRewards";

    /// <summary>false ise oyun yine oynanir ama oturum acilmaz ve puan verilmez (ozellik bayragi).</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Skor esikleri. Bos/null ise <see cref="DefaultTiers"/> kullanilir. (Liste config binder'da varsayilan
    /// degerlere eklenmesin diye varsayilanlar ayri tutulur.)
    /// </summary>
    public List<MiniGameRewardTier>? RewardTiers { get; set; }

    /// <summary>Tek oyunda kazanilabilecek en fazla puan.</summary>
    public int MaxPointsPerGame { get; set; } = 50;

    /// <summary>Bir kullanicinin bir gunde (MerfitScoring:TimeZoneOffsetHours'a gore yerel gun) kazanabilecegi en fazla puan.</summary>
    public int DailyPointLimit { get; set; } = 150;

    /// <summary>Bir kullanicinin bir gunde baslatabilecegi en fazla oyun oturumu (istek siniri).</summary>
    public int MaxSessionsPerDay { get; set; } = 60;

    /// <summary>Oturum acildiktan sonra sonucun gonderilebilecegi en uzun sure; sonra gelen sonuc odullendirilmez.</summary>
    public TimeSpan SessionTimeout { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>
    /// Sure tutarliligi toleransi: oyunun simulasyon suresi, oturumun acilmasindan sonucun gelmesine kadar gecen
    /// gercek sureyi bu kadar asabilir (istemci/sunucu saat ve ag gecikmesi payi). Daha hizli "oynanmis" sonuc reddedilir.
    /// </summary>
    public TimeSpan ClockTolerance { get; set; } = TimeSpan.FromSeconds(3);

    public static readonly IReadOnlyList<MiniGameRewardTier> DefaultTiers = new[]
    {
        new MiniGameRewardTier { MinScore = 10, Points = 10 },
        new MiniGameRewardTier { MinScore = 20, Points = 20 },
        new MiniGameRewardTier { MinScore = 30, Points = 30 },
        new MiniGameRewardTier { MinScore = 50, Points = 50 },
    };

    /// <summary>Kullanilan esikler, MinScore'a gore artan sirada.</summary>
    public IReadOnlyList<MiniGameRewardTier> EffectiveTiers =>
        (RewardTiers is { Count: > 0 } ? RewardTiers : DefaultTiers).OrderBy(t => t.MinScore).ToList();

    /// <summary>Skorun tek oyun limitiyle sinirlanmis odul puani.</summary>
    public int PointsForScore(int score)
    {
        var tier = EffectiveTiers.LastOrDefault(t => score >= t.MinScore);
        return tier is null ? 0 : Math.Min(tier.Points, MaxPointsPerGame);
    }

    /// <summary>Skordan sonraki ilk odul esigi; en ust esige ulasildiysa null.</summary>
    public MiniGameRewardTier? NextTier(int score) => EffectiveTiers.FirstOrDefault(t => t.MinScore > score);
}

public class MiniGameRewardTier
{
    public int MinScore { get; set; }
    public int Points { get; set; }
}

/// <summary>Hatali odul ayari sessizce yanlis puan dagitmasin diye uygulama baslarken (ValidateOnStart) dogrular.</summary>
public class MiniGameRewardOptionsValidator : IValidateOptions<MiniGameRewardOptions>
{
    public ValidateOptionsResult Validate(string? name, MiniGameRewardOptions options)
    {
        var errors = new List<string>();
        var tiers = options.EffectiveTiers;

        if (tiers.Any(t => t.MinScore <= 0 || t.Points <= 0))
        {
            errors.Add("MiniGameRewards: RewardTiers icindeki MinScore ve Points 0'dan buyuk olmalidir.");
        }

        if (tiers.Select(t => t.MinScore).Distinct().Count() != tiers.Count)
        {
            errors.Add("MiniGameRewards: ayni MinScore birden fazla kez tanimlanamaz.");
        }

        for (var i = 1; i < tiers.Count; i++)
        {
            if (tiers[i].Points < tiers[i - 1].Points)
            {
                errors.Add("MiniGameRewards: daha yuksek skor esigi daha az puan veremez.");
                break;
            }
        }

        if (options.MaxPointsPerGame < 0 || options.DailyPointLimit < 0 || options.MaxSessionsPerDay <= 0)
        {
            errors.Add("MiniGameRewards: MaxPointsPerGame/DailyPointLimit negatif, MaxSessionsPerDay 0 veya negatif olamaz.");
        }

        if (options.SessionTimeout <= TimeSpan.Zero || options.ClockTolerance < TimeSpan.Zero)
        {
            errors.Add("MiniGameRewards: SessionTimeout pozitif, ClockTolerance negatif olmayan bir sure olmalidir.");
        }

        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }
}
