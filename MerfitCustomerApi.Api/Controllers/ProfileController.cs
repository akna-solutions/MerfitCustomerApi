using MerfitCustomerApi.Api.Extensions;
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

    public ProfileController(IProfileService profileService)
    {
        _profileService = profileService;
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
}
