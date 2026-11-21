using MerfitCustomerApi.Business.Common;
using MerfitCustomerApi.Business.Common.Pagination;
using MerfitCustomerApi.Business.Dtos.Customer.WorkoutSessions;
using MerfitCustomerApi.Business.Interfaces.Services;
using MerfitCustomerApi.Business.Services.Notifications.Engine;
using MerfitCustomerApi.Domain.Entities;
using MerfitCustomerApi.Domain.Entities.Enums;
using MerfitCustomerApi.Domain.Exceptions;
using MerfitCustomerApi.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

using MerfitCustomerApi.Business.Services.Energy;

namespace MerfitCustomerApi.Business.Services.WorkoutSessions;

/// <summary>
/// IWorkoutSessionService'in varsayilan implementasyonu. WorkoutSession/WorkoutSessionExercise/
/// WorkoutSetLog/PersonalRecord/UserStreak entity'lerini oldugu gibi kullanir.
/// </summary>
public class WorkoutSessionService : IWorkoutSessionService
{
    /// <summary>
    /// Exercise.EquipmentType serbest metin oldugu icin vucut agirligi egzersizleri bu ifadelerle
    /// taninir. EquipmentType bos ise de egzersiz vucut agirligi kabul edilir (agirlik opsiyonel).
    /// </summary>
    private static readonly string[] BodyweightEquipmentMarkers =
    {
        "bodyweight", "body weight", "body_weight", "vücut ağırlığı", "vucut agirligi", "ekipmansız", "ekipmansiz", "none", "yok",
    };

    private readonly IUnitOfWork _unitOfWork;
    private readonly IMerfitScoreService _scoreService;

    private readonly INotificationReminderCanceller? _reminderCanceller;
    private readonly ProgressiveOverloadOptions _progressionOptions;
    private readonly TrainingEngineOptions _engineOptions;

    public WorkoutSessionService(
        IUnitOfWork unitOfWork,
        IMerfitScoreService scoreService,
        INotificationReminderCanceller? reminderCanceller = null,
        IOptions<ProgressiveOverloadOptions>? progressionOptions = null,
        IOptions<TrainingEngineOptions>? engineOptions = null)
    {
        _engineOptions = engineOptions?.Value ?? new TrainingEngineOptions();
        _unitOfWork = unitOfWork;
        _scoreService = scoreService;
        _reminderCanceller = reminderCanceller;
        _progressionOptions = progressionOptions?.Value ?? new ProgressiveOverloadOptions();
    }

