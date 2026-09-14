using MerfitCustomerApi.Api.Extensions;
using MerfitCustomerApi.Business.Dtos.Customer.Personalization;
using MerfitCustomerApi.Business.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MerfitCustomerApi.Api.Controllers;

/// <summary>
/// Kayit sirasinda olusturulan PersonalizationJob'un durumunu mobil uygulamaya raporlayan
/// uc nokta. MerfitNativeApp, register sonrasi dashboard'da bu durumu kontrollu araliklarla
/// (polling) sorgulayip "Programın hazırlanıyor" -> "Programın hazır!" gecisini yonetir.
/// </summary>
[ApiController]
[Route("api/personalization")]
[Authorize]
public class PersonalizationController : ControllerBase
{
    private readonly IPersonalizationStatusService _personalizationStatusService;

    public PersonalizationController(IPersonalizationStatusService personalizationStatusService)
    {
        _personalizationStatusService = personalizationStatusService;
    }

    /// <summary>Giris yapmis kullanicinin PersonalizationJob durumunu dondurur.</summary>
    [HttpGet("status")]
    public async Task<ActionResult<PersonalizationStatusDto>> GetStatus(CancellationToken cancellationToken)
    {
        var userId = User.GetUserId()!.Value;
        var result = await _personalizationStatusService.GetStatusAsync(userId, cancellationToken);
        return Ok(result);
    }
}
