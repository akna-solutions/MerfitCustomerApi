using MerfitCustomerApi.Business.Dtos.Customer.Leaderboard;
using MerfitCustomerApi.Business.Interfaces.Services;
using MerfitCustomerApi.Domain.Entities;
using MerfitCustomerApi.Domain.Entities.Enums;
using MerfitCustomerApi.Domain.Exceptions;
using MerfitCustomerApi.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace MerfitCustomerApi.Business.Services.Leaderboard;

/// <summary>
/// ILeaderboardService'in varsayilan implementasyonu. Mevcut MerfitScore/MerfitScoreBreakdown/
/// MerfitScoreHistory/Achievement/UserAchievement/Reward/LeaderboardReward/LeaderboardPeriod/
/// UserProfile/UserPrivacySetting/WorkoutSession entity'lerini oldugu gibi kullanir.
///
/// NOT: Siralama, LeaderboardEntry tablosundaki onceden hesaplanmis kayitlara degil, MerfitScore
/// uzerinden CANLI olarak hesaplanir (bkz. RankAllUsersAsync). Boylece herhangi bir batch/cron
/// isine ihtiyac olmadan dogru sonuc alinir; kullanici sayisi cok buyudugunde bu hesaplamanin
/// LeaderboardEntry'yi dolduran periyodik bir job ile once-hesaplanmis hale getirilmesi onerilir.
/// </summary>
public class LeaderboardService : ILeaderboardService
{
    private static readonly string[] TurkishMonthNames =
    {
        "Ocak", "Şubat", "Mart", "Nisan", "Mayıs", "Haziran",
        "Temmuz", "Ağustos", "Eylül", "Ekim", "Kasım", "Aralık",
    };

    private readonly IUnitOfWork _unitOfWork;

