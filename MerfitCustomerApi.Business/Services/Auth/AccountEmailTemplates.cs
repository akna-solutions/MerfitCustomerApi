using System.Net;
using MerfitCustomerApi.Business.Interfaces.Services;

namespace MerfitCustomerApi.Business.Services.Auth;

/// <summary>
/// Hesap e-postalarinin (dogrulama, sifre sifirlama, sifre degisti bildirimi) HTML ve duz metin sablonlari.
/// MB FIT renkleri (siyah, beyaz, mavi), tablo tabanli ve satir ici stilli: e-posta istemcilerinde ve
/// mobilde dogru gorunur. Kullanicidan gelen tum degerler (ad) HTML-encode edilir; tek kullanimlik kod sunucuda
/// uretilir ve yine encode edilerek yazilir.
/// </summary>
public static class AccountEmailTemplates
{
    private const string Blue = "#1D51C0";
    private const string Black = "#0B0D12";
    private const string Muted = "#5B606B";
    private const string Background = "#F5F6F8";

    public static OutgoingEmail EmailVerification(string to, string? firstName, string code, TimeSpan lifetime)
    {
        var greeting = Greeting(firstName);
        var duration = FormatDuration(lifetime);
        var html = Layout(
            preheader: "E-posta adresini doğrulamak için kodun hazır.",
            title: "E-posta adresini doğrula",
            paragraphs: new[]
            {
                greeting,
                "MB FIT hesabını oluşturduğun için teşekkürler. E-posta adresini doğrulamak için bu kodu uygulamaya gir.",
            },
            code: code,
            footnote: $"Kod {duration} geçerlidir ve yalnızca bir kez kullanılabilir. Kodu kimseyle paylaşma. Hesabı sen oluşturmadıysan bu e-postayı yok sayabilirsin.");

        var text = $"""
            {greeting}

            MB FIT hesabını oluşturduğun için teşekkürler. E-posta adresini doğrulamak için bu kodu uygulamaya gir:

            {code}

            Kod {duration} geçerlidir ve yalnızca bir kez kullanılabilir. Kodu kimseyle paylaşma. Hesabı sen oluşturmadıysan bu e-postayı yok sayabilirsin.

            MB FIT
            """;

        return new OutgoingEmail(to, "MB FIT e-posta doğrulama kodun", html, text);
    }

    public static OutgoingEmail PasswordReset(string to, string? firstName, string code, TimeSpan lifetime)
    {
        var greeting = Greeting(firstName);
        var duration = FormatDuration(lifetime);
        var html = Layout(
            preheader: "MB FIT şifreni sıfırlamak için kodun hazır.",
            title: "Şifreni sıfırla",
            paragraphs: new[]
            {
                greeting,
                "MB FIT hesabın için bir şifre sıfırlama isteği aldık. Yeni şifreni belirlemek için bu kodu uygulamaya gir.",
            },
            code: code,
            footnote: $"Kod {duration} geçerlidir ve yalnızca bir kez kullanılabilir. Kodu kimseyle paylaşma. Bu isteği sen yapmadıysan bu e-postayı yok sayabilirsin; şifren değişmez. MB FIT sana hiçbir zaman şifreni e-postayla göndermez.");

        var text = $"""
            {greeting}

            MB FIT hesabın için bir şifre sıfırlama isteği aldık. Yeni şifreni belirlemek için bu kodu uygulamaya gir:

            {code}

            Kod {duration} geçerlidir ve yalnızca bir kez kullanılabilir. Kodu kimseyle paylaşma. Bu isteği sen yapmadıysan bu e-postayı yok sayabilirsin; şifren değişmez.

            MB FIT
            """;

        return new OutgoingEmail(to, "MB FIT şifre sıfırlama kodun", html, text);
    }

