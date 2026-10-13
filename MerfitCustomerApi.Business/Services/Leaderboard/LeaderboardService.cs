using MerfitCustomerApi.Business.Dtos.Customer.Leaderboard;
using MerfitCustomerApi.Business.Interfaces.Services;
using MerfitCustomerApi.Business.Services.Scoring;
using MerfitCustomerApi.Domain.Entities;
using MerfitCustomerApi.Domain.Entities.Enums;
using MerfitCustomerApi.Domain.Exceptions;
using MerfitCustomerApi.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace MerfitCustomerApi.Business.Services.Leaderboard;

/// <summary>
/// ILeaderboardService'in varsayilan implementasyonu. Siralama ve puanlar IMerfitScoreService
/// uzerinden, kullanicinin uygulamadaki aksiyonlarindan (antrenman, rekor, ogun/su kaydi, basari)
/// CANLI olarak turetilir; LeaderboardEntry tablosundaki admin anlik goruntulerine bagli degildir.
/// Odul listesi secili donem turunun bugunu kapsayan aktif LeaderboardPeriod kaydindan gelir.
/// </summary>
public class LeaderboardService : ILeaderboardService
{
    private static readonly string[] TurkishMonthNames =
    {
        "Ocak", "Şubat", "Mart", "Nisan", "Mayıs", "Haziran",
        "Temmuz", "Ağustos", "Eylül", "Ekim", "Kasım", "Aralık",
    };

    private static readonly (string Id, string Label)[] BreakdownCategories =
    {
        (MerfitScoreService.WorkoutsCategory, "Antrenmanlar"),
        (MerfitScoreService.PersonalRecordsCategory, "Kişisel rekorlar"),
        (MerfitScoreService.NutritionCategory, "Öğün takibi"),
        (MerfitScoreService.WaterCategory, "Su takibi"),
        (MerfitScoreService.AchievementsCategory, "Başarılar"),
        (MerfitScoreService.MiniGameCategory, "Mini oyun"),
    };

    private readonly IUnitOfWork _unitOfWork;
    private readonly IMerfitScoreService _scoreService;

    public LeaderboardService(IUnitOfWork unitOfWork, IMerfitScoreService scoreService)
    {
        _unitOfWork = unitOfWork;
        _scoreService = scoreService;
    }