    public LeaderboardService(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<CustomerLeaderboardResponse> GetLeaderboardAsync(long userId, string period, CancellationToken cancellationToken = default)
    {
        var profile = await _unitOfWork.Repository<UserProfile>()
            .FirstOrDefaultAsync(p => p.UserId == userId, cancellationToken);
        if (profile is null)
        {
            throw new NotFoundException(nameof(UserProfile), userId);
        }

        var periodType = MapPeriodType(period);
        var periodTag = periodType.ToString();

        var ranked = await RankAllUsersAsync(periodTag, cancellationToken);

        var currentUserRank = ranked.FirstOrDefault(r => r.UserId == userId);
        var hasScoreData = currentUserRank is not null;

        var userNames = await BuildDisplayNamesAsync(ranked.Select(r => r.UserId).ToList(), cancellationToken);

        var topEntries = ranked
            .Take(10)
            .Select(r => MapEntry(r, userId, userNames))
            .ToList();

        var nearbyEntries = new List<CustomerLeaderboardEntryDto>();
        if (currentUserRank is not null)
        {
            var currentIndex = ranked.FindIndex(r => r.UserId == userId);
            var start = Math.Max(0, currentIndex - 3);
            var end = Math.Min(ranked.Count, currentIndex + 3);
            nearbyEntries = ranked.Skip(start).Take(end - start)
                .Select(r => MapEntry(r, userId, userNames))
                .ToList();
        }

        var totalRankedUsers = ranked.Count;
        var topPercent = hasScoreData && totalRankedUsers > 0
            ? Math.Max(1, (int)Math.Round(currentUserRank!.Rank * 100m / totalRankedUsers))
            : 100;

        var scoreBreakdown = await BuildScoreBreakdownAsync(userId, cancellationToken);
        var scoreHistory = await BuildScoreHistoryAsync(userId, cancellationToken);
        var achievements = await BuildAchievementsAsync(userId, cancellationToken);
        var (rewards, resetAt) = await BuildRewardsAsync(periodType, cancellationToken);

        var privacy = await _unitOfWork.Repository<UserPrivacySetting>()
            .FirstOrDefaultAsync(p => p.UserId == userId, cancellationToken);

        var weeklyChange = await CalculateWeeklyChangeAsync(userId, cancellationToken);
        var currentPoints = currentUserRank?.Points ?? 0;
        var currentRankValue = currentUserRank?.Rank ?? 0;

        return new CustomerLeaderboardResponse
        {
            HasScoreData = hasScoreData,
            CurrentUser = new CustomerCurrentUserSummaryDto
            {
                Id = userId.ToString(),
                Name = profile.FirstName,
                Points = currentPoints,
                WeeklyChange = weeklyChange,
                // NOT: Gecmis donemlerdeki en iyi sirasi ayri bir tabloda tutulmuyor; bu yuzden
                // "en iyi sira" olarak mevcut sira/ay gosteriliyor (bkz. FINAL RAPOR - Remaining Work).
                BestRank = currentRankValue,
                BestRankMonthLabel = $"{TurkishMonthNames[DateTime.UtcNow.Month - 1]} {DateTime.UtcNow.Year}",
                League = MapLeague(topPercent),
                LeaderboardVisible = privacy?.ProfileVisibleOnLeaderboard ?? true,
            },
            TopEntries = topEntries,
            NearbyEntries = nearbyEntries,
            TotalRankedUsers = totalRankedUsers,
            TopPercent = topPercent,
            ScoreBreakdown = scoreBreakdown,
            ScoreHistory = scoreHistory,
            Achievements = achievements,
            Rewards = rewards,
            RewardsResetAt = resetAt,
        };
    }

    private record RankedUser(long UserId, int Rank, int Points, int Workouts);

    /// <summary>
    /// Verilen donem etiketine ait en guncel MerfitScore kaydini (kullanici basina) getirir,
    /// puana gore azalan sekilde siralar ve sira (rank) atar. Kucuk/orta olcekli kullanici
    /// tabaninda (birkac bin kayit) memory'de siralama performans acisindan sorun teskil etmez.
    /// </summary>
    private async Task<List<RankedUser>> RankAllUsersAsync(string periodTag, CancellationToken cancellationToken)
    {
        var scores = await _unitOfWork.Repository<MerfitScore>()
            .GetQueryable()
            .Where(s => s.Period == periodTag)
            .ToListAsync(cancellationToken);

        var latestPerUser = scores
            .GroupBy(s => s.UserId)
            .Select(g => g.OrderByDescending(s => s.CalculatedAt).First())
            .OrderByDescending(s => s.Score)
            .ToList();

        if (latestPerUser.Count == 0)
        {
            return new List<RankedUser>();
        }

        var workoutCounts = await _unitOfWork.Repository<WorkoutSession>()
            .GetQueryable()
            .Where(s => s.Status == WorkoutSessionStatus.Completed)
            .GroupBy(s => s.UserId)
            .Select(g => new { UserId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.UserId, x => x.Count, cancellationToken);

        return latestPerUser
            .Select((s, index) => new RankedUser(
                s.UserId,
                index + 1,
                (int)Math.Round(s.Score),
                workoutCounts.GetValueOrDefault(s.UserId, 0)))
            .ToList();
    }

    private async Task<Dictionary<long, string>> BuildDisplayNamesAsync(List<long> userIds, CancellationToken cancellationToken)
    {
        if (userIds.Count == 0)
        {
            return new Dictionary<long, string>();
        }

        return await _unitOfWork.Repository<UserProfile>()
            .GetQueryable()
            .Where(p => userIds.Contains(p.UserId))
            .ToDictionaryAsync(p => p.UserId, p => p.FirstName, cancellationToken);
    }

    private static CustomerLeaderboardEntryDto MapEntry(RankedUser ranked, long currentUserId, Dictionary<long, string> names) => new()
    {
        Id = ranked.UserId.ToString(),
        Name = names.GetValueOrDefault(ranked.UserId, "Kullanıcı"),
        Points = ranked.Points,
        Workouts = ranked.Workouts,
        Rank = ranked.Rank,
        IsCurrentUser = ranked.UserId == currentUserId,
    };

    private async Task<int> CalculateWeeklyChangeAsync(long userId, CancellationToken cancellationToken)
    {
        var latest = await _unitOfWork.Repository<MerfitScore>()
            .GetQueryable()
            .Where(s => s.UserId == userId)
            .OrderByDescending(s => s.CalculatedAt)
            .FirstOrDefaultAsync(cancellationToken);
        if (latest is null)
        {
            return 0;
        }

        var weekAgo = latest.CalculatedAt.AddDays(-7);
        var past = await _unitOfWork.Repository<MerfitScoreHistory>()
            .GetQueryable()
            .Where(h => h.UserId == userId && h.RecordedAt <= weekAgo)
            .OrderByDescending(h => h.RecordedAt)
            .FirstOrDefaultAsync(cancellationToken);

        return past is not null ? (int)Math.Round(latest.Score - past.Score) : 0;
    }

    private async Task<List<CustomerScoreBreakdownItemDto>> BuildScoreBreakdownAsync(long userId, CancellationToken cancellationToken)
    {
        var latestScore = await _unitOfWork.Repository<MerfitScore>()
            .GetQueryable()
            .Where(s => s.UserId == userId)
            .OrderByDescending(s => s.CalculatedAt)
            .FirstOrDefaultAsync(cancellationToken);
        if (latestScore is null)
        {
            return new List<CustomerScoreBreakdownItemDto>();
        }

        var breakdown = await _unitOfWork.Repository<MerfitScoreBreakdown>()
            .GetQueryable()
            .Where(b => b.MerfitScoreId == latestScore.Id)
            .ToListAsync(cancellationToken);

        return breakdown.Select(b => new CustomerScoreBreakdownItemDto
        {
            Id = b.Category,
            Label = b.Category,
            Points = (int)Math.Round(b.Points),
        }).ToList();
    }

    private async Task<List<CustomerScoreHistoryPointDto>> BuildScoreHistoryAsync(long userId, CancellationToken cancellationToken)
    {
        var history = await _unitOfWork.Repository<MerfitScoreHistory>()
            .GetQueryable()
            .Where(h => h.UserId == userId)
            .OrderByDescending(h => h.RecordedAt)
            .Take(7)
            .ToListAsync(cancellationToken);

        var dayAbbrev = new[] { "Paz", "Pzt", "Sal", "Çar", "Per", "Cum", "Cmt" };

        return history
            .OrderBy(h => h.RecordedAt)
            .Select(h => new CustomerScoreHistoryPointDto
            {
                Label = dayAbbrev[(int)h.RecordedAt.DayOfWeek],
                Points = (int)Math.Round(h.Score),
            })
            .ToList();
    }

    private async Task<List<CustomerAchievementDto>> BuildAchievementsAsync(long userId, CancellationToken cancellationToken)
    {
        var achievements = await _unitOfWork.Repository<Achievement>()
            .GetQueryable()
            .Where(a => a.IsActive)
            .ToListAsync(cancellationToken);

        var earnedIds = await _unitOfWork.Repository<UserAchievement>()
            .GetQueryable()
            .Where(ua => ua.UserId == userId)
            .Select(ua => ua.AchievementId)
            .ToListAsync(cancellationToken);
        var earnedSet = earnedIds.ToHashSet();

        return achievements.Select(a => new CustomerAchievementDto
        {
            Id = a.Code,
            Title = a.Title,
            Description = a.Description ?? string.Empty,
            Icon = a.Icon ?? "workouts",
            Earned = earnedSet.Contains(a.Id),
        }).ToList();
    }

    private async Task<(List<CustomerRewardDto> Rewards, DateTime? ResetAt)> BuildRewardsAsync(LeaderboardPeriodType periodType, CancellationToken cancellationToken)
    {
        var activePeriod = await _unitOfWork.Repository<LeaderboardPeriod>()
            .FirstOrDefaultAsync(p => p.Type == periodType && p.IsActive, cancellationToken);
        if (activePeriod is null)
        {
            return (new List<CustomerRewardDto>(), null);
        }

        var links = await _unitOfWork.Repository<LeaderboardReward>()
            .GetQueryable()
            .Where(lr => lr.LeaderboardPeriodId == activePeriod.Id)
            .OrderBy(lr => lr.Rank)
            .ToListAsync(cancellationToken);

        var rewardIds = links.Select(l => l.RewardId).Distinct().ToList();
        var rewardsById = rewardIds.Count == 0
            ? new Dictionary<long, Reward>()
            : await _unitOfWork.Repository<Reward>()
                .GetQueryable()
                .Where(r => rewardIds.Contains(r.Id) && r.IsActive)
                .ToDictionaryAsync(r => r.Id, cancellationToken);

        var rewards = links
            .Where(l => rewardsById.ContainsKey(l.RewardId))
            .Select(l =>
            {
                var reward = rewardsById[l.RewardId];
                return new CustomerRewardDto
                {
                    Rank = l.Rank,
                    Title = reward.Title,
                    Description = reward.Description ?? string.Empty,
                    Icon = reward.RewardType,
                };
            })
            .ToList();

        return (rewards, activePeriod.EndDate);
    }

    private static LeaderboardPeriodType MapPeriodType(string period) => period switch
    {
        "week" => LeaderboardPeriodType.Weekly,
        "month" => LeaderboardPeriodType.Monthly,
        "allTime" => LeaderboardPeriodType.AllTime,
        _ => LeaderboardPeriodType.Monthly,
    };

    private static string MapLeague(int topPercent) => topPercent switch
    {
        <= 1 => "Elmas",
        <= 5 => "Altın",
        <= 15 => "Gümüş",
        <= 40 => "Bronz",
        _ => "Katılımcı",
    };
}