    public static OutgoingEmail PasswordChanged(string to, string? firstName)
    {
        var greeting = Greeting(firstName);
        var html = Layout(
            preheader: "MB FIT şifren değiştirildi.",
            title: "Şifren değiştirildi",
            paragraphs: new[]
            {
                greeting,
                "MB FIT hesabının şifresi az önce değiştirildi ve diğer cihazlardaki oturumların kapatıldı.",
                "Bu değişikliği sen yapmadıysan uygulamadaki “Şifremi Unuttum” adımıyla hemen yeni bir şifre belirle ve bizimle iletişime geç.",
            },
            code: null,
            footnote: "Bu e-posta hesabının güvenliği için otomatik olarak gönderildi.");

        var text = $"""
            {greeting}

            MB FIT hesabının şifresi az önce değiştirildi ve diğer cihazlardaki oturumların kapatıldı.
            Bu değişikliği sen yapmadıysan uygulamadaki "Şifremi Unuttum" adımıyla hemen yeni bir şifre belirle.

            MB FIT
            """;

        return new OutgoingEmail(to, "MB FIT şifren değiştirildi", html, text);
    }

    private static string Greeting(string? firstName)
    {
        var name = firstName?.Trim();
        return string.IsNullOrEmpty(name) ? "Merhaba," : $"Merhaba {name},";
    }

    private static string FormatDuration(TimeSpan lifetime)
    {
        if (lifetime.TotalHours >= 1 && lifetime.TotalMinutes % 60 == 0)
        {
            return $"{(int)lifetime.TotalHours} saat";
        }

        return $"{Math.Max(1, (int)Math.Round(lifetime.TotalMinutes))} dakika";
    }

    private static string Encode(string value) => WebUtility.HtmlEncode(value);

    private static string Layout(string preheader, string title, IEnumerable<string> paragraphs, string? code, string footnote)
    {
        // Tum metinler (kullanici adi dahil) duz metin olarak gelir ve burada encode edilir.
        var body = string.Join("\n", paragraphs.Select(p =>
            $"""<p style="margin:0 0 16px;font-size:16px;line-height:24px;color:{Black};">{Encode(p)}</p>"""));

        var codeBlock = code is null
            ? string.Empty
            : $"""
              <table role="presentation" cellpadding="0" cellspacing="0" border="0" style="margin:8px 0 24px;">
                <tr>
                  <td align="center" style="border:2px solid {Blue};border-radius:12px;padding:14px 28px;">
                    <span style="font-family:'SFMono-Regular',Menlo,Consolas,monospace;font-size:32px;font-weight:800;letter-spacing:8px;color:{Black};">{Encode(code)}</span>
                  </td>
                </tr>
              </table>
              """;

        return $"""
            <!DOCTYPE html>
            <html lang="tr">
            <head>
              <meta charset="utf-8">
              <meta name="viewport" content="width=device-width, initial-scale=1">
              <meta name="color-scheme" content="light">
              <title>{Encode(title)}</title>
            </head>
            <body style="margin:0;padding:0;background:{Background};font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Helvetica,Arial,sans-serif;">
              <div style="display:none;max-height:0;overflow:hidden;opacity:0;">{Encode(preheader)}</div>
              <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0" style="background:{Background};">
                <tr>
                  <td align="center" style="padding:24px 12px;">
                    <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0" style="max-width:560px;background:#FFFFFF;border-radius:16px;overflow:hidden;">
                      <tr>
                        <td style="background:{Black};padding:20px 28px;">
                          <span style="font-size:20px;font-weight:800;letter-spacing:1px;color:#FFFFFF;">MB <span style="color:#3E7BFA;">FIT</span></span>
                        </td>
                      </tr>
                      <tr>
                        <td style="padding:32px 28px 8px;">
                          <h1 style="margin:0 0 20px;font-size:24px;line-height:30px;font-weight:700;color:{Black};">{Encode(title)}</h1>
                          {body}
                          {codeBlock}
                          <p style="margin:0 0 24px;font-size:13px;line-height:20px;color:{Muted};">{Encode(footnote)}</p>
                        </td>
                      </tr>
                      <tr>
                        <td style="padding:16px 28px 24px;border-top:1px solid #E6E8EC;">
                          <p style="margin:0;font-size:12px;line-height:18px;color:{Muted};">Bu e-posta MB FIT tarafından otomatik gönderildi; lütfen yanıtlama.</p>
                        </td>
                      </tr>
                    </table>
                  </td>
                </tr>
              </table>
            </body>
            </html>
            """;
    }
}