    public async Task<CustomerWorkoutSessionDto> StartSessionAsync(long userId, StartWorkoutSessionRequest request, CancellationToken cancellationToken = default)
    {
        if (request.WorkoutId is null && request.WorkoutPlanDayId is null)
        {
            throw new AppValidationException(nameof(request.WorkoutId), "WorkoutId veya WorkoutPlanDayId gönderilmelidir.");
        }

        Workout? workout = null;
        if (request.WorkoutId is long requestedWorkoutId)
        {
            workout = await _unitOfWork.Repository<Workout>().GetByIdAsync(requestedWorkoutId, cancellationToken);
            if (workout is null || !workout.IsActive)
            {
                throw new NotFoundException(nameof(Workout), requestedWorkoutId);
            }
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
            // Antrenman motoru gunlerinde katalog antrenmani yoktur (WorkoutId bos); o zaman istekte WorkoutId
            // gonderilmemeli ya da gunle ayni olmalidir. Katalog gunlerinde eski kural aynen gecerli.
            if (planDay is null || (request.WorkoutId.HasValue && planDay.WorkoutId != request.WorkoutId))
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

            if (ownerPlan.Status is TrainingProgramStatus.Proposed or TrainingProgramStatus.Rejected)
            {
                throw new AppValidationException(nameof(request.WorkoutPlanDayId), "Onaylanmamış bir program önerisinden antrenman başlatılamaz.");
            }

            if (workout is null && planDay.WorkoutId is long dayWorkoutId)
            {
                workout = await _unitOfWork.Repository<Workout>().GetByIdAsync(dayWorkoutId, cancellationToken);
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
                .Where(we => we.WorkoutId == workout!.Id)
                .OrderBy(we => we.Order)
                .ToListAsync(cancellationToken);
            exerciseOrder = catalogExercises.Select(we => (we.ExerciseId, we.Order)).ToList();
        }

        await _unitOfWork.BeginTransactionAsync(cancellationToken);

        var session = new WorkoutSession
        {
            UserId = userId,
            WorkoutId = workout?.Id,
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

    public async Task<PagedResult<CustomerWorkoutSessionHistoryItemDto>> GetHistoryAsync(long userId, PagedRequest request, CancellationToken cancellationToken = default)
    {
        var query = _unitOfWork.Repository<WorkoutSession>()
            .GetQueryable()
            .Where(s => s.UserId == userId && s.Status == WorkoutSessionStatus.Completed && s.CompletedAt != null)
            .OrderByDescending(s => s.CompletedAt);

        var page = await query.ToPagedResultAsync(request.Page, request.PageSize, cancellationToken);
        var sessions = page.Items;
        if (sessions.Count == 0)
        {
            return PagedResult<CustomerWorkoutSessionHistoryItemDto>.Create(
                Array.Empty<CustomerWorkoutSessionHistoryItemDto>(), page.Page, page.PageSize, page.TotalCount);
        }

        var sessionIds = sessions.Select(s => s.Id).ToList();
        var workoutTitles = await WorkoutSessionTitles.LoadAsync(
            _unitOfWork, sessions.Select(s => (s.WorkoutId, s.WorkoutPlanDayId)), cancellationToken);

        var sessionExercises = await _unitOfWork.Repository<WorkoutSessionExercise>()
            .GetQueryable()
            .Where(se => sessionIds.Contains(se.WorkoutSessionId))
            .ToListAsync(cancellationToken);
        var sessionExerciseIds = sessionExercises.Select(se => se.Id).ToList();

        var setLogs = await _unitOfWork.Repository<WorkoutSetLog>()
            .GetQueryable()
            .Where(l => sessionExerciseIds.Contains(l.WorkoutSessionExerciseId))
            .ToListAsync(cancellationToken);
        var sessionIdBySessionExercise = sessionExercises.ToDictionary(se => se.Id, se => se.WorkoutSessionId);
        var setsBySession = setLogs.ToLookup(l => sessionIdBySessionExercise[l.WorkoutSessionExerciseId]);

        var items = sessions.Select(s =>
        {
            // Isinma setleri hacme ve en yuksek agirliga katilmaz (eski kayitlarda isinma isareti yoktur).
            var sets = setsBySession[s.Id].Where(l => !l.IsWarmup).ToList();
            var weights = sets.Where(l => l.WeightKg is > 0).Select(l => l.WeightKg!.Value).ToList();
            return new CustomerWorkoutSessionHistoryItemDto
            {
                Id = s.Id,
                WorkoutId = s.WorkoutId,
                Title = workoutTitles.Resolve(s.WorkoutId, s.WorkoutPlanDayId),
                StartedAt = s.StartedAt,
                CompletedAt = s.CompletedAt!.Value,
                DurationSeconds = s.DurationSeconds,
                CaloriesBurned = s.CaloriesBurned,
                // Yalnizca en az bir seti loglanan egzersizler sayilir (atlanan egzersizler degil).
                ExerciseCount = sets.Select(l => l.WorkoutSessionExerciseId).Distinct().Count(),
                SetCount = sets.Count,
                TotalVolumeKg = sets.Sum(l => (l.WeightKg ?? 0) * (l.Reps ?? 0)),
                MaxWeightKg = weights.Count > 0 ? weights.Max() : null,
            };
        }).ToList();

        return PagedResult<CustomerWorkoutSessionHistoryItemDto>.Create(items, page.Page, page.PageSize, page.TotalCount);
    }

    public async Task<CustomerWorkoutSessionDto> GetSessionAsync(long userId, long sessionId, CancellationToken cancellationToken = default)
    {
        var session = await GetOwnedSessionAsync(userId, sessionId, cancellationToken);
        var title = (await WorkoutSessionTitles.LoadAsync(
            _unitOfWork, new[] { (session.WorkoutId, session.WorkoutPlanDayId) }, cancellationToken)).Resolve(session.WorkoutId, session.WorkoutPlanDayId);

        var targets = await GetExerciseTargetsAsync(session, cancellationToken);

        var sessionExercises = await _unitOfWork.Repository<WorkoutSessionExercise>()
            .GetQueryable()
            .Where(se => se.WorkoutSessionId == session.Id)
            .OrderBy(se => se.Order)
            .ToListAsync(cancellationToken);

        return await BuildSessionDtoAsync(session, targets, sessionExercises, userId, cancellationToken, title);
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

        var exercise = await _unitOfWork.Repository<Exercise>().GetByIdAsync(sessionExercise.ExerciseId, cancellationToken);
        var weightKg = NormalizeWeight(request, exercise);

        if (sessionExercise.StartedAt is null)
        {
            sessionExercise.StartedAt = DateTime.UtcNow;
            _unitOfWork.Repository<WorkoutSessionExercise>().Update(sessionExercise);
        }

        var rir = ResolveRir(request);

        // Ayni set numarasi ikinci kez gonderilirse yeni satir acilmaz, mevcut set guncellenir. Iki istek ayni anda
        // gelirse veritabanindaki unique index ikinci eklemeyi reddeder; o durumda mevcut satir guncellenir.
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
                CreatedAt = DateTime.UtcNow,
            };
            ApplySet(newSet, request, weightKg, rir);
            await _unitOfWork.Repository<WorkoutSetLog>().AddAsync(newSet, cancellationToken);
            try
            {
                await _unitOfWork.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException ex) when (DbExceptionClassifier.IsUniqueViolation(ex, out _))
            {
                _unitOfWork.ClearTracking();
                if (sessionExercise.StartedAt.HasValue)
                {
                    _unitOfWork.Repository<WorkoutSessionExercise>().Update(sessionExercise);
                }

                var concurrent = await _unitOfWork.Repository<WorkoutSetLog>()
                    .GetQueryable(asNoTracking: false)
                    .FirstAsync(s => s.WorkoutSessionExerciseId == sessionExerciseId && s.SetNumber == request.SetNumber, cancellationToken);
                ApplySet(concurrent, request, weightKg, rir);
                _unitOfWork.Repository<WorkoutSetLog>().Update(concurrent);
                await _unitOfWork.SaveChangesAsync(cancellationToken);
            }
        }
        else
        {
            ApplySet(existingSet, request, weightKg, rir);
            _unitOfWork.Repository<WorkoutSetLog>().Update(existingSet);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }

        var targets = await GetExerciseTargetsAsync(session, cancellationToken);
        var setLogs = await _unitOfWork.Repository<WorkoutSetLog>()
            .GetQueryable()
            .Where(s => s.WorkoutSessionExerciseId == sessionExerciseId)
            .OrderBy(s => s.SetNumber)
            .ToListAsync(cancellationToken);
        var previousBest = await GetPreviousBestAsync(userId, sessionExercise.ExerciseId, cancellationToken);
        var lastPerformances = await GetLastPerformancesAsync(userId, session, new List<long> { sessionExercise.ExerciseId }, cancellationToken);
        var engineContext = await LoadEngineProgressionContextAsync(userId, session, targets, new List<long> { sessionExercise.ExerciseId }, cancellationToken);

        var target = targets.TryGetValue(sessionExercise.ExerciseId, out var t) ? t : (ExerciseTargetValues?)null;
        return MapSessionExercise(sessionExercise, exercise, target, setLogs, previousBest, lastPerformances.GetValueOrDefault(sessionExercise.ExerciseId), engineContext);
    }

    public async Task<CustomerWorkoutSessionSummaryDto> CompleteSessionAsync(
        long userId,
        long sessionId,
        CompleteWorkoutSessionRequest request,
        CancellationToken cancellationToken = default)
    {
        var session = await GetOwnedSessionAsync(userId, sessionId, cancellationToken, forUpdate: true);
        EnsureSessionIsActive(session);

        // Hic set kaydi olmayan bir oturum "tamamlandi" sayilirsa dashboard ilk antrenmani yapilmis
        // gibi davranir ve gecmiste bos bir kayit gorunur; bu yuzden en az bir set zorunludur.
        var hasAnySet = await (
            from log in _unitOfWork.Repository<WorkoutSetLog>().GetQueryable()
            join se in _unitOfWork.Repository<WorkoutSessionExercise>().GetQueryable()
                on log.WorkoutSessionExerciseId equals se.Id
            where se.WorkoutSessionId == session.Id
            select log.Id
        ).AnyAsync(cancellationToken);
        if (!hasAnySet)
        {
            throw new AppValidationException("En az bir set kaydetmeden antrenman tamamlanamaz.");
        }

        session.Status = WorkoutSessionStatus.Completed;
        session.CompletedAt = DateTime.UtcNow;
        session.DurationSeconds = request.DurationSeconds;
        session.CaloriesBurned = await EstimateCaloriesAsync(userId, session, request.CaloriesBurned, cancellationToken);
        session.Notes = request.Notes;
        _unitOfWork.Repository<WorkoutSession>().Update(session);

        var newRecords = await DetectAndSavePersonalRecordsAsync(userId, session.Id, cancellationToken);
        await UpdateStreakAsync(userId, cancellationToken);

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        // MB FIT puani: basarilari ver, puan ozetini guncelle (hata antrenman kaydini bozmaz).
        await _scoreService.TryRefreshUserAsync(userId, cancellationToken);

        // Bugunun bekleyen antrenman hatirlatmasini iptal et (hata antrenman kaydini bozmaz; gonderici
        // zaten gonderimden once antrenmanin tamamlanip tamamlanmadigini kontrol eder).
        await NotificationReminderCancellation.TryRunAsync(_unitOfWork, _reminderCanceller,
            c => c.CancelWorkoutRemindersAsync(userId, session.CompletedAt!.Value, cancellationToken), cancellationToken);

        return new CustomerWorkoutSessionSummaryDto
        {
            SessionId = session.Id,
            DurationSeconds = request.DurationSeconds,
            CaloriesBurned = session.CaloriesBurned,
            NewPersonalRecords = newRecords,
        };
    }

    /// <summary>
    /// Antrenman kalorisi sunucuda, uygulama genelindeki tek formulle hesaplanir (WorkoutCalorieEstimator: net
    /// (MET-1) x kg x saat; tur ve zorluk antrenman katalogundan). Takvim, ana sayfa, ilerleme ve kalori dengesi bu
    /// saklanan degeri okur. Kilo bilinmiyorsa (profil eksik) istemcinin gonderdigi tahmin oldugu gibi saklanir.
    /// </summary>
    private async Task<decimal?> EstimateCaloriesAsync(long userId, WorkoutSession session, decimal? clientEstimate, CancellationToken cancellationToken)
    {
        var weightKg = await _unitOfWork.Repository<BodyMeasurement>().GetQueryable()
            .Where(m => m.UserId == userId && m.WeightKg != null)
            .OrderByDescending(m => m.RecordedAt).ThenByDescending(m => m.Id)
            .Select(m => m.WeightKg)
            .FirstOrDefaultAsync(cancellationToken)
            ?? await _unitOfWork.Repository<UserProfile>().GetQueryable()
                .Where(p => p.UserId == userId)
                .Select(p => p.WeightKg)
                .FirstOrDefaultAsync(cancellationToken);

        WorkoutEnergyProfile? meta = null;
        if (session.WorkoutId is long workoutId)
        {
            var profiles = await WorkoutCalorieEstimator.LoadProfilesAsync(_unitOfWork, new[] { workoutId }, cancellationToken);
            profiles.TryGetValue(workoutId, out meta);
        }
        else if (session.WorkoutPlanDayId.HasValue)
        {
            // Motor plan gunu: katalog antrenmani yok; direnc antrenmani, orta zorluk varsayimi.
            meta = WorkoutEnergyProfile.ForEngineSession(string.Empty);
        }

        if (meta is null)
        {
            return clientEstimate;
        }

        var activeSeconds = WorkoutCalorieEstimator.ActiveSeconds(
            session.DurationSeconds, session.StartedAt, session.CompletedAt, MaxCaloriesWorkoutMinutes);
        var estimate = WorkoutCalorieEstimator.EstimateNetKcal(meta.Kind, meta.Difficulty, weightKg, activeSeconds);
        return estimate is null ? clientEstimate : estimate.Value;
    }

    /// <summary>Kaloriye sayilan en uzun antrenman suresi (EnergyBalanceOptions.MaxWorkoutMinutes varsayilani ile ayni).</summary>
    private const int MaxCaloriesWorkoutMinutes = 240;

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
        var lastPerformances = await GetLastPerformancesAsync(userId, session, exerciseIds, cancellationToken);
        var engineContext = await LoadEngineProgressionContextAsync(userId, session, targets, exerciseIds, cancellationToken);

        var title = workoutTitleFallback ?? (await WorkoutSessionTitles.LoadAsync(
            _unitOfWork, new[] { (session.WorkoutId, session.WorkoutPlanDayId) }, cancellationToken)).Resolve(session.WorkoutId, session.WorkoutPlanDayId);

        var exerciseDtos = sessionExercises.Select(se =>
        {
            var target = targets.TryGetValue(se.ExerciseId, out var t) ? t : (ExerciseTargetValues?)null;
            var exercise = exercises.GetValueOrDefault(se.ExerciseId);
            var setLogs = allSetLogs.Where(s => s.WorkoutSessionExerciseId == se.Id).ToList();
            var previousBest = previousBests.GetValueOrDefault(se.ExerciseId);

            return MapSessionExercise(se, exercise, target, setLogs, previousBest, lastPerformances.GetValueOrDefault(se.ExerciseId), engineContext);
        }).ToList();

        return new CustomerWorkoutSessionDto
        {
            Id = session.Id,
            WorkoutId = session.WorkoutId,
            WorkoutPlanDayId = session.WorkoutPlanDayId,
            Title = title,
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
    private readonly record struct ExerciseTargetValues(
        int Sets, int? Reps, int? RestSeconds, int? DurationSeconds, int? RepsMax = null, int? TargetRir = null,
        WeightGuidanceType? WeightGuidance = null, decimal? SuggestedWeightKg = null);

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
                .ToDictionary(g => g.Key, g => new ExerciseTargetValues(
                    g.First().Sets, g.First().Reps, g.First().RestSeconds, g.First().DurationSeconds, g.First().RepsMax,
                    g.First().TargetRir, g.First().WeightGuidance, g.First().SuggestedWeightKg));
        }

        var catalogExercises = await _unitOfWork.Repository<WorkoutExercise>()
            .GetQueryable()
            .Where(we => we.WorkoutId == session.WorkoutId)
            .ToListAsync(cancellationToken);
        if (!session.WorkoutId.HasValue)
        {
            return new Dictionary<long, ExerciseTargetValues>();
        }

        return catalogExercises
            .GroupBy(we => we.ExerciseId)
            .ToDictionary(g => g.Key, g => new ExerciseTargetValues(g.First().Sets, g.First().Reps, g.First().RestSeconds, g.First().DurationSeconds));
    }

