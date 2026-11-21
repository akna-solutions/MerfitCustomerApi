using MerfitCustomerApi.Business.Common;
using MerfitCustomerApi.Domain.Entities;
using MerfitCustomerApi.Domain.Entities.Enums;
using MerfitCustomerApi.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace MerfitCustomerApi.Business.Services.Training;

/// <summary>
/// Motorun veritabanindan okudugu veriler (salt okunur, toplu sorgular). Gecmis performans tek kaynaktan,
/// WorkoutSetLog kayitlarindan hesaplanir; ayri bir performans tablosu tutulmaz.
/// </summary>
public static class TrainingHistoryReader
{
    /// <summary>Katalogdaki aktif egzersizler (istenirse yalnizca verilen kimlikler) motor modeline cevrilmis olarak.</summary>
    public static async Task<IReadOnlyDictionary<long, CatalogExercise>> LoadCatalogAsync(
        IUnitOfWork unitOfWork, TrainingEngineOptions options, IReadOnlyCollection<long>? onlyIds, CancellationToken cancellationToken)
    {
        var exerciseQuery = unitOfWork.Repository<Exercise>().GetQueryable();
        exerciseQuery = onlyIds is null ? exerciseQuery.Where(e => e.IsActive) : exerciseQuery.Where(e => onlyIds.Contains(e.Id));
        var exercises = await exerciseQuery.ToListAsync(cancellationToken);
        if (exercises.Count == 0)
        {
            return new Dictionary<long, CatalogExercise>();
        }

        var ids = exercises.Select(e => e.Id).ToList();
        var muscleGroups = await unitOfWork.Repository<MuscleGroup>().GetQueryable()
            .Select(m => new { m.Id, m.Name, m.Slug })
            .ToListAsync(cancellationToken);
        var regionById = muscleGroups.ToDictionary(m => m.Id, m => MuscleRegionMapper.Map(m.Name, m.Slug, options.MuscleGroupAliases));

        var muscleLinks = await unitOfWork.Repository<ExerciseMuscleGroup>().GetQueryable()
            .Where(l => ids.Contains(l.ExerciseId) && l.Role == ExerciseMuscleRole.Secondary)
            .Select(l => new { l.ExerciseId, l.MuscleGroupId })
            .ToListAsync(cancellationToken);
        var secondaryByExercise = muscleLinks.ToLookup(l => l.ExerciseId, l => l.MuscleGroupId);

        var equipmentLinks = await unitOfWork.Repository<ExerciseEquipment>().GetQueryable()
            .Where(l => ids.Contains(l.ExerciseId))
            .Select(l => new { l.ExerciseId, l.EquipmentId })
            .ToListAsync(cancellationToken);
        var equipmentByExercise = equipmentLinks.ToLookup(l => l.ExerciseId, l => l.EquipmentId);

        var equipment = (await unitOfWork.Repository<Domain.Entities.Equipment>().GetQueryable()
                .Select(e => new { e.Id, e.Name, e.Slug })
                .ToListAsync(cancellationToken))
            .Select(e => new ExerciseMetadataResolver.EquipmentRow(e.Id, e.Name, e.Slug))
            .ToList();

        // Katalog antrenmanlarinda cogunlukla sureyle (tekrarsiz) yazilan egzersizler sureye dayali kabul edilir.
        var usage = await unitOfWork.Repository<WorkoutExercise>().GetQueryable()
            .Where(we => ids.Contains(we.ExerciseId))
            .Select(we => new { we.ExerciseId, TimeBased = we.DurationSeconds > 0 && (we.Reps == null || we.Reps == 0) })
            .ToListAsync(cancellationToken);
        var timeBased = usage.GroupBy(u => u.ExerciseId)
            .Where(g => g.Count(x => x.TimeBased) * 2 > g.Count())
            .Select(g => g.Key)
            .ToHashSet();

        return exercises.ToDictionary(
            e => e.Id,
            e => ExerciseMetadataResolver.Resolve(new ExerciseMetadataResolver.MetadataInput(
                e,
                regionById,
                secondaryByExercise[e.Id].ToList(),
                equipmentByExercise[e.Id].ToList(),
                equipment,
                timeBased.Contains(e.Id))));
    }

