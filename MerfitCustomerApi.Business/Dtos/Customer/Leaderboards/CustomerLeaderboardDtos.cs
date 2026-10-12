namespace MerfitCustomerApi.Business.Dtos.Customer.Leaderboard;

/// <summary>
/// MerfitNativeApp LeaderboardScreen'in ihtiyac duydugu tum verileri tek cagrida saglayan response.
/// </summary>
public class CustomerLeaderboardResponse
{
    public bool HasScoreData { get; set; }

    public CustomerCurrentUserSummaryDto CurrentUser { get; set; } = new();

    /// <summary>Secili donemde puana gore siralanmis ilk 10 kullanici.</summary>
    public List<CustomerLeaderboardEntryDto> TopEntries { get; set; } = new();

    /// <summary>Cagiran kullanicinin etrafindaki (en fazla 3 ustu + kendisi + 2 alti) kullanicilar.</summary>
    public List<CustomerLeaderboardEntryDto> NearbyEntries { get; set; } = new();

    public int TotalRankedUsers { get; set; }
    public int TopPercent { get; set; }

    public List<CustomerScoreBreakdownItemDto> ScoreBreakdown { get; set; } = new();
    public List<CustomerScoreHistoryPointDto> ScoreHistory { get; set; } = new();
    public List<CustomerAchievementDto> Achievements { get; set; } = new();

    public List<CustomerRewardDto> Rewards { get; set; } = new();

    /// <summary>Secili donem icin bugunu kapsayan aktif bir LeaderboardPeriod ya da ona bagli odul yoksa null (mobil taraf odul bolumunu gizler).</summary>
    public DateTime? RewardsResetAt { get; set; }

    /// <summary>Kullanicinin kazandigi hediyeler (UserReward), en yeniden eskiye.</summary>
    public List<CustomerEarnedRewardDto> EarnedRewards { get; set; } = new();
}

public class CustomerCurrentUserSummaryDto
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int Points { get; set; }

    /// <summary>Secili donemdeki sira; donemde hic puani yoksa null.</summary>
    public int? Rank { get; set; }

    public int WeeklyChange { get; set; }
    public int BestRank { get; set; }
    public string BestRankMonthLabel { get; set; } = string.Empty;
    public string League { get; set; } = string.Empty;
    public bool LeaderboardVisible { get; set; }
}

public class CustomerLeaderboardEntryDto
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int Points { get; set; }
    public int Workouts { get; set; }
    public int Rank { get; set; }
    public bool IsCurrentUser { get; set; }
}

public class CustomerScoreBreakdownItemDto
{
    public string Id { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public int Points { get; set; }
}

public class CustomerScoreHistoryPointDto
{
    public string Label { get; set; } = string.Empty;
    public int Points { get; set; }
}

/// <summary>"streak" | "workouts" | "pr" | "weekly" (Achievement.Icon degeriyle beslenir).</summary>
public class CustomerAchievementDto
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Icon { get; set; } = string.Empty;
    public bool Earned { get; set; }
}

/// <summary>"watch" | "premium" | "bag" | "shoes" | "training" | "shaker" | "apparel" | "membership" (Reward.RewardType degeriyle beslenir).</summary>
public class CustomerRewardDto
{
    public int Rank { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Icon { get; set; } = string.Empty;
    public string? ImageUrl { get; set; }
    public string? Value { get; set; }
}

/// <summary>Kullanicinin kazandigi bir hediye. Status: "pending" | "claimed" | "expired".</summary>
public class CustomerEarnedRewardDto
{
    public long Id { get; set; }
    public int? Rank { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Icon { get; set; } = string.Empty;
    public string? ImageUrl { get; set; }
    public string? Value { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime EarnedAt { get; set; }
    public DateTime? ClaimedAt { get; set; }
}