    private CustomerWorkoutSessionExerciseDto MapSessionExercise(
        WorkoutSessionExercise sessionExercise,
        Exercise? exercise,
        ExerciseTargetValues? target,
        List<WorkoutSetLog> setLogs,
        CustomerPreviousBestDto? previousBest,
        CustomerLastPerformanceDto? lastPerformance,
        EngineProgressionContext? engineContext = null)
    {
        return new CustomerWorkoutSessionExerciseDto
        {
            SessionExerciseId = sessionExercise.Id,
            ExerciseId = sessionExercise.ExerciseId,
            Name = exercise?.Name ?? string.Empty,
            Order = sessionExercise.Order,
            TargetSets = target?.Sets ?? 0,
            TargetReps = target?.Reps,
            TargetRepsMax = target?.RepsMax,
            RestSeconds = target?.RestSeconds,
            DurationSeconds = target?.DurationSeconds,
            VideoUrl = exercise?.VideoUrl,
            ImageUrl = exercise?.ImageUrl,
            CompletedSets = setLogs.Select(MapSetLog).ToList(),
            PreviousBest = previousBest,
            RequiresWeight = RequiresWeight(exercise),
            LastPerformance = lastPerformance,
            TargetRir = target?.TargetRir,
            WeightGuidance = target?.WeightGuidance?.ToString(),
            SuggestedStartWeightKg = target?.SuggestedWeightKg,
            // Sureye dayali hedefte (tekrar yok) oneri uretilmez. Antrenman motorunun planlarinda (hedef RIR var)
            // RIR, egilim ve rahatsizlik bildirimini dikkate alan ProgressionEngine; diger planlarda eski kural.
            Progression = target is { Reps: > 0 }
                ? target.Value.TargetRir.HasValue && engineContext is not null
                    ? EngineSuggestion(sessionExercise.ExerciseId, exercise, target.Value, engineContext)
                    : ProgressiveOverloadAdvisor.Suggest(target.Value.Sets, target.Value.Reps, target.Value.RepsMax, lastPerformance?.Sets, _progressionOptions)
                : null,
        };
    }

