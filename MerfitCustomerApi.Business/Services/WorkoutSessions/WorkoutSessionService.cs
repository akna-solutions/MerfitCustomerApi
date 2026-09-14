using MerfitCustomerApi.Business.Dtos.Customer.WorkoutSessions;
using MerfitCustomerApi.Business.Interfaces.Services;
using MerfitCustomerApi.Domain.Entities;
using MerfitCustomerApi.Domain.Entities.Enums;
using MerfitCustomerApi.Domain.Exceptions;
using MerfitCustomerApi.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace MerfitCustomerApi.Business.Services.WorkoutSessions;

/// <summary>
/// IWorkoutSessionService'in varsayilan implementasyonu. WorkoutSession/WorkoutSessionExercise/
/// WorkoutSetLog/PersonalRecord/UserStreak entity'lerini oldugu gibi kullanir.
/// </summary>
public class WorkoutSessionService : IWorkoutSessionService
{
    private readonly IUnitOfWork _unitOfWork;

    public WorkoutSessionService(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<CustomerWorkoutSessionDto> StartSessionAsync(long userId, StartWorkoutSessionRequest request, CancellationToken cancellationToken = default)
    {
        var workout = await _unitOfWork.Repository<Workout>().GetByIdAsync(request.WorkoutId, cancellationToken);
        if (workout is null || !workout.IsActive)
        {
            throw new NotFoundException(nameof(Workout), request.WorkoutId);
        }

        // Faz 2: oturum kisisellestirilmis bir plan gununden ("Bugünün Antrenmanı" -> Başlat)
        // baslatildiysa, hedef set/tekrar/dinlenme degerleri WorkoutExercise (genel katalog)
        // yerine WorkoutPlanExercise'dan (kullaniciya ozel) okunur. Mevcut genel workout akisi
        // (WorkoutPlanDayId gonderilmezse) hic degismez.
        long? workoutPlanDayId = null;
        List<(long ExerciseId, int Order)> exerciseOrder;

        if (request.WorkoutPlanDayId.HasValue)
        {
            var planDay = await _unitOfWork.Repository<WorkoutPlanDay>()
                .GetByIdAsync(request.WorkoutPlanDayId.Value, cancellationToken);
            if (planDay is null || planDay.WorkoutId != request.WorkoutId)
            {
                throw new AppValidationException(nameof(request.WorkoutPlanDayId), "Belirtilen plan günü bu antrenmana ait değil.");
            }

            var ownerPlan = await _unitOfWork.Repository<WorkoutPlan>()
                .FirstOrDefaultAsync(p => p.Id == planDay.WorkoutPlanId && p.UserId == userId, cancellationToken);
            if (ownerPlan is null)
            {
                // Kaydin varligini sizdirmamak icin NotFound (IDOR korumasi) - baska kullanicinin plan gunu.
                throw new NotFoundException(nameof(WorkoutPlanDay), request.WorkoutPlanDayId.Value);
            }

            workoutPlanDayId = planDay.Id;
            var planExercises = await _unitOfWork.Repository<WorkoutPlanExercise>()
                .GetQueryable()
                .Where(pe => pe.WorkoutPlanDayId == planDay.Id)
                .OrderBy(pe => pe.Order)
                .ToListAsync(cancellationToken);
            exerciseOrder = planExercises.Select(pe => (pe.ExerciseId, pe.Order)).ToList();
        }
        else
        {
            var catalogExercises = await _unitOfWork.Repository<WorkoutExercise>()
                .GetQueryable()
                .Where(we => we.WorkoutId == workout.Id)
                .OrderBy(we => we.Order)
                .ToListAsync(cancellationToken);
            exerciseOrder = catalogExercises.Select(we => (we.ExerciseId, we.Order)).ToList();
        }

        await _unitOfWork.BeginTransactionAsync(cancellationToken);

        var session = new WorkoutSession
        {
            UserId = userId,
            WorkoutId = workout.Id,
            WorkoutPlanDayId = workoutPlanDayId,
            StartedAt = DateTime.UtcNow,
            Status = WorkoutSessionStatus.Started,
            CreatedAt = DateTime.UtcNow,
        };

        await _unitOfWork.Repository<WorkoutSession>().AddAsync(session, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        var sessionExercises = exerciseOrder.Select(pe => new WorkoutSessionExercise
        {
            WorkoutSessionId = session.Id,
            ExerciseId = pe.ExerciseId,
            Order = pe.Order,
            CreatedAt = DateTime.UtcNow,
        }).ToList();

        await _unitOfWork.Repository<WorkoutSessionExercise>().AddRangeAsync(sessionExercises, cancellationToken);
        await _unitOfWork.CommitTransactionAsync(cancellationToken);

        var targets = await GetExerciseTargetsAsync(session, cancellationToken);
        return await BuildSessionDtoAsync(session, targets, sessionExercises, userId, cancellationToken);
    }

    public async Task<CustomerWorkoutSessionDto> GetSessionAsync(long userId, long sessionId, CancellationToken cancellationToken = default)
    {
        var session = await GetOwnedSessionAsync(userId, sessionId, cancellationToken);
        var workout = await _unitOfWork.Repository<Workout>().GetByIdAsync(session.WorkoutId, cancellationToken);

        var targets = await GetExerciseTargetsAsync(session, cancellationToken);

        var sessionExercises = await _unitOfWork.Repository<WorkoutSessionExercise>()
            .GetQueryable()
            .Where(se => se.WorkoutSessionId == session.Id)
            .OrderBy(se => se.Order)
            .ToListAsync(cancellationToken);

        return await BuildSessionDtoAsync(session, targets, sessionExercises, userId, cancellationToken, workout?.Title);
    }

    public async Task<CustomerWorkoutSessionExerciseDto> LogSetAsync(
        long userId,
        long sessionId,
        long sessionExerciseId,
        LogWorkoutSetRequest request,
        CancellationToken cancellationToken = default)
    {
        var session = await GetOwnedSessionAsync(userId, sessionId, cancellationToken);
        EnsureSessionIsActive(session);

        var sessionExercise = await _unitOfWork.Repository<WorkoutSessionExercise>()
            .FirstOrDefaultAsync(se => se.Id == sessionExerciseId && se.WorkoutSessionId == sessionId, cancellationToken);
        if (sessionExercise is null)
        {
            throw new NotFoundException(nameof(WorkoutSessionExercise), sessionExerciseId);
        }

        if (sessionExercise.StartedAt is null)
        {
            sessionExercise.StartedAt = DateTime.UtcNow;
            _unitOfWork.Repository<WorkoutSessionExercise>().Update(sessionExercise);
        }

        var existingSet = await _unitOfWork.Repository<WorkoutSetLog>()
            .FirstOrDefaultAsync(
                s => s.WorkoutSessionExerciseId == sessionExerciseId && s.SetNumber == request.SetNumber,
                cancellationToken);

        if (existingSet is null)
        {
            var newSet = new WorkoutSetLog
            {
                WorkoutSessionExerciseId = sessionExerciseId,
                SetNumber = request.SetNumber,
                WeightKg = request.WeightKg,
                Reps = request.Reps,
                DurationSeconds = request.DurationSeconds,
                Rpe = request.Rpe,
                CompletedAt = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow,
            };
            await _unitOfWork.Repository<WorkoutSetLog>().AddAsync(newSet, cancellationToken);
        }
        else
        {
            existingSet.WeightKg = request.WeightKg;
            existingSet.Reps = request.Reps;
            existingSet.DurationSeconds = request.DurationSeconds;
            existingSet.Rpe = request.Rpe;
            existingSet.CompletedAt = DateTime.UtcNow;
            _unitOfWork.Repository<WorkoutSetLog>().Update(existingSet);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        var exercise = await _unitOfWork.Repository<Exercise>().GetByIdAsync(sessionExercise.ExerciseId, cancellationToken);
        var targets = await GetExerciseTargetsAsync(session, cancellationToken);
        var setLogs = await _unitOfWork.Repository<WorkoutSetLog>()
            .GetQueryable()
            .Where(s => s.WorkoutSessionExerciseId == sessionExerciseId)
            .OrderBy(s => s.SetNumber)
            .ToListAsync(cancellationToken);
        var previousBest = await GetPreviousBestAsync(userId, sessionExercise.ExerciseId, cancellationToken);

        var target = targets.TryGetValue(sessionExercise.ExerciseId, out var t) ? t : (ExerciseTargetValues?)null;
        return MapSessionExercise(sessionExercise, exercise, target, setLogs, previousBest);
    }

    public async Task<CustomerWorkoutSessionSummaryDto> CompleteSessionAsync(
        long userId,
        long sessionId,
        CompleteWorkoutSessionRequest request,
        CancellationToken cancellationToken = default)
    {
        var session = await GetOwnedSessionAsync(userId, sessionId, cancellationToken, forUpdate: true);
        EnsureSessionIsActive(session);

        session.Status = WorkoutSessionStatus.Completed;
        session.CompletedAt = DateTime.UtcNow;
        session.DurationSeconds = request.DurationSeconds;
        session.CaloriesBurned = request.CaloriesBurned;
        session.Notes = request.Notes;
        _unitOfWork.Repository<WorkoutSession>().Update(session);

        var newRecords = await DetectAndSavePersonalRecordsAsync(userId, session.Id, cancellationToken);
        await UpdateStreakAsync(userId, cancellationToken);

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new CustomerWorkoutSessionSummaryDto
        {
            SessionId = session.Id,
            DurationSeconds = request.DurationSeconds,
            CaloriesBurned = request.CaloriesBurned,
            NewPersonalRecords = newRecords,
        };
    }

    public async Task CancelSessionAsync(long userId, long sessionId, CancellationToken cancellationToken = default)
    {
        var session = await GetOwnedSessionAsync(userId, sessionId, cancellationToken, forUpdate: true);
        EnsureSessionIsActive(session);

        session.Status = WorkoutSessionStatus.Abandoned;
        session.CompletedAt = DateTime.UtcNow;
        _unitOfWork.Repository<WorkoutSession>().Update(session);

        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Oturumu getirir ve cagiran kullaniciya ait oldugunu dogrular. Baska bir kullaniciya
    /// ait bir oturuma erisim denemesinde -- kaydin varligini sizdirmamak icin -- 404 doner (IDOR korumasi).
    /// </summary>
    private async Task<WorkoutSession> GetOwnedSessionAsync(long userId, long sessionId, CancellationToken cancellationToken, bool forUpdate = false)
    {
        var repo = _unitOfWork.Repository<WorkoutSession>();
        var session = forUpdate
            ? await repo.GetQueryable(asNoTracking: false).FirstOrDefaultAsync(s => s.Id == sessionId, cancellationToken)
            : await repo.GetByIdAsync(sessionId, cancellationToken);

        if (session is null || session.UserId != userId)
        {
            throw new NotFoundException(nameof(WorkoutSession), sessionId);
        }

        return session;
    }

    private static void EnsureSessionIsActive(WorkoutSession session)
    {
        if (session.Status is WorkoutSessionStatus.Completed or WorkoutSessionStatus.Cancelled or WorkoutSessionStatus.Abandoned)
        {
            throw new AppValidationException("Bu antrenman oturumu zaten sonlandirilmis.");
        }
    }

    private async Task<CustomerWorkoutSessionDto> BuildSessionDtoAsync(
        WorkoutSession session,
        Dictionary<long, ExerciseTargetValues> targets,
        List<WorkoutSessionExercise> sessionExercises,
        long userId,
        CancellationToken cancellationToken,
        string? workoutTitleFallback = null)
    {
        var exerciseIds = sessionExercises.Select(se => se.ExerciseId).Distinct().ToList();
        var exercises = await _unitOfWork.Repository<Exercise>()
            .GetQueryable()
            .Where(e => exerciseIds.Contains(e.Id))
            .ToDictionaryAsync(e => e.Id, cancellationToken);

        var sessionExerciseIds = sessionExercises.Select(se => se.Id).ToList();
        var allSetLogs = await _unitOfWork.Repository<WorkoutSetLog>()
            .GetQueryable()
            .Where(s => sessionExerciseIds.Contains(s.WorkoutSessionExerciseId))
            .OrderBy(s => s.SetNumber)
            .ToListAsync(cancellationToken);

        var previousBests = await GetPreviousBestsAsync(userId, exerciseIds, cancellationToken);

        var workout = workoutTitleFallback is null
            ? await _unitOfWork.Repository<Workout>().GetByIdAsync(session.WorkoutId, cancellationToken)
            : null;

        var exerciseDtos = sessionExercises.Select(se =>
        {
            var target = targets.TryGetValue(se.ExerciseId, out var t) ? t : (ExerciseTargetValues?)null;
            var exercise = exercises.GetValueOrDefault(se.ExerciseId);
            var setLogs = allSetLogs.Where(s => s.WorkoutSessionExerciseId == se.Id).ToList();
            var previousBest = previousBests.GetValueOrDefault(se.ExerciseId);

            return MapSessionExercise(se, exercise, target, setLogs, previousBest);
        }).ToList();

        return new CustomerWorkoutSessionDto
        {
            Id = session.Id,
            WorkoutId = session.WorkoutId,
            Title = workoutTitleFallback ?? workout?.Title ?? string.Empty,
            Status = session.Status.ToString(),
            StartedAt = session.StartedAt,
            CompletedAt = session.CompletedAt,
            DurationSeconds = session.DurationSeconds,
            CaloriesBurned = session.CaloriesBurned,
            Exercises = exerciseDtos,
        };
    }

    /// <summary>
    /// Bir egzersizin hedef set/tekrar/dinlenme/sure degerlerini kaynaktan (genel katalog -
    /// WorkoutExercise - veya kisisellestirilmis plan - WorkoutPlanExercise) bagimsiz olarak
    /// tasir; MapSessionExercise bu iki kaynagi ayirt etmek zorunda kalmaz.
    /// </summary>
    private readonly record struct ExerciseTargetValues(int Sets, int? Reps, int? RestSeconds, int? DurationSeconds);

    /// <summary>
    /// Bir oturumun hedef degerlerini, oturumun hangi kaynaktan baslatildigina gore
    /// (WorkoutPlanDayId doluysa kisisellestirilmis WorkoutPlanExercise, degilse genel
    /// katalog WorkoutExercise) ExerciseId'ye gore dictionary olarak dondurur.
    /// </summary>
    private async Task<Dictionary<long, ExerciseTargetValues>> GetExerciseTargetsAsync(WorkoutSession session, CancellationToken cancellationToken)
    {
        if (session.WorkoutPlanDayId.HasValue)
        {
            var planExercises = await _unitOfWork.Repository<WorkoutPlanExercise>()
                .GetQueryable()
                .Where(pe => pe.WorkoutPlanDayId == session.WorkoutPlanDayId.Value)
                .ToListAsync(cancellationToken);

            return planExercises
                .GroupBy(pe => pe.ExerciseId)
                .ToDictionary(g => g.Key, g => new ExerciseTargetValues(g.First().Sets, g.First().Reps, g.First().RestSeconds, g.First().DurationSeconds));
        }

        var catalogExercises = await _unitOfWork.Repository<WorkoutExercise>()
            .GetQueryable()
            .Where(we => we.WorkoutId == session.WorkoutId)
            .ToListAsync(cancellationToken);

        return catalogExercises
            .GroupBy(we => we.ExerciseId)
            .ToDictionary(g => g.Key, g => new ExerciseTargetValues(g.First().Sets, g.First().Reps, g.First().RestSeconds, g.First().DurationSeconds));
    }

    private static CustomerWorkoutSessionExerciseDto MapSessionExercise(
        WorkoutSessionExercise sessionExercise,
        Exercise? exercise,
        ExerciseTargetValues? target,
        List<WorkoutSetLog> setLogs,
        CustomerPreviousBestDto? previousBest)
    {
        return new CustomerWorkoutSessionExerciseDto
        {
            SessionExerciseId = sessionExercise.Id,
            ExerciseId = sessionExercise.ExerciseId,
            Name = exercise?.Name ?? string.Empty,
            Order = sessionExercise.Order,
            TargetSets = target?.Sets ?? 0,
            TargetReps = target?.Reps,
            RestSeconds = target?.RestSeconds,
            DurationSeconds = target?.DurationSeconds,
            VideoUrl = exercise?.VideoUrl,
            ImageUrl = exercise?.ImageUrl,
            CompletedSets = setLogs.Select(s => new CustomerWorkoutSetLogDto
            {
                SetNumber = s.SetNumber,
                WeightKg = s.WeightKg,
                Reps = s.Reps,
                DurationSeconds = s.DurationSeconds,
                Rpe = s.Rpe,
            }).ToList(),
            PreviousBest = previousBest,
        };
    }

    private async Task<CustomerPreviousBestDto?> GetPreviousBestAsync(long userId, long exerciseId, CancellationToken cancellationToken)
    {
        var record = await _unitOfWork.Repository<PersonalRecord>()
            .GetQueryable()
            .Where(pr => pr.UserId == userId && pr.ExerciseId == exerciseId)
            .OrderByDescending(pr => pr.EstimatedOneRepMax)
            .FirstOrDefaultAsync(cancellationToken);

        return record is null ? null : new CustomerPreviousBestDto { WeightKg = record.WeightKg, Reps = record.Reps };
    }

    private async Task<Dictionary<long, CustomerPreviousBestDto>> GetPreviousBestsAsync(long userId, List<long> exerciseIds, CancellationToken cancellationToken)
    {
        var records = await _unitOfWork.Repository<PersonalRecord>()
            .GetQueryable()
            .Where(pr => pr.UserId == userId && exerciseIds.Contains(pr.ExerciseId))
            .ToListAsync(cancellationToken);

        return records
            .GroupBy(r => r.ExerciseId)
            .ToDictionary(
                g => g.Key,
                g =>
                {
                    var best = g.OrderByDescending(r => r.EstimatedOneRepMax).First();
                    return new CustomerPreviousBestDto { WeightKg = best.WeightKg, Reps = best.Reps };
                });
    }

    /// <summary>
    /// Tamamlanan oturumdaki her egzersiz icin (Epley formulu ile) tahmini 1RM hesaplar;
    /// mevcut kisisel rekoru gecen sonuclari PersonalRecord tablosuna yeni kayit olarak ekler.
    /// </summary>
    private async Task<List<CustomerNewPersonalRecordDto>> DetectAndSavePersonalRecordsAsync(long userId, long sessionId, CancellationToken cancellationToken)
    {
        var newRecords = new List<CustomerNewPersonalRecordDto>();

        var sessionExercises = await _unitOfWork.Repository<WorkoutSessionExercise>()
            .GetQueryable()
            .Where(se => se.WorkoutSessionId == sessionId)
            .ToListAsync(cancellationToken);

        if (sessionExercises.Count == 0)
        {
            return newRecords;
        }

        var sessionExerciseIds = sessionExercises.Select(se => se.Id).ToList();
        var setLogs = await _unitOfWork.Repository<WorkoutSetLog>()
            .GetQueryable()
            .Where(s => sessionExerciseIds.Contains(s.WorkoutSessionExerciseId) && s.WeightKg != null && s.Reps != null && s.Reps > 0)
            .ToListAsync(cancellationToken);

        foreach (var sessionExercise in sessionExercises)
        {
            var bestSet = setLogs
                .Where(s => s.WorkoutSessionExerciseId == sessionExercise.Id)
                .Select(s => new { Set = s, OneRepMax = EstimateOneRepMax(s.WeightKg!.Value, s.Reps!.Value) })
                .OrderByDescending(x => x.OneRepMax)
                .FirstOrDefault();

            if (bestSet is null)
            {
                continue;
            }

            var currentBest = await _unitOfWork.Repository<PersonalRecord>()
                .GetQueryable()
                .Where(pr => pr.UserId == userId && pr.ExerciseId == sessionExercise.ExerciseId)
                .OrderByDescending(pr => pr.EstimatedOneRepMax)
                .FirstOrDefaultAsync(cancellationToken);

            if (currentBest is not null && currentBest.EstimatedOneRepMax >= bestSet.OneRepMax)
            {
                continue;
            }

            var record = new PersonalRecord
            {
                UserId = userId,
                ExerciseId = sessionExercise.ExerciseId,
                WeightKg = bestSet.Set.WeightKg!.Value,
                Reps = bestSet.Set.Reps!.Value,
                EstimatedOneRepMax = bestSet.OneRepMax,
                AchievedAt = DateTime.UtcNow,
                WorkoutSessionId = sessionId,
                CreatedAt = DateTime.UtcNow,
            };
            await _unitOfWork.Repository<PersonalRecord>().AddAsync(record, cancellationToken);

            var exercise = await _unitOfWork.Repository<Exercise>().GetByIdAsync(sessionExercise.ExerciseId, cancellationToken);
            newRecords.Add(new CustomerNewPersonalRecordDto
            {
                ExerciseId = sessionExercise.ExerciseId,
                ExerciseName = exercise?.Name ?? string.Empty,
                WeightKg = record.WeightKg,
                Reps = record.Reps,
            });
        }

        return newRecords;
    }

    /// <summary>Epley formulu: 1RM = agirlik * (1 + tekrar / 30).</summary>
    private static decimal EstimateOneRepMax(decimal weightKg, int reps) => Math.Round(weightKg * (1 + (reps / 30m)), 2);

    private async Task UpdateStreakAsync(long userId, CancellationToken cancellationToken)
    {
        var streak = await _unitOfWork.Repository<UserStreak>()
            .GetQueryable(asNoTracking: false)
            .FirstOrDefaultAsync(s => s.UserId == userId, cancellationToken);

        var today = DateTime.UtcNow.Date;

        if (streak is null)
        {
            streak = new UserStreak
            {
                UserId = userId,
                CurrentStreak = 1,
                LongestStreak = 1,
                LastActivityDate = today,
                CreatedAt = DateTime.UtcNow,
            };
            await _unitOfWork.Repository<UserStreak>().AddAsync(streak, cancellationToken);
            return;
        }

        var lastActivity = streak.LastActivityDate?.Date;

        if (lastActivity == today)
        {
            // Bugun zaten bir aktivite kaydedilmis; seriyi tekrar artirma.
            return;
        }

        streak.CurrentStreak = lastActivity == today.AddDays(-1) ? streak.CurrentStreak + 1 : 1;
        streak.LongestStreak = Math.Max(streak.LongestStreak, streak.CurrentStreak);
        streak.LastActivityDate = today;
        _unitOfWork.Repository<UserStreak>().Update(streak);
    }
}
