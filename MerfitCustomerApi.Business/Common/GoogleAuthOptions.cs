namespace MerfitCustomerApi.Business.Common;

/// <summary>
/// appsettings.json icindeki "GoogleAuth" bolumu: Google ile giris/kayit ayarlari.
///
/// ClientIds, Google Cloud Console'da olusturulan OAuth istemci kimlikleridir (gizli degildir ama ortama
/// gore degistigi icin appsettings'e yazilmaz). Production'da GoogleAuth__ClientIds__0 (Web istemcisi),
/// GoogleAuth__ClientIds__1 (iOS istemcisi) ortam degiskenleriyle verilir. Liste bossa uygulama acilir;
/// yalnizca Google uclari 503 doner.
/// </summary>
public class GoogleAuthOptions
{
    public const string SectionName = "GoogleAuth";

    /// <summary>
    /// ID token'in "aud" alaninda kabul edilen OAuth istemci kimlikleri (...apps.googleusercontent.com).
    /// Mobil uygulama webClientId ile yapilandirildigi icin token'lar genelde Web istemcisi kimligini tasir;
    /// iOS istemcisi de guvenli tarafta kalmak icin eklenebilir.
    /// </summary>
    public List<string> ClientIds { get; set; } = new();

    /// <summary>Sunucu ile Google saatleri arasindaki kabul edilen fark (token exp/iat kontrolu).</summary>
    public TimeSpan ClockSkew { get; set; } = TimeSpan.FromMinutes(5);
}