    /// <summary>
    /// Tamamlanmis oturumlardaki maruziyetler (eskiden yeniye). Setsiz egzersiz kayitlari atlanir.
    /// </summary>
    public static async Task<IReadOnlyDictionary<long, IReadOnlyList<ExerciseExposure>>> LoadExposuresAsync(
        IUnitOfWork unitOfWork,
        long userId,
        IReadOnlyCollection<long>? exerciseIds,
        DateTime? before,
        long? excludeSessionId,
        int maxPerExercise,
        CancellationToken cancellationToken,
        DateTime? since = null)
    {
        var query =
            from se in unitOfWork.Repository<WorkoutSessionExercise>().GetQueryable()
            join s in unitOfWork.Repository<WorkoutSession>().GetQueryable() on se.WorkoutSessionId equals s.Id
            where s.UserId == userId && s.Status == WorkoutSessionStatus.Completed && s.CompletedAt != null
            select new { SessionExerciseId = se.Id, se.ExerciseId, SessionId = s.Id, CompletedAt = s.CompletedAt!.Value };
        if (exerciseIds is not null)
        {
            query = query.Where(x => exerciseIds.Contains(x.ExerciseId));
        }
        if (before.HasValue)
        {
            var cutoff = before.Value;
            query = query.Where(x => x.CompletedAt <= cutoff);
        }
        if (since.HasValue)
        {
            var from = since.Value;
            query = query.Where(x => x.CompletedAt >= from);
        }
        if (excludeSessionId.HasValue)
        {
            var excluded = excludeSessionId.Value;
            query = query.Where(x => x.SessionId != excluded);
        }

        var rows = await query.ToListAsync(cancellationToken);
        if (rows.Count == 0)
        {
            return new Dictionary<long, IReadOnlyList<ExerciseExposure>>();
        }

        var sessionExerciseIds = rows.Select(r => r.SessionExerciseId).ToList();
        var logs = await unitOfWork.Repository<WorkoutSetLog>().GetQueryable()
            .Where(l => sessionExerciseIds.Contains(l.WorkoutSessionExerciseId))
            .Select(l => new { l.WorkoutSessionExerciseId, l.SetNumber, l.WeightKg, l.Reps, l.Rir, l.Rpe, l.IsCompleted, l.IsWarmup })
            .ToListAsync(cancellationToken);
        var logsBySessionExercise = logs.ToLookup(l => l.WorkoutSessionExerciseId);

        var result = new Dictionary<long, IReadOnlyList<ExerciseExposure>>();
        foreach (var group in rows.GroupBy(r => r.ExerciseId))
        {
            var exposures = group
                .Select(r => new ExerciseExposure(
                    r.ExerciseId,
                    r.SessionId,
                    r.CompletedAt,
                    logsBySessionExercise[r.SessionExerciseId]
                        .OrderBy(l => l.SetNumber)
                        .Select(l => new PerformedSet(
                            l.SetNumber, l.WeightKg, l.Reps,
                            l.Rir ?? (l.Rpe is decimal rpe ? (int)Math.Clamp(Math.Round(10 - rpe, MidpointRounding.AwayFromZero), 0, 10) : null),
                            l.IsCompleted, l.IsWarmup))
                        .ToList()))
                .Where(e => e.Sets.Count > 0)
                .OrderBy(e => e.CompletedAt)
                .ThenBy(e => e.SessionId)
                .ToList();
            if (exposures.Count > 0)
            {
                result[group.Key] = exposures.TakeLast(maxPerExercise).ToList();
            }
        }

        return result;
    }

    /// <summary>Verilen tarihten beri rahatsizlik bildirilen egzersiz kimlikleri.</summary>
    public static async Task<IReadOnlySet<long>> LoadRecentDiscomfortAsync(IUnitOfWork unitOfWork, long userId, DateTime since, CancellationToken cancellationToken)
    {
        var rows = await unitOfWork.Repository<WorkoutSessionFeedback>().GetQueryable()
            .Where(f => f.UserId == userId && f.HasDiscomfort && f.CreatedAt >= since && f.DiscomfortExerciseIds != null)
            .Select(f => f.DiscomfortExerciseIds!)
            .ToListAsync(cancellationToken);
        return rows.SelectMany(IdList.Parse).ToHashSet();
    }

