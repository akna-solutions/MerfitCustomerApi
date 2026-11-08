using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MerfitCustomerApi.Business.Common;
using MerfitCustomerApi.Business.Interfaces.Services;
using MerfitCustomerApi.Business.Services.Personalization.Generators;
using MerfitCustomerApi.Business.Services.Personalization.Models;
using MerfitCustomerApi.Domain.Entities;
using MerfitCustomerApi.Domain.Entities.Enums;
using MerfitCustomerApi.Domain.Exceptions;
using MerfitCustomerApi.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MerfitCustomerApi.Business.Services.WorkoutLab;

/// <summary>
/// Antrenman Laboratuvari kuyrugunu isler: admin API'sinin (MerfitApi) Pending olarak actigi bir
/// <see cref="WorkoutPlanSimulation"/> kaydini alir, profil tercihlerini <see cref="WorkoutPlanGenerationInput"/>'a
/// cevirir ve gercek <see cref="IWorkoutPlanGenerator"/>'i calistirir. Sonucu (program anlik goruntusu + kalite
/// kontrolleri) yalnizca ayni simulasyon satirina yazar.
///
/// Kullanici verisine dokunmaz: UserProfile/UserGoal/UserEquipment/Subscription okunmaz; WorkoutPlan,
/// PersonalizationJob veya bildirim kaydi olusturulmaz. Generator yalnizca katalogu okur.
/// </summary>
public class WorkoutPlanSimulationProcessor : IWorkoutPlanSimulationProcessor
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IWorkoutPlanGenerator _generator;
    private readonly WorkoutPlanGenerationOptions _generationOptions;
    private readonly WorkoutPlanLabOptions _labOptions;
    private readonly ILogger<WorkoutPlanSimulationProcessor> _logger;

    public WorkoutPlanSimulationProcessor(
        IUnitOfWork unitOfWork,
        IWorkoutPlanGenerator generator,
        IOptions<WorkoutPlanGenerationOptions> generationOptions,
        IOptions<WorkoutPlanLabOptions> labOptions,
        ILogger<WorkoutPlanSimulationProcessor> logger)
    {
        _unitOfWork = unitOfWork;
        _generator = generator;
        _generationOptions = generationOptions.Value;
        _labOptions = labOptions.Value;
        _logger = logger;
    }

    public async Task<bool> ProcessNextAsync(CancellationToken cancellationToken = default)
    {
        var simulation = await TryClaimNextAsync(cancellationToken);
        if (simulation is null)
        {
            return false;
        }

        try
        {
            var profile = JsonSerializer.Deserialize<WorkoutPlanSimulationProfile>(simulation.ProfileJson, WorkoutLabJson.Options)
                ?? new WorkoutPlanSimulationProfile();

            var (input, equipment, resolution, unknownIds) = await BuildInputAsync(profile, cancellationToken);
            var generated = await _generator.GenerateAsync(input, cancellationToken);
            var result = await BuildResultAsync(generated, profile, equipment, resolution, unknownIds, cancellationToken);
            var checks = WorkoutPlanQualityChecker.Evaluate(result, profile, _labOptions);

            simulation.Status = WorkoutPlanSimulationStatus.Completed;
            simulation.AlgorithmVersion = result.AlgorithmVersion;
            simulation.OptionsFingerprint = result.OptionsFingerprint;
            simulation.ResultJson = JsonSerializer.Serialize(result, WorkoutLabJson.Options);
            simulation.QualityChecksJson = JsonSerializer.Serialize(checks, WorkoutLabJson.Options);
            simulation.PassedCheckCount = checks.Count(c => c.Status == WorkoutPlanQualityCheckStatus.Passed);
            simulation.WarningCheckCount = checks.Count(c => c.Status == WorkoutPlanQualityCheckStatus.Warning);
            simulation.FailedCheckCount = checks.Count(c => c.Status == WorkoutPlanQualityCheckStatus.Failed);
            simulation.ErrorMessage = null;
        }
        catch (PersonalizationGenerationException ex)
        {
            // Algoritmanin gercek "program uretilemedi" sonucu (orn. bos katalog); tekrar denemek ayni sonucu verir.
            simulation.Status = WorkoutPlanSimulationStatus.Failed;
            simulation.AlgorithmVersion = WorkoutPlanGenerator.AlgorithmVersion;
            simulation.OptionsFingerprint = ComputeOptionsFingerprint(_generationOptions);
            simulation.ErrorMessage = Truncate(ex.Message, 2000);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning("Workout plan simulation {SimulationId} failed: {Message}", simulation.Id, ex.Message);
            _unitOfWork.ClearTracking();
            simulation = await _unitOfWork.Repository<WorkoutPlanSimulation>()
                .GetQueryable(asNoTracking: false)
                .FirstAsync(s => s.Id == simulation.Id, cancellationToken);
            simulation.Status = simulation.AttemptCount < _labOptions.MaxAttemptCount
                ? WorkoutPlanSimulationStatus.Pending
                : WorkoutPlanSimulationStatus.Failed;
            simulation.ErrorMessage = Truncate($"Simülasyon işlenemedi: {ex.Message}", 2000);
        }

        simulation.CompletedAt = simulation.Status is WorkoutPlanSimulationStatus.Completed or WorkoutPlanSimulationStatus.Failed
            ? DateTime.UtcNow
            : null;
        simulation.UpdatedAt = DateTime.UtcNow;
        _unitOfWork.Repository<WorkoutPlanSimulation>().Update(simulation);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        _unitOfWork.ClearTracking();

        _logger.LogInformation("Workout plan simulation {SimulationId} finished with status {Status}", simulation.Id, simulation.Status);
        return true;
    }

    /// <summary>
    /// En eski bekleyen (veya zaman asimina ugramis Processing) kaydi, PostgreSQL xmin optimistic concurrency ile
    /// alir (bkz. AppDbContext); ayni kaydi iki worker alamaz.
    /// </summary>
    private async Task<WorkoutPlanSimulation?> TryClaimNextAsync(CancellationToken cancellationToken)
    {
        var repo = _unitOfWork.Repository<WorkoutPlanSimulation>();
        var staleCutoff = DateTime.UtcNow - _labOptions.ProcessingTimeout;

        var candidateIds = await repo.GetQueryable()
            .Where(s => s.AttemptCount < _labOptions.MaxAttemptCount
                && (s.Status == WorkoutPlanSimulationStatus.Pending
                    || (s.Status == WorkoutPlanSimulationStatus.Processing && s.StartedAt < staleCutoff)))
            .OrderBy(s => s.Id)
            .Select(s => s.Id)
            .Take(3)
            .ToListAsync(cancellationToken);

        foreach (var id in candidateIds)
        {
            var simulation = await repo.GetQueryable(asNoTracking: false).FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
            if (simulation is null
                || !(simulation.Status == WorkoutPlanSimulationStatus.Pending
                    || (simulation.Status == WorkoutPlanSimulationStatus.Processing && simulation.StartedAt < staleCutoff)))
            {
                continue;
            }

            simulation.Status = WorkoutPlanSimulationStatus.Processing;
            simulation.StartedAt = DateTime.UtcNow;
            simulation.AttemptCount += 1;
            simulation.UpdatedAt = DateTime.UtcNow;
            repo.Update(simulation);

            try
            {
                await _unitOfWork.SaveChangesAsync(cancellationToken);
                return simulation;
            }
            catch (DbUpdateConcurrencyException)
            {
                _unitOfWork.ClearTracking();
            }
        }

        // Deneme hakki biten ve zaman asimina ugrayan kayitlar sonsuza dek Processing'de kalmasin.
        var abandoned = await repo.GetQueryable(asNoTracking: false)
            .Where(s => s.Status == WorkoutPlanSimulationStatus.Processing
                && s.StartedAt < staleCutoff
                && s.AttemptCount >= _labOptions.MaxAttemptCount)
            .Take(10)
            .ToListAsync(cancellationToken);
        if (abandoned.Count > 0)
        {
            foreach (var item in abandoned)
            {
                item.Status = WorkoutPlanSimulationStatus.Failed;
                item.ErrorMessage ??= "Simülasyon zaman aşımına uğradı.";
                item.CompletedAt = DateTime.UtcNow;
                item.UpdatedAt = DateTime.UtcNow;
                repo.Update(item);
            }

            try
            {
                await _unitOfWork.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                // Bir sonraki turda tekrar degerlendirilir.
            }

            _unitOfWork.ClearTracking();
        }

        return null;
    }

    /// <summary>
    /// Profil tercihlerini algoritmanin girdisine cevirir. Antrenman yeri mobil onboarding ile ayni yorumlanir:
    /// spor salonunda katalogdaki tum ekipmanlar, digerlerinde secilen ekipmanlar (Hic ekipmanim yok = bos liste).
    /// </summary>
    private async Task<(WorkoutPlanGenerationInput Input, List<SimulationEquipment> Equipment, string Resolution, List<long> UnknownIds)> BuildInputAsync(
        WorkoutPlanSimulationProfile profile,
        CancellationToken cancellationToken)
    {
        var catalog = await _unitOfWork.Repository<Domain.Entities.Equipment>()
            .GetQueryable()
            .OrderBy(e => e.Name)
            .Select(e => new SimulationEquipment { Id = e.Id, Name = e.Name })
            .ToListAsync(cancellationToken);

        List<SimulationEquipment> equipment;
        string resolution;
        var unknownIds = new List<long>();
        if (profile.TrainingLocation == TrainingLocation.Gym)
        {
            equipment = catalog;
            resolution = "GymAllEquipment";
        }
        else if (profile.NoEquipment || profile.EquipmentIds.Count == 0)
        {
            equipment = new List<SimulationEquipment>();
            resolution = "None";
        }
        else
        {
            var selected = profile.EquipmentIds.Distinct().ToHashSet();
            equipment = catalog.Where(e => selected.Contains(e.Id)).ToList();
            unknownIds = selected.Where(id => catalog.All(e => e.Id != id)).OrderBy(id => id).ToList();
            resolution = "Selected";
        }

        var preferredDays = profile.TrainingDays
            .Select(code => TrainingDayCodes.TryParseCode(code, out var day) ? (DayOfWeek?)day : null)
            .Where(day => day.HasValue)
            .Select(day => day!.Value)
            .ToList();

        var input = new WorkoutPlanGenerationInput
        {
            UserId = 0,
            Goal = profile.Goal,
            ExperienceLevel = profile.ExperienceLevel,
            TrainingDaysPerWeek = profile.TrainingDaysPerWeek,
            PreferredTrainingDays = preferredDays,
            WorkoutDurationMin = profile.WorkoutDurationMin,
            OwnedEquipmentIds = equipment.Select(e => e.Id).ToList(),
            HasActivePlusSubscription = profile.IsPlus,
        };

        return (input, equipment, resolution, unknownIds);
    }

    /// <summary>
    /// Uretilen programi, panelde gosterilecek ad ve kas grubu bilgileriyle zenginlestirir. Tum katalog bilgisi
    /// birkac toplu sorguyla okunur (N+1 yok).
    /// </summary>
    private async Task<WorkoutPlanSimulationResult> BuildResultAsync(
        WorkoutPlanGenerationResult generated,
        WorkoutPlanSimulationProfile profile,
        List<SimulationEquipment> equipment,
        string resolution,
        List<long> unknownIds,
        CancellationToken cancellationToken)
    {
        var diagnostics = generated.Diagnostics
            ?? throw new InvalidOperationException("Generator tani bilgisi donmedi.");
        var insights = diagnostics.Candidates.ToDictionary(c => c.WorkoutId);

        var workoutIds = generated.Days.Select(d => d.Day.WorkoutId)
            .Concat(generated.Days.Where(d => d.Day.CardioWorkoutId.HasValue).Select(d => d.Day.CardioWorkoutId!.Value))
            .Distinct()
            .ToList();
        var mainWorkoutIds = generated.Days.Select(d => d.Day.WorkoutId).Distinct().ToList();
        var cardioWorkoutIds = generated.Days.Where(d => d.Day.CardioWorkoutId.HasValue).Select(d => d.Day.CardioWorkoutId!.Value).Distinct().ToList();

        var workouts = await _unitOfWork.Repository<Workout>().GetQueryable()
            .Where(w => workoutIds.Contains(w.Id))
            .ToDictionaryAsync(w => w.Id, cancellationToken);

        var requiredEquipment = await _unitOfWork.Repository<WorkoutEquipment>().GetQueryable()
            .Where(we => workoutIds.Contains(we.WorkoutId))
            .Select(we => new { we.WorkoutId, we.EquipmentId })
            .ToListAsync(cancellationToken);

        var catalogExerciseRows = await _unitOfWork.Repository<WorkoutExercise>().GetQueryable()
            .Where(we => workoutIds.Contains(we.WorkoutId))
            .OrderBy(we => we.Order)
            .ToListAsync(cancellationToken);

        var exerciseIds = generated.Days.SelectMany(d => d.Exercises.Select(e => e.ExerciseId))
            .Concat(catalogExerciseRows.Where(r => cardioWorkoutIds.Contains(r.WorkoutId)).Select(r => r.ExerciseId))
            .Distinct()
            .ToList();
        var exercises = await _unitOfWork.Repository<Exercise>().GetQueryable()
            .Where(e => exerciseIds.Contains(e.Id))
            .ToDictionaryAsync(e => e.Id, cancellationToken);

        var muscleGroupIds = workouts.Values.Where(w => w.MuscleGroupId.HasValue).Select(w => w.MuscleGroupId!.Value)
            .Concat(exercises.Values.Select(e => e.PrimaryMuscleGroupId))
            .Distinct()
            .ToList();
        var muscleGroups = await _unitOfWork.Repository<MuscleGroup>().GetQueryable()
            .Where(m => muscleGroupIds.Contains(m.Id))
            .ToDictionaryAsync(m => m.Id, m => m.Name, cancellationToken);

        var equipmentIdsNeeded = requiredEquipment.Select(r => r.EquipmentId).Distinct().ToList();
        var equipmentNames = await _unitOfWork.Repository<Domain.Entities.Equipment>().GetQueryable()
            .Where(e => equipmentIdsNeeded.Contains(e.Id))
            .ToDictionaryAsync(e => e.Id, e => e.Name, cancellationToken);

        SimulationWorkout MapWorkout(long id)
        {
            var w = workouts[id];
            insights.TryGetValue(id, out var insight);
            return new SimulationWorkout
            {
                Id = w.Id,
                Title = w.Title,
                Difficulty = w.Difficulty,
                DurationMin = w.DurationMin,
                MuscleGroupId = w.MuscleGroupId,
                MuscleGroupName = w.MuscleGroupId is long mg ? muscleGroups.GetValueOrDefault(mg) : null,
                IsPremium = w.IsPremium,
                IsFeatured = w.IsFeatured,
                RequiredEquipment = requiredEquipment
                    .Where(r => r.WorkoutId == id)
                    .Select(r => new SimulationEquipment { Id = r.EquipmentId, Name = equipmentNames.GetValueOrDefault(r.EquipmentId) ?? $"#{r.EquipmentId}" })
                    .OrderBy(e => e.Name)
                    .ToList(),
                BaseScore = insight?.BaseScore,
                FitScore = insight?.FitScore is decimal fit ? Math.Round(fit, 2) : null,
                Hypertrophy = insight?.Hypertrophy,
                Strength = insight?.Strength,
                CardioScore = insight?.Cardio,
                IsCardioDominant = insight?.IsCardioDominant ?? false,
            };
        }

        SimulationExercise MapExercise(long exerciseId, int order, int sets, int? reps, int? repsMax, int? rest, int? duration, string kind)
        {
            exercises.TryGetValue(exerciseId, out var e);
            return new SimulationExercise
            {
                ExerciseId = exerciseId,
                Name = e?.Name ?? $"#{exerciseId}",
                Order = order,
                MuscleGroupId = e?.PrimaryMuscleGroupId,
                MuscleGroupName = e is null ? null : muscleGroups.GetValueOrDefault(e.PrimaryMuscleGroupId),
                EquipmentType = e?.EquipmentType,
                Sets = sets,
                Reps = reps,
                RepsMax = repsMax,
                RestSeconds = rest,
                DurationSeconds = duration,
                Kind = kind,
            };
        }

        var days = generated.Days
            .Select(d =>
            {
                var main = MapWorkout(d.Day.WorkoutId);
                var cardio = d.Day.CardioWorkoutId is long cardioId ? MapWorkout(cardioId) : null;
                var catalogCount = catalogExerciseRows.Count(r => r.WorkoutId == d.Day.WorkoutId);
                return new SimulationDay
                {
                    DayOfWeek = TrainingDayCodes.ToCode(d.Day.DayOfWeek),
                    DayIndex = TrainingDayCodes.MondayFirstIndex(d.Day.DayOfWeek),
                    Workout = main,
                    Cardio = cardio,
                    IsMixedCardioDay = cardio is null
                        && main.CardioScore is int cs
                        && cs >= _generationOptions.MixedWorkoutCardioScoreThreshold
                        && diagnostics.CardioTarget > 0,
                    EstimatedDurationMin = main.DurationMin + (cardio?.DurationMin ?? 0),
                    TruncatedExerciseCount = Math.Max(0, catalogCount - d.Exercises.Count),
                    Exercises = d.Exercises
                        .Select(e => MapExercise(e.ExerciseId, e.Order, e.Sets, e.Reps, e.RepsMax, e.RestSeconds, e.DurationSeconds, "Main"))
                        .ToList(),
                    CardioExercises = cardio is null
                        ? new List<SimulationExercise>()
                        : catalogExerciseRows
                            .Where(r => r.WorkoutId == cardio.Id)
                            .Select(r => MapExercise(r.ExerciseId, r.Order, r.Sets, r.Reps, null, r.RestSeconds, r.DurationSeconds, "Cardio"))
                            .ToList(),
                };
            })
            .OrderBy(d => d.DayIndex)
            .ToList();

        var goal = diagnostics.Goal;
        var reps = _generationOptions.RepsRangeByGoal.TryGetValue(goal.ToString(), out var range) ? range : _generationOptions.DefaultRepsRange;

        return new WorkoutPlanSimulationResult
        {
            AlgorithmVersion = diagnostics.AlgorithmVersion,
            OptionsFingerprint = ComputeOptionsFingerprint(_generationOptions),
            GeneratedAt = DateTime.UtcNow,
            PlanName = generated.Plan.Name,
            Summary = generated.Summary,
            Goal = goal,
            Experience = diagnostics.Experience,
            TargetDifficulty = diagnostics.TargetDifficulty,
            TrainingDays = diagnostics.TrainingDays,
            UsedPreferredDays = diagnostics.UsedPreferredDays,
            DurationPreferenceMin = diagnostics.DurationPreferenceMin,
            UsedDefaultDuration = profile.WorkoutDurationMin is null,
            DurationToleranceMin = _generationOptions.DurationToleranceMin,
            MaxDurationMin = diagnostics.MaxDurationMin,
            IncludePremium = diagnostics.IncludePremium,
            EquipmentResolution = resolution,
            Equipment = equipment,
            UnknownEquipmentIds = unknownIds,
            PoolStage = diagnostics.PoolStage.ToString(),
            PremiumLastResortUsed = diagnostics.PremiumLastResortUsed,
            EligibleCount = diagnostics.EligibleCount,
            MainPoolCount = diagnostics.MainPoolCount,
            CardioDominantMainDaysFallback = diagnostics.CardioDominantMainDaysFallback,
            CardioTarget = diagnostics.CardioTarget,
            CardioDays = diagnostics.CardioDays,
            SetsRule = _generationOptions.SetsByGoalAndExperience.TryGetValue($"{goal}:{diagnostics.Experience}", out var sets) ? sets : _generationOptions.DefaultSets,
            RepsMinRule = reps.Min,
            RepsMaxRule = reps.Max,
            RestSecondsRule = _generationOptions.RestSecondsByGoal.TryGetValue(goal, out var rest) ? rest : _generationOptions.DefaultRestSeconds,
            MaxExercisesPerDay = _generationOptions.MaxExercisesPerDay,
            Days = days,
            Metrics = BuildMetrics(days),
        };
    }

    public static SimulationMetrics BuildMetrics(List<SimulationDay> days)
    {
        var main = days.SelectMany(d => d.Exercises.Select(e => (Day: d, Exercise: e))).ToList();
        var usedEquipment = days
            .SelectMany(d => d.Workout.RequiredEquipment.Concat(d.Cardio?.RequiredEquipment ?? new List<SimulationEquipment>()))
            .GroupBy(e => e.Id)
            .Select(g => g.First())
            .OrderBy(e => e.Name)
            .ToList();

        return new SimulationMetrics
        {
            DayCount = days.Count,
            TotalExercises = main.Count,
            UniqueExercises = main.Select(x => x.Exercise.ExerciseId).Distinct().Count(),
            TotalSets = main.Sum(x => x.Exercise.Sets),
            CardioExerciseCount = days.Sum(d => d.CardioExercises.Count),
            AverageDurationMin = days.Count == 0 ? 0 : Math.Round((decimal)days.Average(d => d.EstimatedDurationMin), 1),
            AverageExercisesPerDay = days.Count == 0 ? 0 : Math.Round((decimal)main.Count / days.Count, 1),
            UsedEquipmentCount = usedEquipment.Count,
            UsedEquipment = usedEquipment,
            MuscleGroups = main
                .GroupBy(x => x.Exercise.MuscleGroupId)
                .Select(g => new SimulationMuscleGroupShare
                {
                    MuscleGroupId = g.Key,
                    Name = g.First().Exercise.MuscleGroupName ?? "Bilinmiyor",
                    ExerciseCount = g.Count(),
                    Sets = g.Sum(x => x.Exercise.Sets),
                    DayCount = g.Select(x => x.Day.DayOfWeek).Distinct().Count(),
                })
                .OrderByDescending(m => m.Sets)
                .ThenBy(m => m.Name)
                .ToList(),
            RepsRanges = main
                .GroupBy(x => x.Exercise.DurationSeconds is int sec
                    ? $"{sec} sn"
                    : x.Exercise.Reps == x.Exercise.RepsMax ? $"{x.Exercise.Reps}" : $"{x.Exercise.Reps}-{x.Exercise.RepsMax}")
                .Select(g => new SimulationRepsRangeShare { Label = g.Key, ExerciseCount = g.Count() })
                .OrderByDescending(r => r.ExerciseCount)
                .ToList(),
        };
    }

    /// <summary>
    /// Algoritmayi etkileyen tum ayarlarin (agirliklar, cezalar, set/tekrar tablolari) kisa parmak izi. Ayni
    /// AlgorithmVersion icinde appsettings degisikliklerini ayirt etmeye yarar.
    /// </summary>
    public static string ComputeOptionsFingerprint(WorkoutPlanGenerationOptions options)
    {
        var json = JsonSerializer.Serialize(options, new JsonSerializerOptions { IncludeFields = true });
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(json));
        return Convert.ToHexString(hash)[..12].ToLowerInvariant();
    }

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];
}
