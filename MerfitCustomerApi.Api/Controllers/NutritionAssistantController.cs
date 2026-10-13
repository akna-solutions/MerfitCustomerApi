using MerfitCustomerApi.Api.Extensions;
using MerfitCustomerApi.Business.Dtos.Customer.NutritionAssistant;
using MerfitCustomerApi.Business.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MerfitCustomerApi.Api.Controllers;

/// <summary>
/// MerfitNativeApp beslenme ekranindaki "Mbfit Beslenme Asistani" sohbet penceresi. Kullanici kimligi
/// yalnizca JWT'den alinir; istemcinin gonderdigi sohbet kimligi servis tarafinda sahiplik icin dogrulanir.
/// </summary>
[ApiController]
[Route("api/nutrition-assistant")]
[Authorize]
public class NutritionAssistantController : ControllerBase
{
    /// <summary>Sohbet istek govdesinin ust siniri (mesaj 1000 karakter; JSON ve Turkce karakterler icin pay).</summary>
    public const long MaxChatRequestBytes = 16 * 1024;

    private readonly INutritionAssistantService _assistantService;

    public NutritionAssistantController(INutritionAssistantService assistantService)
    {
        _assistantService = assistantService;
    }

    /// <summary>
    /// Secili gunun sohbetini, gercek verilerle hazirlanan acilis degerlendirmesini ve hizli sorulari dondurur.
    /// Sorgu: /api/nutrition-assistant/conversation?date=2026-10-13
    /// </summary>
    [HttpGet("conversation")]
    public async Task<ActionResult<NutritionAssistantConversationDto>> GetConversation(
        [FromQuery] DateOnly? date,
        CancellationToken cancellationToken)
    {
        var userId = User.GetUserId()!.Value;
        var result = await _assistantService.GetConversationAsync(userId, date, cancellationToken);
        return Ok(result);
    }

    /// <summary>
    /// Kullanici mesajini asistana iletir ve yaniti dondurur. Kullanici ve IP basina dakikalik hiz siniri
    /// uygulanir (Program.cs, "NutritionAssistant" ayarlari); asilirsa 429 + Retry-After doner.
    /// </summary>
    [HttpPost("chat")]
    [RequestSizeLimit(MaxChatRequestBytes)]
    public async Task<ActionResult<NutritionAssistantChatResponse>> Chat(
        [FromBody] NutritionAssistantChatRequest request,
        CancellationToken cancellationToken)
    {
        var userId = User.GetUserId()!.Value;
        var result = await _assistantService.SendMessageAsync(userId, request, cancellationToken);
        return Ok(result);
    }

    /// <summary>
    /// Kullanicinin secili gundeki sohbetini ve tum mesajlarini siler (yalnizca kendi sohbeti).
    /// Sorgu: /api/nutrition-assistant/conversation?date=2026-10-13
    /// </summary>
    [HttpDelete("conversation")]
    public async Task<IActionResult> DeleteConversation(
        [FromQuery] DateOnly? date,
        CancellationToken cancellationToken)
    {
        var userId = User.GetUserId()!.Value;
        await _assistantService.DeleteConversationAsync(userId, date, cancellationToken);
        return NoContent();
    }
}
