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
    /// Google ile giris. Mobil uygulamanin Google Sign-In'den aldigi ID token sunucuda dogrulanir. Google hesabi bir
    /// MB FIT hesabina bagliysa veya ayni (Google tarafindan dogrulanmis) e-postayla kayitli bir hesap varsa token
    /// cifti doner (auth). Hesap yoksa registrationRequired=true ve onboarding'i on dolduracak profil bilgileri doner.
    /// Gecersiz token: 401; Google yapilandirilmamis: 503.
    /// </summary>
    [HttpPost("google")]
    [AllowAnonymous]
    public async Task<ActionResult<GoogleSignInResponse>> GoogleSignIn([FromBody] GoogleSignInRequest request, CancellationToken cancellationToken)
    {
        var result = await _authService.GoogleSignInAsync(request, ClientIp, cancellationToken);
        return Ok(result);
    }

    /// <summary>
    /// Google ile kayit (onboarding'in son adimi): onboarding alanlari + ID token ile parolasiz hesap olusturur ve
    /// token cifti doner (201). Hesap bu arada olusmus/baglanabilir durumdaysa yeni hesap acmadan giris yapar (200).
    /// </summary>
    [HttpPost("google/register")]
    [AllowAnonymous]
    public async Task<ActionResult<AuthResponse>> GoogleRegister([FromBody] GoogleRegisterRequest request, CancellationToken cancellationToken)
    {
        var result = await _authService.GoogleRegisterAsync(request, ClientIp, cancellationToken);
        return result.IsNewUser ? StatusCode(StatusCodes.Status201Created, result) : Ok(result);
    }

    /// <summary>
    /// Kayit ekraninda kullanici adinin anlik kontrolu: bicim gecerli mi ve baska bir hesapta kullaniliyor mu.
    /// Her zaman 200 doner; sonuc yanittaki available ve message alanlarindadir.
    /// </summary>
    [HttpGet("username-availability")]
    [AllowAnonymous]
    public async Task<ActionResult<UsernameAvailabilityResponse>> CheckUsernameAvailability([FromQuery] string? username, CancellationToken cancellationToken)
    {
        var result = await _authService.CheckUsernameAvailabilityAsync(username, cancellationToken);
        return Ok(result);
    }

    /// <summary>
    /// Giris yapmis kullaniciya yeni bir 6 haneli e-posta dogrulama kodu gonderir. E-posta zaten dogrulanmissa
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
    /// Giris yapmis kullanicinin e-postasina gelen 6 haneli kodu kontrol eder ve e-postayi dogrulanmis isaretler.
    /// Kod tek kullanimliktir ve en fazla AccountEmail:MaxCodeAttempts kez denenebilir; hatali, suresi dolmus veya
    /// kullanilmis kodda 400 doner.
    /// </summary>
    [HttpPost("email-verification/confirm")]
    [Authorize]
    public async Task<ActionResult<ConfirmEmailResponse>> ConfirmEmail([FromBody] ConfirmEmailRequest request, CancellationToken cancellationToken)
    {
        var userId = User.GetUserId()!.Value;
        var result = await _accountEmailService.ConfirmEmailAsync(userId, request.Code, cancellationToken);
        return Ok(result);
    }

    /// <summary>
    /// Sifre sifirlama kodu ister. Hesap olsun olmasin her zaman ayni genel yaniti doner.
    /// </summary>
    [HttpPost("password/forgot")]
    [AllowAnonymous]
    public async Task<ActionResult<AccountActionResponse>> ForgotPassword([FromBody] ForgotPasswordRequest request, CancellationToken cancellationToken)
    {
        var result = await _accountEmailService.RequestPasswordResetAsync(request.Email, ClientIp, cancellationToken);
        return Ok(result);
    }

    /// <summary>
    /// Sifre sifirlama kodunu kullanmadan kontrol eder (uygulama yeni sifre adimina gecmeden once).
    /// Bir deneme hakki harcar; hatali kodda 400 doner.
    /// </summary>
    [HttpPost("password/reset/verify-code")]
    [AllowAnonymous]
    public async Task<ActionResult<AccountActionResponse>> VerifyPasswordResetCode([FromBody] VerifyPasswordResetCodeRequest request, CancellationToken cancellationToken)
    {
        await _accountEmailService.VerifyPasswordResetCodeAsync(request.Email, request.Code, cancellationToken);
        return Ok(new AccountActionResponse { Message = "Kod doğrulandı." });
    }

    /// <summary>
    /// E-posta + kod ile yeni sifreyi kaydeder. Kod tek kullanimliktir; basarili olunca kullanicinin tum
    /// oturumlari (refresh token'lar) iptal edilir ve kullanici yeni sifresiyle giris yapar.
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
