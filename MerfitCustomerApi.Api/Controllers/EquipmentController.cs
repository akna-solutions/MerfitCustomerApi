using MerfitCustomerApi.Business.Dtos.Customer.Equipment;
using MerfitCustomerApi.Business.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MerfitCustomerApi.Api.Controllers;

/// <summary>
/// Sisteme tanimli ekipmanlarin listesini doner. MerfitNativeApp onboarding akisindaki
/// EquipmentStep, kullaniciya secim yaptirmadan once bu listeyi (slug + isim) cekip
/// gosterir; secilen ogelerin Id'leri register isteginde EquipmentIds olarak geri gonderilir.
/// Onboarding, kullanici henuz hesap olusturmadan calistigindan bu uc nokta anonim erisime acik.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[AllowAnonymous]
public class EquipmentController : ControllerBase
{
    private readonly IEquipmentService _equipmentService;

    public EquipmentController(IEquipmentService equipmentService)
    {
        _equipmentService = equipmentService;
    }

    /// <summary>GET /api/equipment - tum ekipmanlari isme gore siralanmis olarak doner.</summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<CustomerEquipmentListItemDto>>> GetAll(CancellationToken cancellationToken)
    {
        var result = await _equipmentService.GetAllAsync(cancellationToken);
        return Ok(result);
    }
}