using MerfitCustomerApi.Api.Extensions;
using MerfitCustomerApi.Business.Common.Pagination;
using MerfitCustomerApi.Business.Dtos.Customer.Notifications;
using MerfitCustomerApi.Business.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MerfitCustomerApi.Api.Controllers;

/// <summary>Giris yapmis kullanicinin bildirimleri ve bildirim turu ayarlari.</summary>
[ApiController]
[Route("api/notifications")]
[Authorize]
public class NotificationsController : ControllerBase
{
    private readonly INotificationService _notificationService;
    private readonly INotificationDeviceService _deviceService;

    public NotificationsController(INotificationService notificationService, INotificationDeviceService deviceService)
    {
        _notificationService = notificationService;
        _deviceService = deviceService;
    }

    /// <summary>Sorgu: /api/notifications?category=nutrition&amp;page=1&amp;pageSize=20 (en yeni once).</summary>
    [HttpGet]
    public async Task<ActionResult<PagedResult<CustomerNotificationDto>>> GetNotifications(
        [FromQuery] CustomerNotificationListRequest request,
        CancellationToken cancellationToken)
    {
        var userId = User.GetUserId()!.Value;
        return Ok(await _notificationService.GetNotificationsAsync(userId, request, cancellationToken));
    }

    [HttpGet("unread-count")]
    public async Task<ActionResult<CustomerNotificationUnreadCountResponse>> GetUnreadCount(
        [FromQuery] string? category,
        CancellationToken cancellationToken)
    {
        var userId = User.GetUserId()!.Value;
        return Ok(await _notificationService.GetUnreadCountAsync(userId, category, cancellationToken));
    }

    [HttpPost("{id:long}/read")]
    public async Task<IActionResult> MarkAsRead(long id, CancellationToken cancellationToken)
    {
        var userId = User.GetUserId()!.Value;
        await _notificationService.MarkAsReadAsync(userId, id, cancellationToken);
        return NoContent();
    }

    /// <summary>Sorgu: /api/notifications/read-all?category=nutrition (bos ise tum bildirimler).</summary>
    [HttpPost("read-all")]
    public async Task<IActionResult> MarkAllAsRead([FromQuery] string? category, CancellationToken cancellationToken)
    {
        var userId = User.GetUserId()!.Value;
        await _notificationService.MarkAllAsReadAsync(userId, category, cancellationToken);
        return NoContent();
    }

    /// <summary>Backend'deki her NotificationType icin kullanicinin ayari.</summary>
    [HttpGet("settings")]
    public async Task<ActionResult<List<CustomerNotificationTypeSettingDto>>> GetSettings(CancellationToken cancellationToken)
    {
        var userId = User.GetUserId()!.Value;
        return Ok(await _notificationService.GetSettingsAsync(userId, cancellationToken));
    }

    [HttpPut("settings")]
    public async Task<ActionResult<List<CustomerNotificationTypeSettingDto>>> UpdateSettings(
        [FromBody] UpdateNotificationTypeSettingsRequest request,
        CancellationToken cancellationToken)
    {
        var userId = User.GetUserId()!.Value;
        return Ok(await _notificationService.UpdateSettingsAsync(userId, request, cancellationToken));
    }

    /// <summary>Push genel anahtari, sessiz saatler ve saat dilimi.</summary>
    [HttpGet("preferences")]
    public async Task<ActionResult<CustomerNotificationPreferencesDto>> GetPreferences(CancellationToken cancellationToken)
    {
        var userId = User.GetUserId()!.Value;
        return Ok(await _notificationService.GetPreferencesAsync(userId, cancellationToken));
    }

    [HttpPut("preferences")]
    public async Task<ActionResult<CustomerNotificationPreferencesDto>> UpdatePreferences(
        [FromBody] UpdateNotificationPreferencesRequest request,
        CancellationToken cancellationToken)
    {
        var userId = User.GetUserId()!.Value;
        return Ok(await _notificationService.UpdatePreferencesAsync(userId, request, cancellationToken));
    }

    /// <summary>
    /// Push token'ini giris yapmis kullaniciya kaydeder veya gunceller (idempotent). Ayni token baska bir
    /// hesaba kayitliysa bu hesaba tasinir.
    /// </summary>
    [HttpPut("devices")]
    public async Task<ActionResult<CustomerDeviceRegistrationDto>> RegisterDevice(
        [FromBody] RegisterDeviceRequest request,
        CancellationToken cancellationToken)
    {
        var userId = User.GetUserId()!.Value;
        return Ok(await _deviceService.RegisterAsync(userId, request, cancellationToken));
    }

    /// <summary>Cikis yapilirken veya bildirim izni geri alindiginda token'i pasiflestirir. Token govdede gonderilir (URL'de loglanmaz).</summary>
    [HttpPost("devices/deactivate")]
    public async Task<IActionResult> DeactivateDevice([FromBody] DeactivateDeviceRequest request, CancellationToken cancellationToken)
    {
        var userId = User.GetUserId()!.Value;
        await _deviceService.DeactivateAsync(userId, request, cancellationToken);
        return NoContent();
    }
}
