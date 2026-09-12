using MerfitCustomerApi.Business.Common.Pagination;

namespace MerfitCustomerApi.Business.Dtos.Admin.Audit;

/// <summary>GET /api/admin/audit-logs liste satiri.</summary>
public class AdminAuditLogListItemDto
{
    public long Id { get; set; }
    public long? AdminUserId { get; set; }
    public string? AdminEmail { get; set; }
    public string Action { get; set; } = string.Empty;
    public string Entity { get; set; } = string.Empty;
    public long? EntityId { get; set; }
    public string? IpAddress { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>GET /api/admin/audit-logs/{id} detay.</summary>
public class AdminAuditLogDetailDto : AdminAuditLogListItemDto
{
    public string? UserAgent { get; set; }

    /// <summary>Degisiklik oncesi deger (varsa), JSON string olarak.</summary>
    public string? OldValueJson { get; set; }

    /// <summary>Degisiklik sonrasi deger (varsa), JSON string olarak.</summary>
    public string? NewValueJson { get; set; }
}

/// <summary>GET /api/admin/audit-logs sorgu parametreleri.</summary>
public class AdminAuditLogListRequest : PagedRequest
{
    public long? AdminUserId { get; set; }
    public string? Action { get; set; }
    public string? Entity { get; set; }
    public long? EntityId { get; set; }
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
}
