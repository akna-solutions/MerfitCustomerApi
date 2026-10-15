using MerfitCustomerApi.Business.Common;
using MerfitCustomerApi.Business.Dtos.Admin.Workouts;
using MerfitCustomerApi.Business.Interfaces.Services;
using MerfitCustomerApi.Domain.Entities;
using MerfitCustomerApi.Domain.Entities.Enums;
using MerfitCustomerApi.Domain.Exceptions;
using MerfitCustomerApi.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace MerfitCustomerApi.Business.Services.Workouts;

/// <summary>IWorkoutAttributeScoreService'in varsayilan implementasyonu.</summary>
public class WorkoutAttributeScoreService : IWorkoutAttributeScoreService
{
    private readonly IUnitOfWork _unitOfWork;

    public WorkoutAttributeScoreService(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<AdminWorkoutAttributeScoresDto> GetAsync(long workoutId, CancellationToken cancellationToken = default)
    {
        await EnsureWorkoutExistsAsync(workoutId, cancellationToken);

        var scores = await _unitOfWork.Repository<WorkoutAttributeScore>()
            .GetQueryable()
            .Where(s => s.WorkoutId == workoutId)
            .ToListAsync(cancellationToken);

        return ToDto(workoutId, scores);
    }

    public async Task<AdminWorkoutAttributeScoresDto> SetAsync(
        long workoutId,
        AdminSetWorkoutAttributeScoresRequest request,
        long? adminUserId,
        CancellationToken cancellationToken = default)
    {
        await EnsureWorkoutExistsAsync(workoutId, cancellationToken);
        var requested = ValidateAndNormalize(request);

        var repo = _unitOfWork.Repository<WorkoutAttributeScore>();
        var existing = await repo.GetQueryable(asNoTracking: false)
            .Where(s => s.WorkoutId == workoutId)
            .ToListAsync(cancellationToken);

        var now = DateTime.UtcNow;
        foreach (var (attributeType, score) in requested)
        {
            var current = existing.FirstOrDefault(s => s.AttributeType == attributeType);
            if (current is null)
            {
                await repo.AddAsync(new WorkoutAttributeScore
                {
                    WorkoutId = workoutId,
                    AttributeType = attributeType,
                    Score = score,
                    CreatedAt = now,
                    CreatedUser = adminUserId,
                }, cancellationToken);
            }
            else if (current.Score != score)
            {
                current.Score = score;
                current.UpdatedAt = now;
                current.UpdatedUser = adminUserId;
                repo.Update(current);
            }
        }

        var removed = existing.Where(s => !requested.ContainsKey(s.AttributeType)).ToList();
        if (removed.Count > 0)
        {
            repo.RemoveRange(removed);
        }

        try
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Ayni antrenman+tur icin es zamanli bir kayit, (WorkoutId, AttributeType) unique index'ine takildi.
            throw new ConflictException("Antrenman puanlari ayni anda baska bir islemle guncellendi. Lutfen tekrar deneyin.");
        }

        return await GetAsync(workoutId, cancellationToken);
    }

    private async Task EnsureWorkoutExistsAsync(long workoutId, CancellationToken cancellationToken)
    {
        var exists = await _unitOfWork.Repository<Workout>().AnyAsync(w => w.Id == workoutId, cancellationToken);
        if (!exists)
        {
            throw new NotFoundException(nameof(Workout), workoutId);
        }
    }

    /// <summary>
    /// DTO attribute'larindan bagimsiz olarak (servis baska yerden de cagrilabilir) tur ve puan
    /// kurallarini dogrular; ayni turun tekrarina izin vermez.
    /// </summary>
    private static Dictionary<WorkoutAttributeType, int> ValidateAndNormalize(AdminSetWorkoutAttributeScoresRequest request)
    {
        var result = new Dictionary<WorkoutAttributeType, int>();
        foreach (var item in request.Scores ?? new List<AdminWorkoutAttributeScoreInput>())
        {
            if (item.AttributeType is null || !Enum.IsDefined(item.AttributeType.Value))
            {
                throw new AppValidationException(nameof(AdminWorkoutAttributeScoreInput.AttributeType), "Gecersiz ozellik turu.");
            }

            if (item.Score < WorkoutAttributeScore.MinScore || item.Score > WorkoutAttributeScore.MaxScore)
            {
                throw new AppValidationException(
                    nameof(AdminWorkoutAttributeScoreInput.Score),
                    $"Puan {WorkoutAttributeScore.MinScore}-{WorkoutAttributeScore.MaxScore} araliginda olmalidir.");
            }

            if (!result.TryAdd(item.AttributeType.Value, item.Score))
            {
                throw new AppValidationException(
                    nameof(AdminWorkoutAttributeScoreInput.AttributeType),
                    $"{item.AttributeType.Value} ozelligi birden fazla kez gonderilemez.");
            }
        }

        // Kismi ana puan, hedef uyumunda eksik ozelligi 0 sayar ve antrenmani haksiz yere geri iter
        // (veya eski normalizasyonda one cikarirdi). Bu yuzden puan giriliyorsa uc ana ozellik birlikte zorunludur.
        if (result.Count > 0)
        {
            var missingPrimary = GoalAttributeWeights.PrimaryAttributes.Where(t => !result.ContainsKey(t)).ToList();
            if (missingPrimary.Count > 0)
            {
                throw new AppValidationException(
                    nameof(AdminSetWorkoutAttributeScoresRequest.Scores),
                    $"Hipertrofi, Kuvvet ve Kardiyo puanlari birlikte girilmelidir (eksik: {string.Join(", ", missingPrimary)}).");
            }
        }

        return result;
    }

    private static AdminWorkoutAttributeScoresDto ToDto(long workoutId, IReadOnlyCollection<WorkoutAttributeScore> scores)
    {
        var scoredTypes = scores.Select(s => s.AttributeType).ToHashSet();
        return new AdminWorkoutAttributeScoresDto
        {
            WorkoutId = workoutId,
            Scores = scores
                .OrderBy(s => s.AttributeType)
                .Select(s => new AdminWorkoutAttributeScoreItemDto
                {
                    AttributeType = s.AttributeType.ToString(),
                    Score = s.Score,
                    UpdatedAt = s.UpdatedAt ?? s.CreatedAt,
                })
                .ToList(),
            MissingAttributeTypes = Enum.GetValues<WorkoutAttributeType>()
                .Where(t => !scoredTypes.Contains(t))
                .Select(t => t.ToString())
                .ToList(),
            MissingPrimaryAttributeTypes = GoalAttributeWeights.PrimaryAttributes
                .Where(t => !scoredTypes.Contains(t))
                .Select(t => t.ToString())
                .ToList(),
            IsPrimaryComplete = GoalAttributeWeights.PrimaryAttributes.All(scoredTypes.Contains),
        };
    }
}
