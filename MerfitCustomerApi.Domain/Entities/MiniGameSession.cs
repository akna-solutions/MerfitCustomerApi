using MerfitCustomerApi.Domain.Common;
using MerfitCustomerApi.Domain.Entities.Enums;

namespace MerfitCustomerApi.Domain.Entities;

/// <summary>
/// Bir mini oyun (Dumbbell Dash) oturumu. Sunucu oturumu baslatirken seed uretir; oyun bitince istemcinin
/// gonderdigi girdiler (zipla tick'leri) bu seed ile sunucuda yeniden oynatilip skor dogrulanir.
///
/// Ayni zamanda MB FIT puan kaynagidir: Status = Completed ve AwardedPoints > 0 olan oturumlar
/// MerfitScoreService'te "mini_game" kategorisinde CompletedAt anina puan olarak yazilir (ayri bir puan
/// cuzdani/defteri yoktur). Oduller bu satira yazildigi icin puan ekleme ve odul kaydi tek bir satir
/// guncellemesidir; yarim kalamaz.
/// </summary>
public class MiniGameSession : BaseEntity
{
    /// <summary>Oturumun sahibi (JWT'deki kullanici; istemciden alinmaz).</summary>
    public long UserId { get; set; }

    /// <summary>Oyun anahtari (orn. "dumbbell-dash").</summary>
    public string GameKey { get; set; } = string.Empty;

    /// <summary>Oyunu baslatan istemcinin motor surumu; replay ayni kurallarla yapilir.</summary>
    public int EngineVersion { get; set; }

    /// <summary>Sunucunun urettigi engel dizisi seed'i (32 bit isaretsiz deger long olarak saklanir).</summary>
    public long Seed { get; set; }

    /// <summary>
    /// Oturum durumu. Iyimser eszamanlilik belirtecidir (concurrency token): ayni oturumu ayni anda
    /// bitirmeye/terk etmeye calisan iki istekten yalnizca biri basarili olur.
    /// </summary>
    public MiniGameSessionStatus Status { get; set; }

    public DateTime StartedAt { get; set; }

    /// <summary>Bu andan sonra gelen sonuc odullendirilmez (Expired).</summary>
    public DateTime ExpiresAt { get; set; }

    public DateTime? CompletedAt { get; set; }

    /// <summary>Istemcinin bildirdigi skor (yalnizca karsilastirma/inceleme icin; puana esas degildir).</summary>
    public int? ClaimedScore { get; set; }

    /// <summary>Sunucudaki replay ile hesaplanan skor.</summary>
    public int? VerifiedScore { get; set; }

    /// <summary>Replay'e gore oyunun surdugu tick sayisi.</summary>
    public int? VerifiedTicks { get; set; }

    /// <summary>Gonderilen zipla girdisi sayisi.</summary>
    public int? InputCount { get; set; }

    /// <summary>Skorun odul tablosundaki karsiligi (gunluk limit uygulanmadan once).</summary>
    public int TierPoints { get; set; }

    /// <summary>Gercekten verilen MB FIT puani (gunluk limit uygulanmis).</summary>
    public int AwardedPoints { get; set; }

    /// <summary>Rejected durumunda red sebebi (orn. "score_mismatch").</summary>
    public string? RejectReason { get; set; }
}