    private static CustomerWorkoutSetLogDto MapSetLog(WorkoutSetLog s) => new()
    {
        SetNumber = s.SetNumber,
        WeightKg = s.WeightKg,
        Reps = s.Reps,
        DurationSeconds = s.DurationSeconds,
        Rpe = s.Rpe,
        Rir = s.Rir,
        IsWarmup = s.IsWarmup,
        IsCompleted = s.IsCompleted,
    };

    /// <summary>RIR dogrudan girildiyse o; yalnizca RPE girildiyse RIR = 10 - RPE (yuvarlanir, 0-10).</summary>
    private static int? ResolveRir(LogWorkoutSetRequest request)
    {
        if (request.Rir.HasValue)
        {
            return request.Rir;
        }

        return request.Rpe is decimal rpe ? (int)Math.Clamp(Math.Round(10 - rpe, MidpointRounding.AwayFromZero), 0, 10) : null;
    }

    private static void ApplySet(WorkoutSetLog set, LogWorkoutSetRequest request, decimal? weightKg, int? rir)
    {
        set.WeightKg = weightKg;
        set.Reps = request.Reps;
        set.DurationSeconds = request.DurationSeconds;
        set.Rpe = request.Rpe;
        set.Rir = rir;
        set.IsWarmup = request.IsWarmup ?? false;
        set.IsCompleted = request.IsCompleted ?? true;
        set.CompletedAt = DateTime.UtcNow;
    }

