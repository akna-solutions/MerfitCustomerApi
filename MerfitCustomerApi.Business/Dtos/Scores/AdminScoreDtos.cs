using MerfitCustomerApi.Business.Common.Pagination;

namespace MerfitCustomerApi.Business.Dtos.Admin.Scores;

public class AdminScoreListItemDto
{
    public long UserId { get; set; }
    public string UserEmail { get; set; } = string.Empty;
    public decimal Score { get; set; }
    public string? Period { get; set; }
    public DateTime? CalculatedAt { get; set; }
}

public class AdminScoreListRequest : PagedRequest
{
}

public class AdminRecalculateScoreResultDto
{
    public long UserId { get; set; }
    public decimal PreviousScore { get; set; }
    public decimal NewScore { get; set; }
    public DateTime CalculatedAt { get; set; }
}
