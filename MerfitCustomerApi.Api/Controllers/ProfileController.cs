using MerfitCustomerApi.Api.Extensions;
using MerfitCustomerApi.Business.Dtos.Auth;
using MerfitCustomerApi.Business.Dtos.Customer.Profile;
using MerfitCustomerApi.Business.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MerfitCustomerApi.Api.Controllers;

/// <summary>
/// MerfitNativeApp'in ProfileContext'i icin tek GET/PUT ucu. Uygulamadaki tum Profile alt
/// ekranlari (Goals, Body Measurements, Notifications, Privacy, Units, Appearance, ...) bu
/// tek kaynaktan okuyup kismi (partial) guncelleme yapar.
/// </summary>
[ApiController]
[Route("api/profile")]
[Authorize]
public class ProfileController : ControllerBase
{
    private readonly IProfileService _profileService;
    private readonly IFitnessProfileService _fitnessProfileService;

    public ProfileController(IProfileService profileService, IFitnessProfileService fitnessProfileService)
    {
        _profileService = profileService;
        _fitnessProfileService = fitnessProfileService;
    }

    [HttpGet]
    public async Task<ActionResult<CustomerProfileResponse>> GetProfile(CancellationToken cancellationToken)
    {
        var userId = User.GetUserId()!.Value;
        var result = await _profileService.GetProfileAsync(userId, cancellationToken);
        return Ok(result);
    }

    /// <summary>Yalnizca gonderilen alanlari gunceller; guncellenmis tam profili dondurur.</summary>
    [HttpPut]
    public async Task<ActionResult<CustomerProfileResponse>> UpdateProfile(
        [FromBody] UpdateProfileRequest request,
        CancellationToken cancellationToken)
    {
        var userId = User.GetUserId()!.Value;
        var result = await _profileService.UpdateProfileAsync(userId, request, cancellationToken);
        return Ok(result);
    }

    /// <summary>
    /// Yalnizca alisveris icin kayit olmus kullanicinin fitness profilini (onboarding'in boy/kilo/hedef/antrenman
    /// adimlari) tamamlar ve guncel profili dondurur. Profil zaten tamamsa 409; boy veya kilo eksikse 400. Yag Kaybi
    /// hedefinde dusuk yag orani onayi gerekiyorsa kayittaki gibi 409 LOW_BODY_FAT_CONFIRMATION_REQUIRED.
    /// </summary>
    [HttpPost("fitness")]
    public async Task<ActionResult<CustomerProfileResponse>> CompleteFitnessProfile(
        [FromBody] FitnessProfileRequest request,
        CancellationToken cancellationToken)
    {
        var userId = User.GetUserId()!.Value;
        await _fitnessProfileService.CompleteAsync(userId, request, cancellationToken);
        var result = await _profileService.GetProfileAsync(userId, cancellationToken);
        return Ok(result);
    }

    /// <summary>Kullanicinin theme tercihini doner (kayit yoksa varsayilan "System").</summary>
    [HttpGet("theme")]
    public async Task<ActionResult<ThemePreferenceResponse>> GetThemePreference(CancellationToken cancellationToken)
    {
        var userId = User.GetUserId()!.Value;
        var result = await _profileService.GetThemePreferenceAsync(userId, cancellationToken);
        return Ok(result);
    }

    /// <summary>Kullanicinin theme tercihini gunceller.</summary>
    [HttpPut("theme")]
    public async Task<ActionResult<ThemePreferenceResponse>> UpdateThemePreference(
        [FromBody] UpdateThemePreferenceRequest request,
        CancellationToken cancellationToken)
    {
        var userId = User.GetUserId()!.Value;
        var result = await _profileService.UpdateThemePreferenceAsync(userId, request, cancellationToken);
        return Ok(result);
    }
}