    /// <summary>Motor progresyonu icin bu oturumdan onceki maruziyetler ve rahatsizlik bildirimleri.</summary>
    private sealed record EngineProgressionContext(
        IReadOnlyDictionary<long, IReadOnlyList<Training.ExerciseExposure>> Exposures,
        IReadOnlySet<long> DiscomfortExerciseIds,
        IReadOnlyDictionary<long, Training.CatalogExercise> Catalog);

    private async Task<EngineProgressionContext?> LoadEngineProgressionContextAsync(
        long userId,
        WorkoutSession session,
        Dictionary<long, ExerciseTargetValues> targets,
        List<long> exerciseIds,
        CancellationToken cancellationToken)
    {
        var engineExerciseIds = exerciseIds.Where(id => targets.TryGetValue(id, out var t) && t.TargetRir.HasValue).ToList();
        if (engineExerciseIds.Count == 0)
        {
            return null;
        }

        var exposures = await Training.TrainingHistoryReader.LoadExposuresAsync(
            _unitOfWork, userId, engineExerciseIds, before: session.StartedAt, excludeSessionId: session.Id, maxPerExercise: 6, cancellationToken);
        var discomfort = await Training.TrainingHistoryReader.LoadRecentDiscomfortAsync(
            _unitOfWork, userId, DateTime.UtcNow.AddDays(-_engineOptions.DiscomfortLookbackDays), cancellationToken);
        var catalog = await Training.TrainingHistoryReader.LoadCatalogAsync(_unitOfWork, _engineOptions, engineExerciseIds, cancellationToken);
        return new EngineProgressionContext(exposures, discomfort, catalog);
    }

