namespace MerfitCustomerApi.Business.Dtos.Auth;

/// <summary>
/// Basarili kayit/giris isleminden sonra doner. RN tarafi bu token'lari alip
/// kullaniciyi dogrudan dashboard'a yonlendirebilir (SuccessScreen -> handleStartTraining).
/// </summary>
public class AuthResponse
{
    public long UserId { get; set; }

    public string Email { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// UserProfile.Username - Register'da kullanicinin AccountStep'te sectigi deger, Login'de
    /// veritabanindaki gercek deger. RN tarafi bunu "@username" seklinde gosterebilir.
    /// </summary>
    public string Username { get; set; } = string.Empty;

    /// <summary>API isteklerinde Authorization: Bearer header'inda kullanilacak JWT.</summary>
    public string AccessToken { get; set; } = string.Empty;

    /// <summary>Access token suresi doldugunda yenilemek icin kullanilacak jeton.</summary>
    public string RefreshToken { get; set; } = string.Empty;

    /// <summary>AccessToken'in son gecerlilik tarihi (UTC).</summary>
    public DateTime AccessTokenExpiresAt { get; set; }

    /// <summary>
    /// Kayit sirasinda olusturulan PersonalizationJob'un durumu ("Pending" | "Processing" |
    /// "Completed" | "Failed"). RN tarafi bu alanla kisisel antrenman/beslenme planinin henuz
    /// arka planda hazirlandigini anlayip dashboard'da uygun bir bos/yukleniyor durumu gosterebilir.
    /// Login yanitinda doldurulmaz (null); yalnizca RegisterAsync tarafindan set edilir.
    /// </summary>
    public string? PersonalizationStatus { get; set; }

    /// <summary>
    /// E-posta adresi dogrulandi mi. Dogrulanmamis hesaplar giris yapabilir ve uygulamayi kullanabilir
    /// (mevcut kullanicilarin girisi bozulmaz); uygulama dogrulama hatirlatmasi gosterir.
    /// </summary>
    public bool EmailConfirmed { get; set; }

    /// <summary>
    /// Yalnizca register yanitinda dolu: dogrulama e-postasi gonderildiyse true, gonderilemediyse false
    /// (uygulama "tekrar gonder" sunar). Login yanitinda null.
    /// </summary>
    public bool? VerificationEmailSent { get; set; }

    /// <summary>
    /// Bu istekte yeni hesap olusturulduysa true (register ve Google ile kayit). Uygulama yeni hesaplarda
    /// "Hazirsin" ekranini gosterir; giris yanitlarinda false.
    /// </summary>
    public bool IsNewUser { get; set; }
}
