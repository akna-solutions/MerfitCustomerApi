using System.ComponentModel.DataAnnotations;
using MerfitCustomerApi.Business.Common.Pagination;

namespace MerfitCustomerApi.Business.Dtos.Admin.Rewards;

public class AdminLeaderboardRewardDto
{
    public long Id { get; set; }
    public long LeaderboardPeriodId { get; set; }
    public int Rank { get; set; }
    public long RewardId { get; set; }
    public string RewardTitle { get; set; } = string.Empty;
}

public class AdminLeaderboardRewardListRequest : PagedRequest
{
    public long? LeaderboardPeriodId { get; set; }
}

public class AdminUpsertLeaderboardRewardRequest
{
    [Required(ErrorMessage = "LeaderboardPeriodId zorunludur.")]
    public long LeaderboardPeriodId { get; set; }

    [Range(1, 100000, ErrorMessage = "Rank 1 veya daha buyuk olmalidir.")]
    public int Rank { get; set; }

    [Required(ErrorMessage = "RewardId zorunludur.")]
    public long RewardId { get; set; }
}