    private CustomerProgressionSuggestionDto EngineSuggestion(
        long exerciseId, Exercise? exercise, ExerciseTargetValues target, EngineProgressionContext context)
    {
        var catalog = context.Catalog.GetValueOrDefault(exerciseId);
        var decision = Training.ProgressionEngine.Decide(
            new Training.ProgressionInput(
                target.Sets,
                target.Reps ?? 1,
                target.RepsMax ?? target.Reps ?? 1,
                target.TargetRir,
                catalog?.LoadIncrementKg ?? 2.5m,
                catalog?.IsBodyweight ?? !RequiresWeight(exercise),
                context.DiscomfortExerciseIds.Contains(exerciseId)),
            context.Exposures.GetValueOrDefault(exerciseId) ?? Array.Empty<Training.ExerciseExposure>(),
            _engineOptions);

        return new CustomerProgressionSuggestionDto
        {
            Action = decision.Action,
            PreviousWeightKg = decision.PreviousWeightKg,
            SuggestedWeightKg = decision.SuggestedWeightKg,
            IncreaseKg = decision.ChangeKg,
            TargetRepsMin = decision.TargetRepsMin,
            TargetRepsMax = decision.TargetRepsMax,
            Message = decision.Message,
            TargetRir = target.TargetRir,
            IsConfident = decision.IsConfident,
            Trend = decision.Trend.ToString(),
            Reasons = decision.Reasons.ToList(),
        };
    }

