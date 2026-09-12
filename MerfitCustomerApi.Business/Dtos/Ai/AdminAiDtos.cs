using MerfitCustomerApi.Business.Common.Pagination;
using MerfitCustomerApi.Domain.Entities.Enums;

namespace MerfitCustomerApi.Business.Dtos.Admin.Ai;

public class AdminAiRequestListItemDto
{
    public long Id { get; set; }
    public long UserId { get; set; }
    public string UserEmail { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? Model { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class AdminAiRequestDetailDto : AdminAiRequestListItemDto
{
    public string Prompt { get; set; } = string.Empty;
    public bool HasResult { get; set; }
}

public class AdminAiRequestListRequest : PagedRequest
{
    public long? UserId { get; set; }
    public AiRequestType? Type { get; set; }
    public AiGenerationStatus? Status { get; set; }
    public string? Model { get; set; }
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
}

public class AdminAiResultListItemDto
{
    public long Id { get; set; }
    public long RequestId { get; set; }
    public long UserId { get; set; }
    public string UserEmail { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}

public class AdminAiResultDetailDto : AdminAiResultListItemDto
{
    public string ResultJson { get; set; } = string.Empty;
}

public class AdminAiResultListRequest : PagedRequest
{
    public long? RequestId { get; set; }
}

public class AdminAiStatisticsDto
{
    public long TotalRequests { get; set; }
    public long SuccessfulRequests { get; set; }
    public long FailedRequests { get; set; }
    public long PendingRequests { get; set; }

    /// <summary>StartedAt ve CompletedAt'i dolu olan (tamamlanmis) isteklerin ortalama sureleri (saniye).</summary>
    public double AverageDurationSeconds { get; set; }

    public List<AdminAiDistributionItemDto> ModelDistribution { get; set; } = new();
    public List<AdminAiDistributionItemDto> RequestTypeDistribution { get; set; } = new();
}

public class AdminAiDistributionItemDto
{
    public string Key { get; set; } = string.Empty;
    public long Count { get; set; }
}
