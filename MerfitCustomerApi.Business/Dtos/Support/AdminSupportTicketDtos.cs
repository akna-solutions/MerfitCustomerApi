using System.ComponentModel.DataAnnotations;
using MerfitCustomerApi.Business.Common.Pagination;
using MerfitCustomerApi.Domain.Entities.Enums;

namespace MerfitCustomerApi.Business.Dtos.Admin.Support;

public class AdminSupportTicketListItemDto
{
    public long Id { get; set; }
    public long UserId { get; set; }
    public string UserEmail { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string Priority { get; set; } = string.Empty;
    public int MessageCount { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? ClosedAt { get; set; }
}

public class AdminSupportTicketDetailDto : AdminSupportTicketListItemDto
{
    public string Message { get; set; } = string.Empty;
}

public class AdminSupportTicketListRequest : PagedRequest
{
    public SupportTicketStatus? Status { get; set; }
    public SupportTicketPriority? Priority { get; set; }
    public long? UserId { get; set; }
    public DateTime? CreatedFrom { get; set; }
    public DateTime? CreatedTo { get; set; }
}

public class AdminUpdateTicketStatusRequest
{
    [Required(ErrorMessage = "Durum alani zorunludur.")]
    public SupportTicketStatus Status { get; set; }
}

public class AdminUpdateTicketPriorityRequest
{
    [Required(ErrorMessage = "Oncelik alani zorunludur.")]
    public SupportTicketPriority Priority { get; set; }
}

public class AdminSupportTicketMessageDto
{
    public long Id { get; set; }
    public long TicketId { get; set; }
    public long SenderUserId { get; set; }
    public string SenderEmail { get; set; } = string.Empty;

    /// <summary>Mesaji gonderenin talep sahibi kullanici mi yoksa bir admin mi oldugunu belirtir.</summary>
    public bool IsFromAdmin { get; set; }

    public string Message { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}

public class AdminSendTicketMessageRequest
{
    [Required(ErrorMessage = "Mesaj alani zorunludur.")]
    [StringLength(4000, MinimumLength = 1)]
    public string Message { get; set; } = string.Empty;
}