    public async Task<CustomerWorkoutFeedbackDto> SubmitFeedbackAsync(
        long userId, long sessionId, SubmitWorkoutFeedbackRequest request, CancellationToken cancellationToken = default)
    {
        var session = await GetOwnedSessionAsync(userId, sessionId, cancellationToken);
        if (session.Status != WorkoutSessionStatus.Completed)
        {
            throw new AppValidationException("Değerlendirme yalnızca tamamlanan antrenman için gönderilebilir.");
        }

        var discomfortIds = (request.DiscomfortExerciseIds ?? new List<long>()).Distinct().ToList();
        if (discomfortIds.Count > 0)
        {
            var sessionExerciseIds = await _unitOfWork.Repository<WorkoutSessionExercise>().GetQueryable()
                .Where(se => se.WorkoutSessionId == sessionId)
                .Select(se => se.ExerciseId)
                .ToListAsync(cancellationToken);
            if (discomfortIds.Any(id => !sessionExerciseIds.Contains(id)))
            {
                throw new AppValidationException(nameof(request.DiscomfortExerciseIds), "Rahatsızlık bildirilen egzersiz bu antrenmanda yok.");
            }
        }

        var repo = _unitOfWork.Repository<WorkoutSessionFeedback>();
        var feedback = await repo.GetQueryable(asNoTracking: false).FirstOrDefaultAsync(f => f.WorkoutSessionId == sessionId, cancellationToken);
        var isNew = feedback is null;
        feedback ??= new WorkoutSessionFeedback { WorkoutSessionId = sessionId, UserId = userId, CreatedAt = DateTime.UtcNow };
        feedback.Difficulty = request.Difficulty;
        feedback.Fatigue = request.Fatigue;
        feedback.Recovery = request.Recovery;
        feedback.Satisfaction = request.Satisfaction;
        feedback.HasDiscomfort = request.HasDiscomfort || discomfortIds.Count > 0;
        feedback.DiscomfortExerciseIds = discomfortIds.Count > 0 ? string.Join(',', discomfortIds) : null;
        feedback.Note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim();
        if (isNew)
        {
            await repo.AddAsync(feedback, cancellationToken);
        }
        else
        {
            feedback.UpdatedAt = DateTime.UtcNow;
            repo.Update(feedback);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new CustomerWorkoutFeedbackDto
        {
            SessionId = sessionId,
            Difficulty = feedback.Difficulty,
            Fatigue = feedback.Fatigue,
            Recovery = feedback.Recovery,
            Satisfaction = feedback.Satisfaction,
            HasDiscomfort = feedback.HasDiscomfort,
            DiscomfortExerciseIds = discomfortIds,
            Note = feedback.Note,
            SafetyMessage = feedback.HasDiscomfort
                ? "Rahatsızlık bildirdiğin hareketlerde ağırlık artışı durduruldu. Ağrı yapan hareketi zorlama, alternatifini kullanabilirsin. Ağrı sürerse veya şiddetliyse bir sağlık profesyoneline danış."
                : null,
        };
    }

    /// <summary>Egzersiz harici agirlik gerektiriyor mu (bkz. <see cref="BodyweightEquipmentMarkers"/>).</summary>
    private static bool RequiresWeight(Exercise? exercise)
    {
        var equipment = exercise?.EquipmentType;
        if (string.IsNullOrWhiteSpace(equipment))
        {
            return false;
        }

        return !BodyweightEquipmentMarkers.Any(m => equipment.Contains(m, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Set agirligini dogrular: agirlik gerektiren egzersizde 0/bos agirlik reddedilir; vucut agirligi
    /// egzersizinde 0 "ek agirlik yok" demektir ve null olarak saklanir (rekor/hacim hesaplarini bozmasin).
    /// </summary>
    private static decimal? NormalizeWeight(LogWorkoutSetRequest request, Exercise? exercise)
    {
        if (request.Reps is null && request.DurationSeconds is null)
        {
            throw new AppValidationException(nameof(request.Reps), "Tekrar sayisi veya sure girilmelidir.");
        }

        if (request.WeightKg is > 0)
        {
            return request.WeightKg;
        }

        if (RequiresWeight(exercise))
        {
            throw new AppValidationException(nameof(request.WeightKg), "Bu egzersiz icin 0'dan buyuk bir agirlik girilmelidir.");
        }

        return null;
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

    /// <summary>
    /// Her egzersiz icin kullanicinin bu oturumdan once tamamladigi en son oturumdaki setleri getirir
    /// ("Geçen sefer: 3x10 @ 40 kg"). Uygulama bunu ilk deger ve referans olarak gosterir.
    /// </summary>
    private async Task<Dictionary<long, CustomerLastPerformanceDto>> GetLastPerformancesAsync(
        long userId,
        WorkoutSession currentSession,
        List<long> exerciseIds,
        CancellationToken cancellationToken)
    {
        if (exerciseIds.Count == 0)
        {
            return new Dictionary<long, CustomerLastPerformanceDto>();
        }

        var cutoff = currentSession.CompletedAt ?? currentSession.StartedAt;
        var currentSessionId = currentSession.Id;
        var candidates = await (
            from se in _unitOfWork.Repository<WorkoutSessionExercise>().GetQueryable()
            join s in _unitOfWork.Repository<WorkoutSession>().GetQueryable()
                on se.WorkoutSessionId equals s.Id
            where s.UserId == userId
                && s.Id != currentSessionId
                && s.Status == WorkoutSessionStatus.Completed
                && s.CompletedAt != null
                && s.CompletedAt <= cutoff
                && exerciseIds.Contains(se.ExerciseId)
            select new { SessionExerciseId = se.Id, se.ExerciseId, SessionId = s.Id, CompletedAt = s.CompletedAt!.Value }
        ).ToListAsync(cancellationToken);

        if (candidates.Count == 0)
        {
            return new Dictionary<long, CustomerLastPerformanceDto>();
        }

        var candidateIds = candidates.Select(c => c.SessionExerciseId).ToList();
        var logs = await _unitOfWork.Repository<WorkoutSetLog>()
            .GetQueryable()
            .Where(l => candidateIds.Contains(l.WorkoutSessionExerciseId))
            .ToListAsync(cancellationToken);
        var logsBySessionExercise = logs.ToLookup(l => l.WorkoutSessionExerciseId);

        var result = new Dictionary<long, CustomerLastPerformanceDto>();
        foreach (var group in candidates.GroupBy(c => c.ExerciseId))
        {
            // Setsiz (atlanmis) egzersiz kayitlarini gec; en son gercekten yapilan oturumu al.
            var latest = group
                .Where(c => logsBySessionExercise[c.SessionExerciseId].Any())
                .OrderByDescending(c => c.CompletedAt)
                .FirstOrDefault();
            if (latest is null)
            {
                continue;
            }

            result[group.Key] = new CustomerLastPerformanceDto
            {
                SessionId = latest.SessionId,
                CompletedAt = latest.CompletedAt,
                Sets = logsBySessionExercise[latest.SessionExerciseId].OrderBy(l => l.SetNumber).Select(MapSetLog).ToList(),
            };
        }

        return result;
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
            .Where(s => sessionExerciseIds.Contains(s.WorkoutSessionExerciseId) && !s.IsWarmup && s.WeightKg != null && s.Reps != null && s.Reps > 0)
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
