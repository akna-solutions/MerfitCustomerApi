using MerfitCustomerApi.Business.Dtos.Customer.Achievements;
using MerfitCustomerApi.Business.Interfaces.Services;
using MerfitCustomerApi.Domain.Entities;
using MerfitCustomerApi.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace MerfitCustomerApi.Business.Services.Achievements;

/// <summary>
/// IAchievementService'in varsayilan implementasyonu. Kazanim kosullari IMerfitScoreService'te hesaplanir
/// (antrenman, ogun, su, kilo ve mini oyun islemlerinden sonra da ayni kod calisir); bu servis listeyi okur ve
/// UserAchievement.SeenAt ile kutlamanin her kazanim icin yalnizca bir kez gosterilmesini saglar.
/// </summary>
public class AchievementService : IAchievementService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IMerfitScoreService _scoreService;

    public AchievementService(IUnitOfWork unitOfWork, IMerfitScoreService scoreService)
    {
        _unitOfWork = unitOfWork;
        _scoreService = scoreService;
    }

    public async Task<CustomerAchievementsResponse> GetMyAchievementsAsync(long userId, CancellationToken cancellationToken = default)
    {
        // Ornegin admin panelinden yeni eklenen bir basarinin kosulu zaten saglaniyorsa burada kazanilir.
        await _scoreService.TryRefreshUserAsync(userId, cancellationToken);

        var achievements = await _unitOfWork.Repository<Achievement>()
            .GetQueryable()
            .Where(a => a.IsActive)
            .OrderBy(a => a.Points)
            .ThenBy(a => a.Id)
            .ToListAsync(cancellationToken);

        var earned = await _unitOfWork.Repository<UserAchievement>()
            .GetQueryable()
            .Where(ua => ua.UserId == userId)
            .Select(ua => new { ua.AchievementId, ua.EarnedAt, ua.SeenAt })
            .ToListAsync(cancellationToken);
        // Eski veride ayni basari birden fazla yazilmis olabilir; ilk kazanim esas alinir.
        var earnedById = earned
            .GroupBy(e => e.AchievementId)
            .ToDictionary(g => g.Key, g => (EarnedAt: g.Min(e => e.EarnedAt), Seen: g.Any(e => e.SeenAt != null)));

        var items = achievements.Select(a =>
        {
            var isEarned = earnedById.TryGetValue(a.Id, out var info);
            return new
            {
                Unseen = isEarned && !info.Seen,
                Dto = new CustomerAchievementItemDto
                {
                    Id = a.Id,
                    Code = a.Code,
                    Title = a.Title,
                    Description = a.Description ?? string.Empty,
                    Icon = a.Icon ?? string.Empty,
                    Points = a.Points,
                    Earned = isEarned,
                    EarnedAt = isEarned ? DateTime.SpecifyKind(info.EarnedAt, DateTimeKind.Utc) : null,
                },
            };
        }).ToList();

        return new CustomerAchievementsResponse
        {
            EarnedCount = items.Count(i => i.Dto.Earned),
            TotalCount = items.Count,
            Achievements = items
                .OrderByDescending(i => i.Dto.Earned)
                .ThenByDescending(i => i.Dto.EarnedAt)
                .Select(i => i.Dto)
                .ToList(),
            NewlyEarned = items
                .Where(i => i.Unseen)
                .OrderBy(i => i.Dto.EarnedAt)
                .Select(i => i.Dto)
                .ToList(),
        };
    }

    public async Task MarkSeenAsync(long userId, MarkAchievementsSeenRequest request, CancellationToken cancellationToken = default)
    {
        var ids = request.AchievementIds.Distinct().ToList();
        if (ids.Count == 0)
        {
            return;
        }

        var repository = _unitOfWork.Repository<UserAchievement>();
        var rows = await repository.GetQueryable(asNoTracking: false)
            .Where(ua => ua.UserId == userId && ids.Contains(ua.AchievementId) && ua.SeenAt == null)
            .ToListAsync(cancellationToken);
        if (rows.Count == 0)
        {
            return;
        }

        var now = DateTime.UtcNow;
        foreach (var row in rows)
        {
            row.SeenAt = now;
            row.UpdatedAt = now;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
