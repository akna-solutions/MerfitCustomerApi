using System.Text.Json;

namespace MerfitCustomerApi.Api.AccountLinks;

/// <summary>
/// E-postadaki baglantilarin actigi kucuk web sayfalari (/verify-email ve /reset-password).
///
/// - Jeton URL'nin "#" kismindadir; tarayici bunu sunucuya gondermez, sayfa okuduktan hemen sonra adres
///   cubugundan da siler. Boylece jeton sunucu/proxy loglarina ve Referer basligina dusmez.
/// - Sayfa acilisi hicbir sey degistirmez (e-posta tarayicilarinin onizlemesi jetonu tuketemez);
///   dogrulama ve sifre degisikligi yalnizca kullanici butona bastiginda API'ye POST ile yapilir.
/// - Sayfa uygulamayi deep link ile acmayi da sunar; uygulama yuklu degilse islem tarayicida tamamlanir.
/// - Sunucudan gelen mesajlar textContent ile yazilir (HTML olarak yorumlanmaz); CSP yalnizca bu sayfanin
///   nonce'lu script/style'ina ve ayni kaynaga (API) istege izin verir.
/// </summary>
public static class AccountLinkPage
{
    public enum Kind
    {
        VerifyEmail,
        ResetPassword,
    }

    public static string Render(Kind kind, string appScheme, string nonce)
    {
        var config = JsonSerializer.Serialize(new
        {
            kind = kind == Kind.VerifyEmail ? "verify" : "reset",
            scheme = appScheme,
        });
        var title = kind == Kind.VerifyEmail ? "E-posta doğrulama" : "Şifre sıfırlama";

        return $$"""
            <!DOCTYPE html>
            <html lang="tr">
            <head>
              <meta charset="utf-8">
              <meta name="viewport" content="width=device-width, initial-scale=1">
              <meta name="robots" content="noindex, nofollow">
              <meta name="referrer" content="no-referrer">
              <title>MB FIT · {{title}}</title>
              <style nonce="{{nonce}}">
                :root { --bg:#050507; --card:#0F1116; --text:#FFFFFF; --muted:rgba(255,255,255,0.66); --border:rgba(255,255,255,0.16);
                        --blue:#3E7BFA; --blue-pressed:#1D51C0; --danger:#FF6B6B; --input:rgba(255,255,255,0.06); }
                @media (prefers-color-scheme: light) {
                  :root { --bg:#F5F6F8; --card:#FFFFFF; --text:#0B0D12; --muted:rgba(11,13,18,0.62); --border:rgba(11,13,18,0.14);
                          --blue:#1D51C0; --blue-pressed:#153E94; --danger:#DC2626; --input:rgba(11,13,18,0.04); }
                }
                * { box-sizing:border-box; }
                body { margin:0; min-height:100vh; background:var(--bg); color:var(--text);
                       font-family:-apple-system,BlinkMacSystemFont,"Segoe UI",Roboto,Helvetica,Arial,sans-serif;
                       display:flex; align-items:center; justify-content:center; padding:24px 16px; }
                main { width:100%; max-width:420px; background:var(--card); border:1px solid var(--border); border-radius:20px; padding:28px 24px; }
                .brand { font-size:18px; font-weight:800; letter-spacing:1px; margin:0 0 24px; color:var(--text); }
                .brand span { color:var(--blue); }
                h1 { font-size:24px; line-height:30px; margin:0 0 10px; }
                p { font-size:15px; line-height:22px; color:var(--muted); margin:0 0 20px; }
                .btn { display:block; width:100%; height:52px; border:0; border-radius:14px; font-size:15px; font-weight:700;
                       text-align:center; line-height:52px; text-decoration:none; cursor:pointer; margin-top:12px; }
                .btn-primary { background:var(--blue-pressed); color:#FFFFFF; }
                .btn-primary:disabled { opacity:0.5; cursor:default; }
                .btn-secondary { background:transparent; color:var(--text); border:1px solid var(--border); line-height:50px; }
                label { display:block; font-size:13px; font-weight:600; margin:14px 0 6px; }
                input { width:100%; height:52px; border-radius:14px; border:1px solid var(--border); background:var(--input);
                        color:var(--text); font-size:16px; padding:0 16px; }
                input:focus, .btn:focus-visible { outline:2px solid var(--blue); outline-offset:2px; }
                .hint { font-size:13px; line-height:18px; margin:8px 0 0; }
                .error { color:var(--danger); font-size:14px; font-weight:600; margin:14px 0 0; min-height:1px; }
                [hidden] { display:none !important; }
              </style>
            </head>
            <body>
              <main aria-live="polite">
                <p class="brand">MB <span>FIT</span></p>
                <h1 id="title">{{title}}</h1>
                <p id="message">Bağlantı kontrol ediliyor…</p>

                <div id="verify-actions" hidden>
                  <button id="verify-button" class="btn btn-primary" type="button">E-postamı doğrula</button>
                </div>

                <form id="reset-form" method="post" action="#" hidden novalidate>
                  <label for="new-password">Yeni şifre</label>
                  <input id="new-password" name="new-password" type="password" autocomplete="new-password" minlength="6" maxlength="100" required>
                  <label for="confirm-password">Yeni şifre (tekrar)</label>
                  <input id="confirm-password" name="confirm-password" type="password" autocomplete="new-password" minlength="6" maxlength="100" required>
                  <p class="hint">En az 6 karakter olmalı. Başka hesaplarında kullanmadığın bir şifre seç.</p>
                  <button id="reset-button" class="btn btn-primary" type="submit">Şifremi güncelle</button>
                </form>

                <p id="error" class="error" role="alert"></p>
                <a id="app-link" class="btn btn-secondary" href="#" hidden>MB FIT uygulamasında aç</a>
              </main>
              <script nonce="{{nonce}}">
                (function () {
                  var cfg = {{config}};
                  var params = new URLSearchParams(window.location.hash.slice(1));
                  var token = params.get("token");
                  // Jeton adres cubugundan ve tarayici gecmisinden silinir.
                  if (window.history && window.history.replaceState) {
                    window.history.replaceState(null, "", window.location.pathname);
                  }

                  var $ = function (id) { return document.getElementById(id); };
                  var appLink = $("app-link");
                  function setText(id, text) { $(id).textContent = text; }
                  function showError(text) { setText("error", text || ""); }
                  function deepLink(path) { return cfg.scheme + "://" + path; }
                  function showAppLink(path, label) {
                    appLink.href = deepLink(path);
                    appLink.textContent = label;
                    appLink.hidden = false;
                  }

                  async function post(url, body) {
                    var response = await fetch(url, {
                      method: "POST",
                      headers: { "Content-Type": "application/json", "Accept": "application/json" },
                      body: JSON.stringify(body),
                      credentials: "omit",
                      cache: "no-store",
                      referrerPolicy: "no-referrer"
                    });
                    var data = {};
                    try { data = await response.json(); } catch (e) { data = {}; }
                    if (!response.ok) {
                      var message = data.errorMessage;
                      if (!message && data.errors) {
                        var values = Array.isArray(data.errors) ? data.errors : Object.values(data.errors).flat();
                        message = values[0];
                      }
                      if (response.status === 429) {
                        message = message || "Çok fazla deneme yaptın. Lütfen biraz bekleyip tekrar dene.";
                      }
                      throw new Error(message || "Bir sorun oluştu. Lütfen tekrar dene.");
                    }
                    return data;
                  }

                  function invalid(text) {
                    setText("title", "Bağlantı kullanılamıyor");
                    setText("message", text);
                    $("verify-actions").hidden = true;
                    $("reset-form").hidden = true;
                    showAppLink(cfg.kind === "verify" ? "pages/auth/verify-email" : "pages/auth/forgot-password",
                      "Uygulamada yeni bağlantı iste");
                  }

                  if (!token) {
                    invalid("Bağlantı eksik veya hatalı. E-postadaki bağlantıyı yeniden açmayı ya da yeni bir bağlantı istemeyi dene.");
                    return;
                  }

                  if (cfg.kind === "verify") {
                    setText("message", "MB FIT hesabının e-posta adresini doğrulamak için butona dokun.");
                    $("verify-actions").hidden = false;
                    showAppLink("pages/auth/verify-email?token=" + encodeURIComponent(token), "MB FIT uygulamasında aç");
                    $("verify-button").addEventListener("click", async function () {
                      var button = $("verify-button");
                      button.disabled = true;
                      button.textContent = "Doğrulanıyor…";
                      showError("");
                      try {
                        await post("/api/auth/email-verification/confirm", { token: token });
                        setText("title", "E-posta adresin doğrulandı");
                        setText("message", "Teşekkürler! Artık MB FIT uygulamasına dönebilirsin.");
                        $("verify-actions").hidden = true;
                        showAppLink("pages/dashboard", "Uygulamaya dön");
                      } catch (error) {
                        invalid(error.message);
                      }
                    });
                    return;
                  }

                  (async function () {
                    try {
                      await post("/api/auth/password/reset/validate", { token: token });
                    } catch (error) {
                      invalid(error.message);
                      return;
                    }
                    setText("message", "Yeni şifreni belirle. Uygulama telefonunda yüklüyse orada da devam edebilirsin.");
                    $("reset-form").hidden = false;
                    showAppLink("pages/auth/reset-password?token=" + encodeURIComponent(token), "MB FIT uygulamasında aç");
                  })();

                  $("reset-form").addEventListener("submit", async function (event) {
                    event.preventDefault();
                    var password = $("new-password").value;
                    var confirmPassword = $("confirm-password").value;
                    if (password.length < 6 || password.length > 100) { showError("Şifre 6-100 karakter arasında olmalı."); return; }
                    if (password !== confirmPassword) { showError("Şifreler eşleşmiyor."); return; }
                    var button = $("reset-button");
                    button.disabled = true;
                    button.textContent = "Kaydediliyor…";
                    showError("");
                    try {
                      var result = await post("/api/auth/password/reset", { token: token, newPassword: password, confirmPassword: confirmPassword });
                      $("reset-form").hidden = true;
                      setText("title", "Şifren güncellendi");
                      setText("message", result.message || "Yeni şifrenle giriş yapabilirsin.");
                      showAppLink("pages/login", "Uygulamada giriş yap");
                    } catch (error) {
                      button.disabled = false;
                      button.textContent = "Şifremi güncelle";
                      showError(error.message);
                    }
                  });
                })();
              </script>
            </body>
            </html>
            """;
    }
}