    public async Task<CustomerLeaderboardResponse> GetLeaderboardAsync(long userId, string period, CancellationToken cancellationToken = default)
    {
        var profile = await _unitOfWork.Repository<UserProfile>()
            .FirstOrDefaultAsync(p => p.UserId == userId, cancellationToken);
        if (profile is null)
        {
            throw new NotFoundException(nameof(UserProfile), userId);
        }

        // Kosulu saglanmis ama henuz verilmemis basarilar (orn. yeni tanimlanan bir basari) burada da verilir.
        await _scoreService.TryRefreshUserAsync(userId, cancellationToken);

        var periodType = MapPeriodType(period);
        var window = await _scoreService.ResolveWindowAsync(periodType, cancellationToken);

        var ranked = await _scoreService.RankUsersAsync(window.FromUtc, window.ToUtc, cancellationToken);

        var currentUserRank = ranked.FirstOrDefault(r => r.UserId == userId);
        var hasScoreData = currentUserRank is not null;

        var topRanked = ranked.Take(10).ToList();
        var nearbyRanked = new List<MerfitRankedUser>();
        if (currentUserRank is not null)
        {
            var currentIndex = ranked.FindIndex(r => r.UserId == userId);
            var start = Math.Max(0, currentIndex - 3);
            var end = Math.Min(ranked.Count, currentIndex + 3);
            nearbyRanked = ranked.Skip(start).Take(end - start).ToList();
        }

        var userNames = await BuildDisplayNamesAsync(topRanked.Concat(nearbyRanked).Select(r => r.UserId).Distinct().ToList(), cancellationToken);

        var totalRankedUsers = ranked.Count;
        var topPercent = hasScoreData && totalRankedUsers > 0
            ? Math.Max(1, (int)Math.Round(currentUserRank!.Rank * 100m / totalRankedUsers))
            : 100;

        var userEvents = await _scoreService.GetPointEventsAsync(null, null, userId, cancellationToken);
        var scoreBreakdown = BuildScoreBreakdown(userEvents, window);
        var scoreHistory = BuildScoreHistory(userEvents);
        var weekAgo = DateTime.UtcNow.AddDays(-7);
        var weeklyChange = userEvents.Where(e => e.OccurredAtUtc >= weekAgo).Sum(e => e.Points);

        var achievements = await BuildAchievementsAsync(userId, cancellationToken);
        var rewards = await BuildRewardsAsync(window.Period, cancellationToken);
        var earnedRewards = await BuildEarnedRewardsAsync(userId, cancellationToken);

        var privacy = await _unitOfWork.Repository<UserPrivacySetting>()
            .FirstOrDefaultAsync(p => p.UserId == userId, cancellationToken);

        return new CustomerLeaderboardResponse
        {
            HasScoreData = hasScoreData,
            CurrentUser = new CustomerCurrentUserSummaryDto
            {
                Id = userId.ToString(),
                Name = profile.FirstName,
                Points = currentUserRank?.Points ?? 0,
                Rank = currentUserRank?.Rank,
                WeeklyChange = weeklyChange,
                // NOT: Gecmis donemlerdeki en iyi sira ayri bir tabloda tutulmuyor; bu yuzden
                // "en iyi sira" olarak mevcut sira/ay gosteriliyor.
                BestRank = currentUserRank?.Rank ?? 0,
                BestRankMonthLabel = $"{TurkishMonthNames[DateTime.UtcNow.Month - 1]} {DateTime.UtcNow.Year}",
                League = MapLeague(topPercent),
                LeaderboardVisible = privacy?.ProfileVisibleOnLeaderboard ?? true,
            },
            TopEntries = topRanked.Select(r => MapEntry(r, userId, userNames)).ToList(),
            NearbyEntries = nearbyRanked.Select(r => MapEntry(r, userId, userNames)).ToList(),
            TotalRankedUsers = totalRankedUsers,
            TopPercent = topPercent,
            ScoreBreakdown = scoreBreakdown,
            ScoreHistory = scoreHistory,
            Achievements = achievements,
            Rewards = rewards,
            RewardsResetAt = rewards.Count > 0 ? window.Period?.EndDate : null,
            EarnedRewards = earnedRewards,
        };
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

    private async Task<List<CustomerRewardDto>> BuildRewardsAsync(LeaderboardPeriod? activePeriod, CancellationToken cancellationToken)
    {
        if (activePeriod is null)
        {
            return new List<CustomerRewardDto>();
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

        return links
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
                    ImageUrl = reward.ImageUrl,
                    Value = reward.Value,
                };
            })
            .ToList();
    }

    /// <summary>Kullanicinin kazandigi (iptal edilmemis) oduller, en yeniden eskiye.</summary>
    private async Task<List<CustomerEarnedRewardDto>> BuildEarnedRewardsAsync(long userId, CancellationToken cancellationToken)
    {
        var rows = await (
            from ur in _unitOfWork.Repository<UserReward>().GetQueryable()
            join r in _unitOfWork.Repository<Reward>().GetQueryable() on ur.RewardId equals r.Id
            where ur.UserId == userId && ur.Status != UserRewardStatus.Cancelled
            orderby ur.CreatedAt descending
            select new { ur.Id, ur.Rank, ur.Status, ur.CreatedAt, ur.ClaimedAt, r.Title, r.Description, r.RewardType, r.ImageUrl, r.Value }
        ).ToListAsync(cancellationToken);

        return rows.Select(x => new CustomerEarnedRewardDto
        {
            Id = x.Id,
            Rank = x.Rank,
            Title = x.Title,
            Description = x.Description ?? string.Empty,
            Icon = x.RewardType,
            ImageUrl = x.ImageUrl,
            Value = x.Value,
            Status = x.Status switch
            {
                UserRewardStatus.Claimed => "claimed",
                UserRewardStatus.Expired => "expired",
                _ => "pending",
            },
            EarnedAt = x.CreatedAt,
            ClaimedAt = x.ClaimedAt,
        }).ToList();
    }

    private static CustomerLeaderboardEntryDto MapEntry(MerfitRankedUser ranked, long currentUserId, Dictionary<long, string> names) => new()
    {
        Id = ranked.UserId.ToString(),
        Name = names.GetValueOrDefault(ranked.UserId, "Kullanıcı"),
        Points = ranked.Points,
        Workouts = ranked.Workouts,
        Rank = ranked.Rank,
        IsCurrentUser = ranked.UserId == currentUserId,
    };

    /// <summary>Secili donemde kazanilan puanlarin kategori dagilimi (puani olmayan kategoriler atlanir).</summary>
    private static List<CustomerScoreBreakdownItemDto> BuildScoreBreakdown(IReadOnlyList<MerfitPointEvent> userEvents, MerfitScoreWindow window)
    {
        var inWindow = userEvents
            .Where(e => (!window.FromUtc.HasValue || e.OccurredAtUtc >= window.FromUtc.Value)
                        && (!window.ToUtc.HasValue || e.OccurredAtUtc < window.ToUtc.Value))
            .ToList();

        return BreakdownCategories
            .Select(c => new CustomerScoreBreakdownItemDto
            {
                Id = c.Id,
                Label = c.Label,
                Points = inWindow.Where(e => e.Category == c.Id).Sum(e => e.Points),
            })
            .Where(i => i.Points > 0)
            .ToList();
    }

    /// <summary>Son 7 gunun her biri icin gun sonundaki toplam MB FIT puani.</summary>
    private List<CustomerScoreHistoryPointDto> BuildScoreHistory(IReadOnlyList<MerfitPointEvent> userEvents)
    {
        if (userEvents.Count == 0)
        {
            return new List<CustomerScoreHistoryPointDto>();
        }

        var dayAbbrev = new[] { "Paz", "Pzt", "Sal", "Çar", "Per", "Cum", "Cmt" };
        var todayStart = _scoreService.GetLocalDayStartUtc(DateTime.UtcNow);

        return Enumerable.Range(0, 7)
            .Select(offset => todayStart.AddDays(offset - 6))
            .Select(dayStart => new CustomerScoreHistoryPointDto
            {
                Label = dayAbbrev[(int)dayStart.AddHours(12).DayOfWeek],
                Points = userEvents.Where(e => e.OccurredAtUtc < dayStart.AddDays(1)).Sum(e => e.Points),
            })
            .ToList();
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
