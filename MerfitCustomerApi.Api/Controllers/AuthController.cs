using MerfitCustomerApi.Api.Extensions;
using MerfitCustomerApi.Business.Dtos.Auth;
using MerfitCustomerApi.Business.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MerfitCustomerApi.Api.Controllers;

/// <summary>
/// Kimlik dogrulama uc noktalarini (register, login, refresh vb.) barindirir.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _authService;
    private readonly IAccountEmailService _accountEmailService;

    public AuthController(IAuthService authService, IAccountEmailService accountEmailService)
    {
        _authService = authService;
        _accountEmailService = accountEmailService;
    }

    /// <summary>
    /// Yeni bir MERFIT hesabi olusturur (MerfitNativeApp onboarding akisinin son adimi).
    /// Basarili oldugunda dogrudan kullanilabilir bir access/refresh token cifti doner,
    /// boylece mobil uygulama kayittan sonra ayrica login yapmadan dashboard'a gecebilir.
    /// </summary>
    [HttpPost("register")]
    public async Task<ActionResult<AuthResponse>> Register([FromBody] RegisterRequest request)
    {
        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
        var result = await _authService.RegisterAsync(request, ipAddress);

        return StatusCode(StatusCodes.Status201Created, result);
    }

    /// <summary>
    /// Var olan bir MERFIT hesabi ile e-posta veya kullanici adi ve parola kullanarak giris yapar ve
    /// dogrudan kullanilabilir bir access/refresh token cifti doner.
    /// </summary>
    [HttpPost("login")]
    public async Task<ActionResult<AuthResponse>> Login([FromBody] LoginRequest request)
    {
        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
        var result = await _authService.LoginAsync(request, ipAddress);

        return Ok(result);
    }

    /// <summary>
    /// Giris yapmis kullaniciya yeni bir e-posta dogrulama baglantisi gonderir. E-posta zaten dogrulanmissa
    /// gondermez (alreadyVerified=true). Bekleme suresi dolmadiysa 429 + Retry-After; e-posta gonderilemezse 503.
    /// </summary>
    [HttpPost("email-verification/resend")]
    [Authorize]
    public async Task<ActionResult<ResendVerificationEmailResponse>> ResendVerificationEmail(CancellationToken cancellationToken)
    {
        var userId = User.GetUserId()!.Value;
        var result = await _accountEmailService.ResendVerificationEmailAsync(userId, ClientIp, cancellationToken);
        return Ok(result);
    }

    /// <summary>
    /// E-postadaki dogrulama jetonunu kullanir ve e-postayi dogrulanmis isaretler. Jeton tek kullanimliktir;
    /// gecersiz, suresi dolmus veya kullanilmis jetonda 400 doner.
    /// </summary>
    [HttpPost("email-verification/confirm")]
    [AllowAnonymous]
    public async Task<ActionResult<ConfirmEmailResponse>> ConfirmEmail([FromBody] ConfirmEmailRequest request, CancellationToken cancellationToken)
    {
        var result = await _accountEmailService.ConfirmEmailAsync(request.Token, cancellationToken);
        return Ok(result);
    }

    /// <summary>
    /// Sifre sifirlama baglantisi ister. Hesap olsun olmasin her zaman ayni genel yaniti doner.
    /// </summary>
    [HttpPost("password/forgot")]
    [AllowAnonymous]
    public async Task<ActionResult<AccountActionResponse>> ForgotPassword([FromBody] ForgotPasswordRequest request, CancellationToken cancellationToken)
    {
        var result = await _accountEmailService.RequestPasswordResetAsync(request.Email, ClientIp, cancellationToken);
        return Ok(result);
    }

    /// <summary>Sifre sifirlama baglantisinin hala gecerli oldugunu kontrol eder (jetonu kullanmaz).</summary>
    [HttpPost("password/reset/validate")]
    [AllowAnonymous]
    public async Task<ActionResult<AccountActionResponse>> ValidatePasswordResetToken([FromBody] ValidatePasswordResetTokenRequest request, CancellationToken cancellationToken)
    {
        await _accountEmailService.ValidatePasswordResetTokenAsync(request.Token, cancellationToken);
        return Ok(new AccountActionResponse { Message = "Bağlantı geçerli." });
    }

    /// <summary>
    /// Jetonla yeni sifreyi kaydeder. Jeton tek kullanimliktir; basarili olunca kullanicinin tum oturumlari
    /// (refresh token'lar) iptal edilir ve kullanici yeni sifresiyle giris yapar.
    /// </summary>
    [HttpPost("password/reset")]
    [AllowAnonymous]
    public async Task<ActionResult<AccountActionResponse>> ResetPassword([FromBody] ResetPasswordRequest request, CancellationToken cancellationToken)
    {
        var result = await _accountEmailService.ResetPasswordAsync(request, ClientIp, cancellationToken);
        return Ok(result);
    }

    private string? ClientIp => HttpContext.Connection.RemoteIpAddress?.ToString();
}
