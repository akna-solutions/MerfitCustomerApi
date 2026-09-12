using MerfitCustomerApi.Business.Dtos.Auth;
using MerfitCustomerApi.Business.Interfaces.Services;
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

    public AuthController(IAuthService authService)
    {
        _authService = authService;
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
}