using System.ComponentModel.DataAnnotations;
using MerfitCustomerApi.Business.Common.Pagination;
using MerfitCustomerApi.Domain.Entities.Enums;

namespace MerfitCustomerApi.Business.Dtos.Admin.Leaderboards;

public class AdminLeaderboardPeriodDto
{
    public long Id { get; set; }
    public string Type { get; set; } = string.Empty;
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class AdminLeaderboardPeriodListRequest : PagedRequest
{
    public LeaderboardPeriodType? Type { get; set; }
    public bool? IsActive { get; set; }
}

public class AdminUpsertLeaderboardPeriodRequest
{
    [Required(ErrorMessage = "Donem turu zorunludur.")]
    public LeaderboardPeriodType Type { get; set; }

    [Required(ErrorMessage = "Baslangic tarihi zorunludur.")]
    public DateTime StartDate { get; set; }

    [Required(ErrorMessage = "Bitis tarihi zorunludur.")]
    public DateTime EndDate { get; set; }

    public bool IsActive { get; set; } = true;
}

public class AdminLeaderboardEntryDto
{
    public long UserId { get; set; }
    public string UserEmail { get; set; } = string.Empty;
    public decimal Points { get; set; }
    public int WorkoutCount { get; set; }
    public int Rank { get; set; }
}

public class AdminLeaderboardRecalculateResultDto
{
    public long LeaderboardPeriodId { get; set; }
    public int EntryCount { get; set; }
    public DateTime RecalculatedAt { get; set; }
}
