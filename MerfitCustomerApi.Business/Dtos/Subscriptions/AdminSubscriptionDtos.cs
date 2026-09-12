using System.ComponentModel.DataAnnotations;
using MerfitCustomerApi.Business.Common.Pagination;
using MerfitCustomerApi.Domain.Entities.Enums;

namespace MerfitCustomerApi.Business.Dtos.Admin.Subscriptions;

public class AdminSubscriptionListItemDto
{
    public long Id { get; set; }
    public long UserId { get; set; }
    public string UserEmail { get; set; } = string.Empty;
    public long SubscriptionProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string Provider { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime StartedAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public bool AutoRenew { get; set; }
    public DateTime? CancelledAt { get; set; }
}

public class AdminSubscriptionDetailDto : AdminSubscriptionListItemDto
{
    public string? ExternalTransactionId { get; set; }
}

public class AdminSubscriptionListRequest : PagedRequest
{
    public long? UserId { get; set; }
    public SubscriptionStatus? Status { get; set; }
    public SubscriptionProvider? Provider { get; set; }
    public long? SubscriptionProductId { get; set; }
    public DateTime? StartedFrom { get; set; }
    public DateTime? StartedTo { get; set; }
    public DateTime? ExpiresFrom { get; set; }
    public DateTime? ExpiresTo { get; set; }
}

public class AdminUpdateSubscriptionStatusRequest
{
    [Required(ErrorMessage = "Durum alani zorunludur.")]
    public SubscriptionStatus Status { get; set; }

    [StringLength(500)]
    public string? Reason { get; set; }
}
