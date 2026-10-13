using System.Security.Cryptography;
using MerfitCustomerApi.Api.AccountLinks;
using MerfitCustomerApi.Business.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace MerfitCustomerApi.Api.Controllers;

/// <summary>
/// E-postadaki baglantilarin hedefi: {AccountEmail:PublicBaseUrl}/verify-email ve /reset-password.
/// Sayfa jetonu "#" kismindan okur, uygulamayi acmayi (deep link) veya islemi tarayicida tamamlamayi sunar.
/// GET istegi hicbir durumu degistirmez. Bkz. <see cref="AccountLinkPage"/>.
/// </summary>
[ApiController]
[AllowAnonymous]
[ApiExplorerSettings(IgnoreApi = true)]
public class AccountLinksController : ControllerBase
{
    private readonly AccountEmailOptions _options;

    public AccountLinksController(IOptions<AccountEmailOptions> options)
    {
        _options = options.Value;
    }

    [HttpGet("/verify-email")]
    public ContentResult VerifyEmail() => Page(AccountLinkPage.Kind.VerifyEmail);

    [HttpGet("/reset-password")]
    public ContentResult ResetPassword() => Page(AccountLinkPage.Kind.ResetPassword);

    private ContentResult Page(AccountLinkPage.Kind kind)
    {
        var nonce = Convert.ToBase64String(RandomNumberGenerator.GetBytes(16));
        var headers = Response.Headers;
        headers.ContentSecurityPolicy =
            $"default-src 'none'; script-src 'nonce-{nonce}'; style-src 'nonce-{nonce}'; connect-src 'self'; " +
            "img-src 'self' data:; base-uri 'none'; form-action 'none'; frame-ancestors 'none'";
        headers.CacheControl = "no-store";
        headers["Referrer-Policy"] = "no-referrer";
        headers["X-Content-Type-Options"] = "nosniff";
        headers["X-Frame-Options"] = "DENY";
        headers["X-Robots-Tag"] = "noindex, nofollow";

        return new ContentResult
        {
            Content = AccountLinkPage.Render(kind, _options.AppScheme, nonce),
            ContentType = "text/html; charset=utf-8",
            StatusCode = StatusCodes.Status200OK,
        };
    }
}