    public static async Task<IReadOnlyList<FeedbackSignal>> LoadFeedbackAsync(IUnitOfWork unitOfWork, long userId, DateTime since, CancellationToken cancellationToken)
    {
        var rows = await unitOfWork.Repository<WorkoutSessionFeedback>().GetQueryable()
            .Where(f => f.UserId == userId && f.CreatedAt >= since)
            .OrderBy(f => f.CreatedAt)
            .ToListAsync(cancellationToken);
        return rows.Select(f => new FeedbackSignal(
                f.WorkoutSessionId, f.CreatedAt, f.Difficulty, f.Fatigue, f.Recovery, f.Satisfaction, f.HasDiscomfort,
                IdList.Parse(f.DiscomfortExerciseIds).ToList()))
            .ToList();
    }

    /// <summary>
    /// Son 4 haftadaki devamlilik: planli gun sayisi (o tarihlerde aktif olan planlarin gunleri, hafta hafta) ve
    /// tamamlanan plan oturumu sayisi. Planin baslangicindan onceki haftalar sayilmaz.
    /// </summary>
    public static async Task<(int Scheduled, int Completed)> LoadAdherenceAsync(IUnitOfWork unitOfWork, long userId, DateTime now, CancellationToken cancellationToken)
    {
        var windowStart = now.Date.AddDays(-28);
        var plans = await unitOfWork.Repository<WorkoutPlan>().GetQueryable()
            .Where(p => p.UserId == userId
                && (p.Status == TrainingProgramStatus.Active || p.Status == TrainingProgramStatus.Archived)
                && (p.EndDate == null || p.EndDate >= windowStart))
            .Select(p => new { p.Id, p.StartDate, p.EndDate, p.ArchivedAt })
            .ToListAsync(cancellationToken);
        if (plans.Count == 0)
        {
            return (0, 0);
        }

        var planIds = plans.Select(p => p.Id).ToList();
        var days = await unitOfWork.Repository<WorkoutPlanDay>().GetQueryable()
            .Where(d => planIds.Contains(d.WorkoutPlanId))
            .Select(d => new { d.WorkoutPlanId, d.DayOfWeek })
            .ToListAsync(cancellationToken);
        var daysByPlan = days.ToLookup(d => d.WorkoutPlanId, d => d.DayOfWeek);

        var scheduled = 0;
        for (var date = windowStart; date < now.Date; date = date.AddDays(1))
        {
            var activePlan = plans.FirstOrDefault(p => p.StartDate.Date <= date && (p.EndDate ?? p.ArchivedAt ?? DateTime.MaxValue).Date > date);
            if (activePlan is not null && daysByPlan[activePlan.Id].Contains(date.DayOfWeek))
            {
                scheduled++;
            }
        }

        var completed = await unitOfWork.Repository<WorkoutSession>().GetQueryable()
            .CountAsync(s => s.UserId == userId
                && s.Status == WorkoutSessionStatus.Completed
                && s.WorkoutPlanDayId != null
                && s.CompletedAt >= windowStart
                && s.CompletedAt < now.Date,
                cancellationToken);

        return (scheduled, completed);
    }
}

/// <summary>Virgulle ayrilmis kimlik listeleri (mevcut PreferredTrainingDays / ExcludedFoodIds deseni).</summary>
public static class IdList
{
    public static IEnumerable<long> Parse(string? value) => string.IsNullOrWhiteSpace(value)
        ? Enumerable.Empty<long>()
        : value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(x => long.TryParse(x, out var id) ? id : 0)
            .Where(id => id > 0)
            .Distinct();

    public static string? Format(IEnumerable<long>? ids)
    {
        var list = ids?.Where(id => id > 0).Distinct().OrderBy(x => x).ToList();
        return list is null || list.Count == 0 ? null : string.Join(',', list);
    }
}
