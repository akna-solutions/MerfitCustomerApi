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

    public NotificationsController(INotificationService notificationService)
    {
        _notificationService = notificationService;
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
}
