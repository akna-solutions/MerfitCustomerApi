using System.Text.Json;
using MerfitCustomerApi.Business.Common;
using MerfitCustomerApi.Business.Dtos.Customer.Training;
using MerfitCustomerApi.Business.Interfaces.Services;
using MerfitCustomerApi.Domain.Entities;
using MerfitCustomerApi.Domain.Entities.Enums;
using MerfitCustomerApi.Domain.Exceptions;
using MerfitCustomerApi.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace MerfitCustomerApi.Business.Services.Training;

/// <summary>
/// Antrenman motorunun veritabani orkestrasyonu. Program versiyonlari mevcut WorkoutPlan / WorkoutPlanDay /
/// WorkoutPlanExercise tablolarinda tutulur. Hicbir versiyon silinmez ve aktif versiyon kullanici onayi olmadan
/// degistirilmez: motorun onerileri "Proposed" durumunda bekler.
/// </summary>
public sealed class TrainingProgramService : ITrainingProgramService
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly IUnitOfWork _unitOfWork;
    private readonly TrainingEngineOptions _options;
    private readonly TimeProvider _time;

    public TrainingProgramService(IUnitOfWork unitOfWork, IOptions<TrainingEngineOptions> options, TimeProvider? time = null)
    {
        _unitOfWork = unitOfWork;
        _options = options.Value;
        _time = time ?? TimeProvider.System;
    }

    private DateTime Now => _time.GetUtcNow().UtcDateTime;

    // ------------------------------------------------------------------ Tercihler

    public async Task<TrainingPreferencesDto> GetPreferencesAsync(long userId, CancellationToken cancellationToken = default)
    {
        var pref = await _unitOfWork.Repository<UserTrainingPreference>().FirstOrDefaultAsync(p => p.UserId == userId, cancellationToken);
        var profile = await _unitOfWork.Repository<UserProfile>().FirstOrDefaultAsync(p => p.UserId == userId, cancellationToken);
        var equipment = await _unitOfWork.Repository<UserEquipment>().GetQueryable()
            .Where(e => e.UserId == userId).Select(e => e.EquipmentId).OrderBy(id => id).ToListAsync(cancellationToken);
        var dto = ToDto(pref, profile);
        dto.EquipmentIds = equipment;
        dto.EquipmentMode = equipment.Count > 0 ? "selected" : HasFullEquipment(profile) ? "full" : "bodyweight";
        return dto;
    }

    /// <summary>Uygulamada ekipman tek tek secilmez; salon veya "ekipmanim var" = katalogdaki tum ekipmanlar.</summary>
    private static bool HasFullEquipment(UserProfile? profile) =>
        profile is not null && (profile.HasEquipment || profile.TrainingLocation == TrainingLocation.Gym);

    public async Task<TrainingPreferencesDto> UpdatePreferencesAsync(long userId, TrainingPreferencesDto request, CancellationToken cancellationToken = default)
    {
        await SavePreferencesAsync(userId, request, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return await GetPreferencesAsync(userId, cancellationToken);
    }

    private async Task SavePreferencesAsync(long userId, TrainingPreferencesDto request, CancellationToken cancellationToken)
    {
        TrainingFocus? focus = null;
        if (!string.IsNullOrWhiteSpace(request.Focus))
        {
            if (!Enum.TryParse<TrainingFocus>(request.Focus, ignoreCase: true, out var parsed) || !Enum.IsDefined(parsed))
            {
                throw new AppValidationException(nameof(request.Focus), "Geçersiz antrenman odağı.");
            }
            focus = parsed;
        }

        var exerciseIds = request.PreferredExerciseIds.Concat(request.AvoidedExerciseIds).Distinct().ToList();
        if (exerciseIds.Count > 0)
        {
            var known = await _unitOfWork.Repository<Exercise>().GetQueryable().Where(e => exerciseIds.Contains(e.Id)).Select(e => e.Id).ToListAsync(cancellationToken);
            if (known.Count != exerciseIds.Count)
            {
                throw new AppValidationException("Seçilen egzersizlerden biri veritabanında yok.");
            }
        }

        if (request.PreferredExerciseIds.Intersect(request.AvoidedExerciseIds).Any())
        {
            throw new AppValidationException("Bir egzersiz hem tercih edilen hem kaçınılan olamaz.");
        }

        var muscleIds = request.PriorityMuscleGroupIds.Concat(request.LimitedMuscleGroupIds).Distinct().ToList();
        if (muscleIds.Count > 0)
        {
            var known = await _unitOfWork.Repository<MuscleGroup>().GetQueryable().Where(m => muscleIds.Contains(m.Id)).Select(m => m.Id).ToListAsync(cancellationToken);
            if (known.Count != muscleIds.Count)
            {
                throw new AppValidationException("Seçilen kas gruplarından biri veritabanında yok.");
            }
        }

        var repo = _unitOfWork.Repository<UserTrainingPreference>();
        var pref = await repo.GetQueryable(asNoTracking: false).FirstOrDefaultAsync(p => p.UserId == userId, cancellationToken);
        var isNew = pref is null;
        pref ??= new UserTrainingPreference { UserId = userId, CreatedAt = Now };
        pref.Focus = focus;
        pref.DaysPerWeek = request.DaysPerWeek;
        pref.PriorityMuscleGroupIds = IdList.Format(request.PriorityMuscleGroupIds);
        pref.PreferredExerciseIds = IdList.Format(request.PreferredExerciseIds);
        pref.AvoidedExerciseIds = IdList.Format(request.AvoidedExerciseIds);
        pref.LimitedMuscleGroupIds = IdList.Format(request.LimitedMuscleGroupIds);
        pref.LimitationsNote = string.IsNullOrWhiteSpace(request.LimitationsNote) ? null : request.LimitationsNote.Trim();
        if (isNew)
        {
            await repo.AddAsync(pref, cancellationToken);
        }
        else
        {
            pref.UpdatedAt = Now;
            repo.Update(pref);
        }

        if (request.EquipmentIds is not null)
        {
            var wanted = request.EquipmentIds.Distinct().ToList();
            if (wanted.Count > 0)
            {
                var knownEquipment = await _unitOfWork.Repository<Domain.Entities.Equipment>().GetQueryable()
                    .Where(e => wanted.Contains(e.Id)).Select(e => e.Id).ToListAsync(cancellationToken);
                if (knownEquipment.Count != wanted.Count)
                {
                    throw new AppValidationException(nameof(request.EquipmentIds), "Seçilen ekipmanlardan biri veritabanında yok.");
                }
            }

            var equipmentRepo = _unitOfWork.Repository<UserEquipment>();
            var existing = await equipmentRepo.GetQueryable(asNoTracking: false).Where(e => e.UserId == userId).ToListAsync(cancellationToken);
            foreach (var row in existing.Where(r => !wanted.Contains(r.EquipmentId)).ToList())
            {
                equipmentRepo.Remove(row);
            }

            var have = existing.Select(r => r.EquipmentId).ToHashSet();
            foreach (var id in wanted.Where(id => !have.Contains(id)))
            {
                await equipmentRepo.AddAsync(new UserEquipment { UserId = userId, EquipmentId = id, CreatedAt = Now }, cancellationToken);
            }
        }

        if (request.SessionMinutes.HasValue)
        {
            var profile = await _unitOfWork.Repository<UserProfile>().GetQueryable(asNoTracking: false)
                .FirstOrDefaultAsync(p => p.UserId == userId, cancellationToken);
            if (profile is not null && profile.WorkoutDurationMin != request.SessionMinutes)
            {
                profile.WorkoutDurationMin = request.SessionMinutes;
                profile.UpdatedAt = Now;
                _unitOfWork.Repository<UserProfile>().Update(profile);
            }
        }
    }

    private static TrainingPreferencesDto ToDto(UserTrainingPreference? pref, UserProfile? profile) => new()
    {
        Focus = pref?.Focus?.ToString(),
        DaysPerWeek = pref?.DaysPerWeek ?? profile?.TrainingDaysPerWeek,
        SessionMinutes = profile?.WorkoutDurationMin,
        PriorityMuscleGroupIds = IdList.Parse(pref?.PriorityMuscleGroupIds).ToList(),
        PreferredExerciseIds = IdList.Parse(pref?.PreferredExerciseIds).ToList(),
        AvoidedExerciseIds = IdList.Parse(pref?.AvoidedExerciseIds).ToList(),
        LimitedMuscleGroupIds = IdList.Parse(pref?.LimitedMuscleGroupIds).ToList(),
        LimitationsNote = pref?.LimitationsNote,
    };

    // ------------------------------------------------------------------ Baglam

    private sealed record EngineContext(
        TrainingProfile Profile,
        IReadOnlyDictionary<long, CatalogExercise> Catalog,
        TrainingHistory History,
        FitnessGoal Goal);

    private async Task<EngineContext> LoadContextAsync(long userId, WorkoutPlan? activePlan, CancellationToken cancellationToken)
    {
        var profile = await _unitOfWork.Repository<UserProfile>().FirstOrDefaultAsync(p => p.UserId == userId, cancellationToken)
            ?? throw new AppValidationException("Program oluşturmak için önce profilini tamamlamalısın.");
        var pref = await _unitOfWork.Repository<UserTrainingPreference>().FirstOrDefaultAsync(p => p.UserId == userId, cancellationToken);
        var activeGoal = await _unitOfWork.Repository<UserGoal>().GetQueryable()
            .Where(g => g.UserId == userId && g.IsActive)
            .OrderByDescending(g => g.CreatedAt)
            .Select(g => (FitnessGoal?)g.GoalType)
            .FirstOrDefaultAsync(cancellationToken);
        var goal = FitnessGoals.Normalize(activeGoal ?? profile.Goal);
        // Ekipman: kullanici tek tek sectiyse (UserEquipment) yalnizca onlar; secmediyse salon / ekipmanli profil
        // katalogdaki tum ekipmanlara sahip sayilir (mevcut plan ureticisiyle ayni kural); aksi halde vucut agirligi.
        var equipment = await _unitOfWork.Repository<UserEquipment>().GetQueryable()
            .Where(e => e.UserId == userId).Select(e => e.EquipmentId).ToListAsync(cancellationToken);
        var explicitEquipment = equipment.Count > 0;
        var fullEquipment = !explicitEquipment && HasFullEquipment(profile);
        if (fullEquipment)
        {
            equipment = await _unitOfWork.Repository<Domain.Entities.Equipment>().GetQueryable().Select(e => e.Id).ToListAsync(cancellationToken);
        }

        var catalog = await TrainingHistoryReader.LoadCatalogAsync(_unitOfWork, _options, null, cancellationToken);
        var muscleGroups = await _unitOfWork.Repository<MuscleGroup>().GetQueryable().Select(m => new { m.Id, m.Name, m.Slug }).ToListAsync(cancellationToken);
        var regionByMuscle = muscleGroups.ToDictionary(m => m.Id, m => MuscleRegionMapper.Map(m.Name, m.Slug, _options.MuscleGroupAliases));
        HashSet<MuscleRegion> Regions(string? ids) => IdList.Parse(ids)
            .Select(id => regionByMuscle.GetValueOrDefault(id, MuscleRegion.Other))
            .Where(r => r != MuscleRegion.Other)
            .ToHashSet();

        var focus = pref?.Focus ?? goal switch
        {
            FitnessGoal.LoseWeight => TrainingFocus.FatLoss,
            FitnessGoal.GetStronger => TrainingFocus.Strength,
            _ => TrainingFocus.Hypertrophy,
        };

        var now = Now;
        var trainingProfile = new TrainingProfile
        {
            UserId = userId,
            Age = profile.DateOfBirth is DateTime dob ? (int)((now - dob).TotalDays / 365.25) : null,
            Gender = profile.Gender,
            HeightCm = profile.HeightCm,
            WeightKg = profile.WeightKg,
            Focus = focus,
            FocusFromProfileGoal = pref?.Focus is null,
            Experience = profile.ExperienceLevel ?? ExperienceLevel.Beginner,
            ExperienceDefaulted = profile.ExperienceLevel is null,
            RequestedDaysPerWeek = pref?.DaysPerWeek ?? profile.TrainingDaysPerWeek ?? 3,
            AvailableDays = TrainingDayCodes.ParseStored(profile.PreferredTrainingDays),
            SessionMinutes = profile.WorkoutDurationMin is > 0 ? profile.WorkoutDurationMin.Value : 45,
            SessionMinutesDefaulted = profile.WorkoutDurationMin is not > 0,
            EquipmentIds = equipment.ToHashSet(),
            HasGymAccess = fullEquipment,
            PriorityRegions = Regions(pref?.PriorityMuscleGroupIds),
            LimitedRegions = Regions(pref?.LimitedMuscleGroupIds),
            PreferredExerciseIds = IdList.Parse(pref?.PreferredExerciseIds).ToHashSet(),
            AvoidedExerciseIds = IdList.Parse(pref?.AvoidedExerciseIds).ToHashSet(),
            LimitationsNote = pref?.LimitationsNote,
        };

        var exposures = await TrainingHistoryReader.LoadExposuresAsync(
            _unitOfWork, userId, null, before: null, excludeSessionId: null, maxPerExercise: 40, cancellationToken, since: now.AddDays(-120));
        var feedback = await TrainingHistoryReader.LoadFeedbackAsync(_unitOfWork, userId, now.AddDays(-Math.Max(28, _options.DiscomfortLookbackDays)), cancellationToken);
        var (scheduled, completed) = await TrainingHistoryReader.LoadAdherenceAsync(_unitOfWork, userId, now, cancellationToken);

        var currentExerciseIds = new HashSet<long>();
        if (activePlan is not null)
        {
            var dayIds = await _unitOfWork.Repository<WorkoutPlanDay>().GetQueryable().Where(d => d.WorkoutPlanId == activePlan.Id).Select(d => d.Id).ToListAsync(cancellationToken);
            currentExerciseIds = (await _unitOfWork.Repository<WorkoutPlanExercise>().GetQueryable()
                .Where(e => dayIds.Contains(e.WorkoutPlanDayId)).Select(e => e.ExerciseId).ToListAsync(cancellationToken)).ToHashSet();
        }

        var history = new TrainingHistory
        {
            ExposuresByExercise = exposures,
            RecentFeedback = feedback,
            ScheduledSessionsLast4Weeks = scheduled,
            CompletedPlanSessionsLast4Weeks = completed,
            CurrentProgramExerciseIds = currentExerciseIds,
        };

        return new EngineContext(trainingProfile, catalog, history, goal);
    }

    private Task<WorkoutPlan?> GetActivePlanEntityAsync(long userId, CancellationToken cancellationToken) =>
        _unitOfWork.Repository<WorkoutPlan>().GetQueryable(asNoTracking: false)
            .Where(p => p.UserId == userId && p.IsActive)
            .OrderByDescending(p => p.Id)
            .FirstOrDefaultAsync(cancellationToken);

    private static bool IsEnginePlan(WorkoutPlan plan) => plan.Source is TrainingProgramSource.Engine or TrainingProgramSource.Manual;

    // ------------------------------------------------------------------ Olusturma / versiyonlama

    public async Task<TrainingProgramDto> GenerateAsync(long userId, GenerateTrainingProgramRequest request, CancellationToken cancellationToken = default)
    {
        await SavePreferencesAsync(userId, request, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        var active = await GetActivePlanEntityAsync(userId, cancellationToken);
        var context = await LoadContextAsync(userId, active is not null && IsEnginePlan(active) ? active : null, cancellationToken);
        var builder = new ProgramBuilder(_options);
        var program = builder.Build(context.Profile, context.Catalog.Values.ToList(), context.History, ProgramAdjustments.None, Now);
        EnsureValid(program, context);

        if (active is not null && IsEnginePlan(active))
        {
            // Aktif programin ortasinda kullanici onayi olmadan degisiklik yapilmaz: yeni versiyon oneri olarak bekler.
            var changes = await DiffAsync(active, program, ProgramAdjustments.None, context, "Tercihlerin güncellendiği için yeni versiyon hazırlandı.", cancellationToken);
            var proposal = await PersistAsync(userId, context, program, TrainingProgramStatus.Proposed, TrainingProgramSource.Engine, active, changes, cancellationToken);
            return await GetProgramAsync(userId, proposal.Id, cancellationToken);
        }

        var created = new List<TrainingChangeDto> { new() { Type = "created", Message = "İlk kişisel programın oluşturuldu." } };
        if (active is not null)
        {
            created.Add(new() { Type = "note", Message = "Önceki hazır antrenman planın arşivlendi; tamamladığın antrenmanlar ve geçmişin korunuyor." });
        }

        var plan = await PersistAsync(userId, context, program, TrainingProgramStatus.Active, TrainingProgramSource.Engine, active, created, cancellationToken);
        return await GetProgramAsync(userId, plan.Id, cancellationToken);
    }

    public async Task<TrainingUpdateRecommendationDto> GetUpdateRecommendationAsync(long userId, CancellationToken cancellationToken = default)
    {
        var active = await GetActivePlanEntityAsync(userId, cancellationToken);
        if (active is null || !IsEnginePlan(active))
        {
            return new TrainingUpdateRecommendationDto { HasActiveProgram = false };
        }

        var context = await LoadContextAsync(userId, active, cancellationToken);
        var recovery = RecoveryAssessor.Assess(context.History, context.Catalog, _options, Now);
        var week = CurrentWeek(active);
        var reasons = new List<string>();
        var blockWeeks = active.BlockWeeks ?? _options.BlockWeeks;
        if (week > blockWeeks)
        {
            reasons.Add($"{blockWeeks} haftalık program bloğu tamamlandı.");
        }
        if (recovery.Status == RecoveryStatus.DeloadRecommended)
        {
            reasons.Add("Performans düşüşü ve yorgunluk birlikte görülüyor; deload önerilir.");
        }
        if (recovery.DiscomfortExerciseIds.Count > 0)
        {
            reasons.Add("Rahatsızlık bildirdiğin hareketler var; alternatifleriyle değiştirilmesi önerilir.");
        }

        var pending = await _unitOfWork.Repository<WorkoutPlan>().GetQueryable()
            .Where(p => p.UserId == userId && p.Status == TrainingProgramStatus.Proposed)
            .OrderByDescending(p => p.Id).Select(p => (long?)p.Id).FirstOrDefaultAsync(cancellationToken);

        return new TrainingUpdateRecommendationDto
        {
            HasActiveProgram = true,
            IsUpdateRecommended = reasons.Count > 0,
            Reasons = reasons,
            PendingProposalId = pending,
            CurrentWeek = week,
            BlockWeeks = blockWeeks,
            Recovery = ToDto(recovery),
        };
    }

    public async Task<TrainingProgramDto> ProposeUpdateAsync(long userId, CancellationToken cancellationToken = default)
    {
        var active = await GetActivePlanEntityAsync(userId, cancellationToken);
        if (active is null || !IsEnginePlan(active))
        {
            throw new AppValidationException("Güncelleme önerisi için önce kişisel program oluşturmalısın.");
        }

        var context = await LoadContextAsync(userId, active, cancellationToken);
        var recovery = RecoveryAssessor.Assess(context.History, context.Catalog, _options, Now);
        var adjustments = recovery.SuggestedAdjustments;

        // Duraganlik rotasyonu: aktif programdaki bir harekette son N seansta tahmini 1RM ilerlemediyse ve ayni
        // bolge icin uygun baska hareket varsa, yeni versiyonda alternatif secilir.
        var excluded = adjustments.ExcludedExerciseIds.ToHashSet();
        var rotationReasons = new List<string>();
        foreach (var exerciseId in context.History.CurrentProgramExerciseIds.OrderBy(id => id))
        {
            if (!context.History.ExposuresByExercise.TryGetValue(exerciseId, out var exposures) || exposures.Count < _options.StallExposuresForRotation
                || !context.Catalog.TryGetValue(exerciseId, out var meta))
            {
                continue;
            }

            var window = exposures.TakeLast(_options.StallExposuresForRotation).Select(LoadMath.BestEstimate).ToList();
            if (window.Any(v => v is null)) continue;
            var stalled = window[^1] <= window[0];
            var hasAlternative = context.Catalog.Values.Any(c => c.Id != exerciseId && c.PrimaryRegion == meta.PrimaryRegion
                && !excluded.Contains(c.Id) && !context.Profile.AvoidedExerciseIds.Contains(c.Id)
                && ExerciseMetadataResolver.IsAvailable(c, context.Profile.EquipmentIds, context.Profile.HasGymAccess));
            if (stalled && hasAlternative)
            {
                excluded.Add(exerciseId);
                rotationReasons.Add($"{meta.Name} hareketinde son {_options.StallExposuresForRotation} seansta ilerleme olmadığı için alternatif bir hareket seçildi.");
            }
        }

        adjustments = adjustments with { ExcludedExerciseIds = excluded, Reasons = adjustments.Reasons.Concat(rotationReasons).ToList() };
        var program = new ProgramBuilder(_options).Build(context.Profile, context.Catalog.Values.ToList(), context.History, adjustments, Now);
        EnsureValid(program, context);

        var intro = adjustments.IsDeload
            ? "Toparlanma sinyallerine göre bir haftalık deload versiyonu önerildi."
            : "Geçmiş performansına ve toparlanmana göre yeni versiyon önerildi.";
        var changes = await DiffAsync(active, program, adjustments, context, intro, cancellationToken);
        var proposal = await PersistAsync(userId, context, program, TrainingProgramStatus.Proposed, TrainingProgramSource.Engine, active, changes, cancellationToken);
        return await GetProgramAsync(userId, proposal.Id, cancellationToken);
    }

    public async Task<TrainingProgramDto> AcceptProposalAsync(long userId, long programId, CancellationToken cancellationToken = default)
    {
        var proposal = await _unitOfWork.Repository<WorkoutPlan>().GetQueryable(asNoTracking: false)
            .FirstOrDefaultAsync(p => p.Id == programId && p.UserId == userId, cancellationToken)
            ?? throw new NotFoundException(nameof(WorkoutPlan), programId);
        if (proposal.Status != TrainingProgramStatus.Proposed)
        {
            throw new AppValidationException("Yalnızca bekleyen bir öneri onaylanabilir.");
        }

        var active = await GetActivePlanEntityAsync(userId, cancellationToken);
        if (active is not null && proposal.PreviousPlanId.HasValue && active.Id != proposal.PreviousPlanId)
        {
            throw new ConflictException("Bu öneri artık güncel değil; aktif programın değişti. Yeni bir öneri oluştur.");
        }

        await _unitOfWork.BeginTransactionAsync(cancellationToken);
        if (active is not null)
        {
            Archive(active);
        }

        proposal.Status = TrainingProgramStatus.Active;
        proposal.IsActive = true;
        proposal.StartDate = Now;
        proposal.UpdatedAt = Now;
        _unitOfWork.Repository<WorkoutPlan>().Update(proposal);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        await _unitOfWork.CommitTransactionAsync(cancellationToken);
        return await GetProgramAsync(userId, proposal.Id, cancellationToken);
    }

    public async Task RejectProposalAsync(long userId, long programId, CancellationToken cancellationToken = default)
    {
        var proposal = await _unitOfWork.Repository<WorkoutPlan>().GetQueryable(asNoTracking: false)
            .FirstOrDefaultAsync(p => p.Id == programId && p.UserId == userId, cancellationToken)
            ?? throw new NotFoundException(nameof(WorkoutPlan), programId);
        if (proposal.Status != TrainingProgramStatus.Proposed)
        {
            throw new AppValidationException("Yalnızca bekleyen bir öneri reddedilebilir.");
        }

        proposal.Status = TrainingProgramStatus.Rejected;
        proposal.UpdatedAt = Now;
        _unitOfWork.Repository<WorkoutPlan>().Update(proposal);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    private void Archive(WorkoutPlan plan)
    {
        plan.IsActive = false;
        plan.Status = TrainingProgramStatus.Archived;
        plan.ArchivedAt = Now;
        plan.EndDate ??= Now;
        plan.UpdatedAt = Now;
        _unitOfWork.Repository<WorkoutPlan>().Update(plan);
    }

    private void EnsureValid(GeneratedProgram program, EngineContext context)
    {
        var errors = ProgramValidator.Validate(program, context.Catalog, context.Profile, _options);
        if (errors.Count > 0)
        {
            throw new AppValidationException("Program doğrulanamadı: " + string.Join(" ", errors));
        }
    }

    private int? CurrentWeek(WorkoutPlan plan) =>
        plan.Status == TrainingProgramStatus.Active ? (int)Math.Floor((Now - plan.StartDate).TotalDays / 7) + 1 : null;

    private async Task<WorkoutPlan> PersistAsync(
        long userId,
        EngineContext context,
        GeneratedProgram program,
        TrainingProgramStatus status,
        TrainingProgramSource source,
        WorkoutPlan? previous,
        IReadOnlyList<TrainingChangeDto> changes,
        CancellationToken cancellationToken)
    {
        var lastVersion = await _unitOfWork.Repository<WorkoutPlan>().GetQueryable()
            .Where(p => p.UserId == userId && p.ProgramVersion != null)
            .MaxAsync(p => (int?)p.ProgramVersion, cancellationToken) ?? 0;

        await _unitOfWork.BeginTransactionAsync(cancellationToken);

        // Ayni anda tek acik oneri: yeni oneri, bekleyen eski oneriyi gecersiz kilar (reddedildi olarak isaretlenir).
        var openProposals = await _unitOfWork.Repository<WorkoutPlan>().GetQueryable(asNoTracking: false)
            .Where(p => p.UserId == userId && p.Status == TrainingProgramStatus.Proposed)
            .ToListAsync(cancellationToken);
        foreach (var open in openProposals)
        {
            open.Status = TrainingProgramStatus.Rejected;
            open.UpdatedAt = Now;
            _unitOfWork.Repository<WorkoutPlan>().Update(open);
        }

        if (status == TrainingProgramStatus.Active && previous is not null)
        {
            Archive(previous);
        }

        var version = lastVersion + 1;
        var plan = new WorkoutPlan
        {
            UserId = userId,
            Name = $"Program V{version}{(program.IsDeload ? " (Deload)" : string.Empty)} · {program.Split.Label}",
            Description = string.Join(" ", program.Split.Reasons),
            Goal = context.Goal,
            StartDate = Now,
            IsActive = status == TrainingProgramStatus.Active,
            IsAiGenerated = false,
            Status = status,
            Source = source,
            ProgramVersion = version,
            PreviousPlanId = previous?.Id,
            SplitType = program.Split.SplitType,
            EngineVersion = program.EngineVersion,
            BlockWeeks = program.BlockWeeks,
            IsDeload = program.IsDeload,
            ChangeSummary = JsonSerializer.Serialize(new ProgramMeta
            {
                Focus = program.Focus.ToString(),
                SplitLabel = program.Split.Label,
                SplitReasons = program.Split.Reasons.ToList(),
                Notes = program.Notes.ToList(),
                Warnings = program.MetadataWarnings.ToList(),
                Changes = changes.ToList(),
                Volume = program.WeeklyVolume.Select(v => new TrainingRegionVolumeDto
                {
                    Region = v.Region.ToString(),
                    Label = MuscleRegionMapper.Label(v.Region),
                    TargetSets = v.TargetSets,
                    DirectSets = v.PlannedDirectSets,
                    IndirectSets = v.PlannedIndirectSets,
                    TotalSets = v.PlannedTotalSets,
                }).ToList(),
            }, Json),
            CreatedAt = Now,
        };
        await _unitOfWork.Repository<WorkoutPlan>().AddAsync(plan, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        foreach (var day in program.Days)
        {
            var dayEntity = new WorkoutPlanDay
            {
                WorkoutPlanId = plan.Id,
                DayOfWeek = day.DayOfWeek,
                Order = day.Order,
                WorkoutId = null,
                Title = day.Template.Title,
                Focus = day.Template.Focus,
                CreatedAt = Now,
            };
            await _unitOfWork.Repository<WorkoutPlanDay>().AddAsync(dayEntity, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            await _unitOfWork.Repository<WorkoutPlanExercise>().AddRangeAsync(day.Exercises.Select(e => new WorkoutPlanExercise
            {
                WorkoutPlanDayId = dayEntity.Id,
                ExerciseId = e.Exercise.Id,
                Order = e.Order,
                Sets = e.Prescription.Sets,
                Reps = e.Prescription.DurationSeconds.HasValue ? null : e.Prescription.RepsMin,
                RepsMax = e.Prescription.DurationSeconds.HasValue ? null : e.Prescription.RepsMax,
                RestSeconds = e.Prescription.RestSeconds,
                DurationSeconds = e.Prescription.DurationSeconds,
                TargetRir = e.Prescription.TargetRir,
                TargetMuscleGroupId = e.Exercise.PrimaryMuscleGroupId,
                WeightGuidance = e.WeightGuidance,
                SuggestedWeightKg = e.SuggestedWeightKg,
                AlternativeExerciseIds = IdList.Format(e.AlternativeExerciseIds),
                SelectionReason = Truncate(e.SelectionReason, 500),
                CreatedAt = Now,
            }), cancellationToken);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        await _unitOfWork.CommitTransactionAsync(cancellationToken);
        return plan;
    }

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];

    /// <summary>Onceki versiyona gore degisiklikler ve nedenleri (kullaniciya gosterilir).</summary>
    private async Task<List<TrainingChangeDto>> DiffAsync(
        WorkoutPlan previous, GeneratedProgram next, ProgramAdjustments adjustments, EngineContext context, string intro, CancellationToken cancellationToken)
    {
        var changes = new List<TrainingChangeDto> { new() { Type = "note", Message = intro } };
        if (previous.SplitType != next.Split.SplitType)
        {
            changes.Add(new() { Type = "split", Message = $"Split değişti: {next.Split.Label}. {string.Join(" ", next.Split.Reasons)}" });
        }

        if (next.IsDeload)
        {
            changes.Add(new() { Type = "deload", Message = "Bu versiyon bir deload haftası: set sayısı ve ağırlıklar azaltıldı, setlerde daha fazla tekrar payı bırakılıyor." });
        }

        var dayIds = await _unitOfWork.Repository<WorkoutPlanDay>().GetQueryable().Where(d => d.WorkoutPlanId == previous.Id).Select(d => d.Id).ToListAsync(cancellationToken);
        var oldExercises = await _unitOfWork.Repository<WorkoutPlanExercise>().GetQueryable().Where(e => dayIds.Contains(e.WorkoutPlanDayId)).ToListAsync(cancellationToken);
        var oldIds = oldExercises.Select(e => e.ExerciseId).ToHashSet();
        var newIds = next.Days.SelectMany(d => d.Exercises).Select(e => e.Exercise.Id).ToHashSet();
        string Name(long id) => context.Catalog.TryGetValue(id, out var c) ? c.Name : $"#{id}";

        foreach (var removed in oldIds.Except(newIds).OrderBy(id => id))
        {
            var why = context.History.RecentFeedback.Any(f => f.DiscomfortExerciseIds.Contains(removed))
                ? "rahatsızlık bildirdiğin için"
                : adjustments.ExcludedExerciseIds.Contains(removed) ? "ilerleme durduğu için" : "yeni egzersiz seçimiyle";
            changes.Add(new() { Type = "exercise_removed", Message = $"{Name(removed)} çıkarıldı ({why})." });
        }

        foreach (var added in newIds.Except(oldIds).OrderBy(id => id))
        {
            changes.Add(new() { Type = "exercise_added", Message = $"{Name(added)} eklendi." });
        }

        // Bolge bazinda dogrudan set degisimi.
        var oldByRegion = oldExercises
            .GroupBy(e => context.Catalog.TryGetValue(e.ExerciseId, out var c) ? c.PrimaryRegion : MuscleRegion.Other)
            .ToDictionary(g => g.Key, g => g.Sum(e => e.Sets));
        foreach (var v in next.WeeklyVolume)
        {
            var before = oldByRegion.GetValueOrDefault(v.Region);
            var after = (int)v.PlannedDirectSets;
            if (Math.Abs(after - before) >= 2)
            {
                changes.Add(new() { Type = "volume", Message = $"{MuscleRegionMapper.Label(v.Region)} haftalık doğrudan seti {before} → {after}." });
            }
        }

        changes.AddRange(adjustments.Reasons.Select(r => new TrainingChangeDto { Type = "note", Message = r }));
        return changes;
    }

    // ------------------------------------------------------------------ Elle degisiklik

    public async Task<IReadOnlyList<TrainingAlternativeDto>> GetAlternativesAsync(long userId, long planExerciseId, CancellationToken cancellationToken = default)
    {
        var (plan, planExercise) = await GetActivePlanExerciseAsync(userId, planExerciseId, cancellationToken);
        var context = await LoadContextAsync(userId, plan, cancellationToken);
        return Alternatives(planExercise, context, await DayExerciseIdsAsync(planExercise.WorkoutPlanDayId, cancellationToken));
    }

    private static List<TrainingAlternativeDto> Alternatives(WorkoutPlanExercise planExercise, EngineContext context, IReadOnlySet<long> dayExerciseIds)
    {
        if (!context.Catalog.TryGetValue(planExercise.ExerciseId, out var current))
        {
            return new List<TrainingAlternativeDto>();
        }

        var stored = IdList.Parse(planExercise.AlternativeExerciseIds).ToList();
        return context.Catalog.Values
            .Where(c => c.Id != current.Id && c.PrimaryRegion == current.PrimaryRegion && !dayExerciseIds.Contains(c.Id))
            .Where(c => !context.Profile.AvoidedExerciseIds.Contains(c.Id))
            .Where(c => ExerciseMetadataResolver.IsAvailable(c, context.Profile.EquipmentIds, context.Profile.HasGymAccess))
            .OrderByDescending(c => stored.Contains(c.Id))
            .ThenByDescending(c => c.Mechanic == current.Mechanic)
            .ThenByDescending(c => c.Pattern == current.Pattern)
            .ThenBy(c => Math.Abs((int)c.Difficulty - (int)context.Profile.Experience))
            .ThenBy(c => c.Id)
            .Take(6)
            .Select(c => new TrainingAlternativeDto
            {
                ExerciseId = c.Id,
                Name = c.Name,
                Mechanic = c.Mechanic.ToString(),
                Reason = $"Aynı ana kas ({MuscleRegionMapper.Label(c.PrimaryRegion)}), {(c.Mechanic == ExerciseMechanicType.Compound ? "bileşik" : "izolasyon")}"
                    + (c.Pattern == current.Pattern && c.Pattern.HasValue ? ", aynı hareket paterni" : string.Empty)
                    + (c.RequiredEquipmentIds.Count == 0 ? ", ekipmansız" : string.Empty) + ".",
            })
            .ToList();
    }

    public async Task<TrainingProgramDto> SwapExerciseAsync(long userId, long planExerciseId, SwapExerciseRequest request, CancellationToken cancellationToken = default)
    {
        var (plan, planExercise) = await GetActivePlanExerciseAsync(userId, planExerciseId, cancellationToken);
        var context = await LoadContextAsync(userId, plan, cancellationToken);
        var dayExerciseIds = await DayExerciseIdsAsync(planExercise.WorkoutPlanDayId, cancellationToken);
        var allowed = Alternatives(planExercise, context, dayExerciseIds);
        if (!context.Catalog.TryGetValue(request.NewExerciseId, out var replacement))
        {
            throw new AppValidationException(nameof(request.NewExerciseId), "Seçilen egzersiz veritabanındaki aktif egzersizlerden biri değil.");
        }
        if (allowed.All(a => a.ExerciseId != replacement.Id))
        {
            throw new AppValidationException(nameof(request.NewExerciseId),
                "Bu egzersiz bu hareketin yerine geçemez (farklı kas grubu, eksik ekipman, kaçınılan hareket veya aynı günde zaten var).");
        }

        var oldName = context.Catalog.TryGetValue(planExercise.ExerciseId, out var old) ? old.Name : $"#{planExercise.ExerciseId}";
        var days = await _unitOfWork.Repository<WorkoutPlanDay>().GetQueryable().Where(d => d.WorkoutPlanId == plan.Id).OrderBy(d => d.Order).ToListAsync(cancellationToken);
        var dayIds = days.Select(d => d.Id).ToList();
        var exercises = await _unitOfWork.Repository<WorkoutPlanExercise>().GetQueryable().Where(e => dayIds.Contains(e.WorkoutPlanDayId)).OrderBy(e => e.Order).ToListAsync(cancellationToken);

        var meta = ReadMeta(plan);
        var changes = new List<TrainingChangeDto>
        {
            new() { Type = "exercise_swapped", Message = $"{oldName} yerine {replacement.Name} seçtin. Diğer günler ve ayarlar aynı kaldı." },
        };

        var lastVersion = await _unitOfWork.Repository<WorkoutPlan>().GetQueryable()
            .Where(p => p.UserId == userId && p.ProgramVersion != null).MaxAsync(p => (int?)p.ProgramVersion, cancellationToken) ?? 0;

        await _unitOfWork.BeginTransactionAsync(cancellationToken);
        Archive(plan);
        var version = lastVersion + 1;
        var copy = new WorkoutPlan
        {
            UserId = userId,
            Name = $"Program V{version} · {meta.SplitLabel ?? plan.SplitType}",
            Description = plan.Description,
            Goal = plan.Goal,
            StartDate = Now,
            IsActive = true,
            Status = TrainingProgramStatus.Active,
            Source = TrainingProgramSource.Manual,
            ProgramVersion = version,
            PreviousPlanId = plan.Id,
            SplitType = plan.SplitType,
            EngineVersion = plan.EngineVersion,
            BlockWeeks = plan.BlockWeeks,
            IsDeload = plan.IsDeload,
            ChangeSummary = JsonSerializer.Serialize(meta with { Changes = changes }, Json),
            CreatedAt = Now,
        };
        await _unitOfWork.Repository<WorkoutPlan>().AddAsync(copy, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        var focus = context.Profile.Focus;
        foreach (var day in days)
        {
            var newDay = new WorkoutPlanDay
            {
                WorkoutPlanId = copy.Id, DayOfWeek = day.DayOfWeek, Order = day.Order, WorkoutId = day.WorkoutId,
                CardioWorkoutId = day.CardioWorkoutId, Title = day.Title, Focus = day.Focus, CreatedAt = Now,
            };
            await _unitOfWork.Repository<WorkoutPlanDay>().AddAsync(newDay, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            var copies = exercises.Where(e => e.WorkoutPlanDayId == day.Id).Select(e =>
            {
                var clone = new WorkoutPlanExercise
                {
                    WorkoutPlanDayId = newDay.Id, ExerciseId = e.ExerciseId, Order = e.Order, Sets = e.Sets, Reps = e.Reps,
                    RepsMax = e.RepsMax, RestSeconds = e.RestSeconds, DurationSeconds = e.DurationSeconds, TargetRir = e.TargetRir,
                    TargetMuscleGroupId = e.TargetMuscleGroupId, WeightGuidance = e.WeightGuidance, SuggestedWeightKg = e.SuggestedWeightKg,
                    AlternativeExerciseIds = e.AlternativeExerciseIds, SelectionReason = e.SelectionReason, CreatedAt = Now,
                };
                if (e.Id == planExercise.Id)
                {
                    var isFirstCompound = replacement.Mechanic == ExerciseMechanicType.Compound
                        && !exercises.Any(x => x.WorkoutPlanDayId == day.Id && x.Order < e.Order
                            && context.Catalog.TryGetValue(x.ExerciseId, out var c) && c.Mechanic == ExerciseMechanicType.Compound);
                    var scheme = PrescriptionRules.For(focus, replacement.Mechanic, context.Profile.Experience, isFirstCompound);
                    clone.ExerciseId = replacement.Id;
                    clone.TargetMuscleGroupId = replacement.PrimaryMuscleGroupId;
                    if (replacement.IsTimeBased)
                    {
                        clone.Reps = null; clone.RepsMax = null;
                        clone.DurationSeconds = PrescriptionRules.TimeBasedSeconds(focus, context.Profile.Experience);
                    }
                    else
                    {
                        clone.Reps = scheme.RepsMin; clone.RepsMax = scheme.RepsMax; clone.DurationSeconds = null;
                    }
                    clone.TargetRir = e.TargetRir ?? scheme.TargetRir;
                    clone.RestSeconds = scheme.RestSeconds;
                    var exposures = context.History.ExposuresByExercise.GetValueOrDefault(replacement.Id) ?? Array.Empty<ExerciseExposure>();
                    var suggested = replacement.IsBodyweight || replacement.IsTimeBased
                        ? null
                        : LoadMath.SuggestFromHistory(exposures, scheme.RepsMin, scheme.RepsMax, scheme.TargetRir, replacement.LoadIncrementKg, Now);
                    clone.SuggestedWeightKg = suggested;
                    clone.WeightGuidance = replacement.IsBodyweight || replacement.IsTimeBased
                        ? WeightGuidanceType.Bodyweight
                        : suggested.HasValue ? WeightGuidanceType.FromHistory : WeightGuidanceType.RampUp;
                    clone.AlternativeExerciseIds = IdList.Format(IdList.Parse(e.AlternativeExerciseIds).Where(id => id != replacement.Id).Append(e.ExerciseId));
                    clone.SelectionReason = "Kullanıcı tarafından seçildi.";
                }
                return clone;
            }).ToList();
            await _unitOfWork.Repository<WorkoutPlanExercise>().AddRangeAsync(copies, cancellationToken);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        await _unitOfWork.CommitTransactionAsync(cancellationToken);
        return await GetProgramAsync(userId, copy.Id, cancellationToken);
    }

    private async Task<(WorkoutPlan Plan, WorkoutPlanExercise Exercise)> GetActivePlanExerciseAsync(long userId, long planExerciseId, CancellationToken cancellationToken)
    {
        var planExercise = await _unitOfWork.Repository<WorkoutPlanExercise>().GetByIdAsync(planExerciseId, cancellationToken)
            ?? throw new NotFoundException(nameof(WorkoutPlanExercise), planExerciseId);
        var day = await _unitOfWork.Repository<WorkoutPlanDay>().GetByIdAsync(planExercise.WorkoutPlanDayId, cancellationToken)
            ?? throw new NotFoundException(nameof(WorkoutPlanExercise), planExerciseId);
        var plan = await _unitOfWork.Repository<WorkoutPlan>().GetQueryable(asNoTracking: false)
            .FirstOrDefaultAsync(p => p.Id == day.WorkoutPlanId && p.UserId == userId, cancellationToken)
            ?? throw new NotFoundException(nameof(WorkoutPlanExercise), planExerciseId);
        if (!plan.IsActive || !IsEnginePlan(plan))
        {
            throw new AppValidationException("Yalnızca aktif kişisel programdaki egzersizler değiştirilebilir.");
        }
        return (plan, planExercise);
    }

    private async Task<IReadOnlySet<long>> DayExerciseIdsAsync(long dayId, CancellationToken cancellationToken) =>
        (await _unitOfWork.Repository<WorkoutPlanExercise>().GetQueryable().Where(e => e.WorkoutPlanDayId == dayId).Select(e => e.ExerciseId).ToListAsync(cancellationToken)).ToHashSet();

    // ------------------------------------------------------------------ Okuma

    public async Task<TrainingProgramDto?> GetActiveAsync(long userId, CancellationToken cancellationToken = default)
    {
        var plan = await _unitOfWork.Repository<WorkoutPlan>().GetQueryable()
            .Where(p => p.UserId == userId && p.IsActive && (p.Source == TrainingProgramSource.Engine || p.Source == TrainingProgramSource.Manual))
            .OrderByDescending(p => p.Id)
            .FirstOrDefaultAsync(cancellationToken);
        return plan is null ? null : await MapAsync(plan, cancellationToken);
    }

    public Task<IReadOnlyList<TrainingProgramSummaryDto>> GetVersionsAsync(long userId, CancellationToken cancellationToken = default)
        => GetVersionsAsync(userId, null, null, cancellationToken);

    /// <summary>Versiyon listesinde tek istekte donebilecek en fazla kayit.</summary>
    public const int MaxVersionPageSize = 100;

    public async Task<IReadOnlyList<TrainingProgramSummaryDto>> GetVersionsAsync(long userId, int? offset, int? limit, CancellationToken cancellationToken = default)
    {
        IQueryable<WorkoutPlan> query = _unitOfWork.Repository<WorkoutPlan>().GetQueryable()
            .Where(p => p.UserId == userId && p.ProgramVersion != null)
            .OrderByDescending(p => p.ProgramVersion);
        if (offset is > 0) query = query.Skip(offset.Value);
        if (limit.HasValue) query = query.Take(Math.Clamp(limit.Value, 1, MaxVersionPageSize));

        // Performans: ozet kartinda kullanilmayan alanlar (Description vb.) okunmaz.
        var plans = await query
            .Select(p => new WorkoutPlan
            {
                Id = p.Id,
                ProgramVersion = p.ProgramVersion,
                Status = p.Status,
                Source = p.Source,
                ChangeSummary = p.ChangeSummary,
                IsDeload = p.IsDeload,
                StartDate = p.StartDate,
                EndDate = p.EndDate,
                CreatedAt = p.CreatedAt,
            })
            .ToListAsync(cancellationToken);
        var planIds = plans.Select(p => p.Id).ToList();
        var completed = await (
            from s in _unitOfWork.Repository<WorkoutSession>().GetQueryable()
            join d in _unitOfWork.Repository<WorkoutPlanDay>().GetQueryable() on s.WorkoutPlanDayId equals d.Id
            where s.UserId == userId && s.Status == WorkoutSessionStatus.Completed && planIds.Contains(d.WorkoutPlanId)
            group s by d.WorkoutPlanId into g
            select new { PlanId = g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.PlanId, x => x.Count, cancellationToken);

        return plans.Select(p => new TrainingProgramSummaryDto
        {
            Id = p.Id,
            Version = p.ProgramVersion ?? 0,
            Status = p.Status.ToString(),
            Source = p.Source.ToString(),
            SplitLabel = ReadMeta(p).SplitLabel,
            IsDeload = p.IsDeload,
            StartDate = p.StartDate,
            EndDate = p.EndDate,
            CreatedAt = p.CreatedAt,
            CompletedSessions = completed.GetValueOrDefault(p.Id),
        }).ToList();
    }

    public async Task<TrainingProgramDto> GetProgramAsync(long userId, long programId, CancellationToken cancellationToken = default)
    {
        var plan = await _unitOfWork.Repository<WorkoutPlan>().FirstOrDefaultAsync(p => p.Id == programId && p.UserId == userId, cancellationToken);
        if (plan is null || plan.ProgramVersion is null)
        {
            throw new NotFoundException(nameof(WorkoutPlan), programId);
        }
        return await MapAsync(plan, cancellationToken);
    }

    private static ProgramMeta ReadMeta(WorkoutPlan plan)
    {
        if (string.IsNullOrWhiteSpace(plan.ChangeSummary)) return new ProgramMeta();
        try
        {
            return JsonSerializer.Deserialize<ProgramMeta>(plan.ChangeSummary, Json) ?? new ProgramMeta();
        }
        catch (JsonException)
        {
            return new ProgramMeta();
        }
    }

    private async Task<TrainingProgramDto> MapAsync(WorkoutPlan plan, CancellationToken cancellationToken)
    {
        var days = await _unitOfWork.Repository<WorkoutPlanDay>().GetQueryable().Where(d => d.WorkoutPlanId == plan.Id).OrderBy(d => d.Order).ToListAsync(cancellationToken);
        var dayIds = days.Select(d => d.Id).ToList();
        var planExercises = await _unitOfWork.Repository<WorkoutPlanExercise>().GetQueryable().Where(e => dayIds.Contains(e.WorkoutPlanDayId)).OrderBy(e => e.Order).ToListAsync(cancellationToken);
        var exerciseIds = planExercises.Select(e => e.ExerciseId)
            .Concat(planExercises.SelectMany(e => IdList.Parse(e.AlternativeExerciseIds)))
            .Distinct().ToList();
        var catalog = await TrainingHistoryReader.LoadCatalogAsync(_unitOfWork, _options, exerciseIds, cancellationToken);
        var exercises = await _unitOfWork.Repository<Exercise>().GetQueryable().Where(e => exerciseIds.Contains(e.Id)).ToDictionaryAsync(e => e.Id, cancellationToken);
        var muscleIds = planExercises.Where(e => e.TargetMuscleGroupId.HasValue).Select(e => e.TargetMuscleGroupId!.Value).Distinct().ToList();
        var muscles = await _unitOfWork.Repository<MuscleGroup>().GetQueryable().Where(m => muscleIds.Contains(m.Id)).ToDictionaryAsync(m => m.Id, m => m.Name, cancellationToken);
        var meta = ReadMeta(plan);

        return new TrainingProgramDto
        {
            Id = plan.Id,
            Version = plan.ProgramVersion ?? 0,
            Status = plan.Status.ToString(),
            Source = plan.Source.ToString(),
            Name = plan.Name,
            Focus = meta.Focus,
            SplitType = plan.SplitType,
            SplitLabel = meta.SplitLabel,
            StartDate = plan.StartDate,
            EndDate = plan.EndDate,
            BlockWeeks = plan.BlockWeeks ?? _options.BlockWeeks,
            CurrentWeek = CurrentWeek(plan),
            IsDeload = plan.IsDeload,
            PreviousPlanId = plan.PreviousPlanId,
            EngineVersion = plan.EngineVersion,
            SplitReasons = meta.SplitReasons,
            Notes = meta.Notes,
            MetadataWarnings = meta.Warnings,
            Changes = meta.Changes,
            WeeklyVolume = meta.Volume,
            Days = days.Select(d =>
            {
                var items = planExercises.Where(e => e.WorkoutPlanDayId == d.Id).ToList();
                return new TrainingDayDto
                {
                    WorkoutPlanDayId = d.Id,
                    DayOfWeek = d.DayOfWeek.ToString(),
                    Order = d.Order,
                    Title = d.Title ?? string.Empty,
                    Focus = d.Focus,
                    EstimatedMinutes = Personalization.MyPlanService.EstimateMinutes(items),
                    Exercises = items.Select(e =>
                    {
                        var ex = exercises.GetValueOrDefault(e.ExerciseId);
                        var c = catalog.GetValueOrDefault(e.ExerciseId);
                        return new TrainingExerciseDto
                        {
                            PlanExerciseId = e.Id,
                            ExerciseId = e.ExerciseId,
                            Name = ex?.Name ?? string.Empty,
                            ImageUrl = ex?.ImageUrl,
                            VideoUrl = ex?.VideoUrl,
                            Order = e.Order,
                            TargetMuscleGroupId = e.TargetMuscleGroupId,
                            TargetMuscle = e.TargetMuscleGroupId is long mid ? muscles.GetValueOrDefault(mid) : null,
                            Mechanic = c?.Mechanic.ToString(),
                            Sets = e.Sets,
                            RepsMin = e.Reps,
                            RepsMax = e.RepsMax,
                            DurationSeconds = e.DurationSeconds,
                            TargetRir = e.TargetRir,
                            TargetRirLabel = e.TargetRir is int rir ? PrescriptionRules.RirLabel(rir) : null,
                            RestSeconds = e.RestSeconds,
                            WeightGuidance = e.WeightGuidance?.ToString(),
                            SuggestedWeightKg = e.SuggestedWeightKg,
                            WeightInstruction = WeightInstruction(e),
                            SelectionReason = e.SelectionReason,
                            Alternatives = IdList.Parse(e.AlternativeExerciseIds)
                                .Where(exercises.ContainsKey)
                                .Select(id => new TrainingAlternativeDto
                                {
                                    ExerciseId = id,
                                    Name = exercises[id].Name,
                                    ImageUrl = exercises[id].ImageUrl,
                                    Mechanic = catalog.GetValueOrDefault(id)?.Mechanic.ToString(),
                                }).ToList(),
                        };
                    }).ToList(),
                };
            }).ToList(),
        };
    }

    private static string? WeightInstruction(WorkoutPlanExercise e) => e.WeightGuidance switch
    {
        WeightGuidanceType.FromHistory when e.SuggestedWeightKg is decimal kg =>
            $"Geçmiş kayıtlarına göre {ProgressionEngine.Kg(kg)} kg ile başla.",
        WeightGuidanceType.RampUp =>
            $"Bu hareket için kaydın yok. İlk seansta hafif bir ağırlıkla başla, setler arasında küçük artışlarla {e.Reps}-{e.RepsMax} tekrarı {e.TargetRir ?? 2} tekrar daha yapabilecek şekilde tamamlayabileceğin ağırlığı bul.",
        WeightGuidanceType.Bodyweight => "Vücut ağırlığıyla yapılır.",
        _ => null,
    };

    // ------------------------------------------------------------------ Analiz

    public async Task<TrainingProgressDto> GetProgressAsync(long userId, CancellationToken cancellationToken = default)
    {
        var active = await GetActivePlanEntityAsync(userId, cancellationToken);
        var context = await LoadContextAsync(userId, active, cancellationToken);
        var readiness = await ReadinessAsync(active, context, cancellationToken);
        var analysis = PerformanceAnalyzer.Analyze(context.History.ExposuresByExercise, context.Catalog, context.History, readiness, _options, Now);
        return new TrainingProgressDto
        {
            CompletedSessions4Weeks = analysis.CompletedSessions4Weeks,
            CompletedSessions12Weeks = analysis.CompletedSessions12Weeks,
            CompletionRate4Weeks = analysis.CompletionRate4Weeks,
            Exercises = analysis.Exercises.Select(ToDto).ToList(),
            RegionWeeks = analysis.RegionWeeks.Select(r => new TrainingRegionWeekDto
            {
                Region = r.Region.ToString(), Label = MuscleRegionMapper.Label(r.Region), WeekStart = r.WeekStart, Sets = r.Sets, VolumeKg = r.VolumeKg,
            }).ToList(),
            Insights = analysis.Insights.Select(i => new TrainingInsightDto { Type = i.Type, Message = i.Message, Region = i.Region, ExerciseId = i.ExerciseId }).ToList(),
            DataNotes = analysis.DataNotes.ToList(),
        };
    }

    /// <summary>Aktif motor programindaki egzersizler icin bir sonraki seansin progresyon karari.</summary>
    private async Task<IReadOnlyDictionary<long, ProgressionDecision>> ReadinessAsync(WorkoutPlan? active, EngineContext context, CancellationToken cancellationToken)
    {
        var result = new Dictionary<long, ProgressionDecision>();
        if (active is null || !IsEnginePlan(active))
        {
            return result;
        }

        var dayIds = await _unitOfWork.Repository<WorkoutPlanDay>().GetQueryable().Where(d => d.WorkoutPlanId == active.Id).Select(d => d.Id).ToListAsync(cancellationToken);
        var planExercises = await _unitOfWork.Repository<WorkoutPlanExercise>().GetQueryable().Where(e => dayIds.Contains(e.WorkoutPlanDayId) && e.Reps != null).ToListAsync(cancellationToken);
        var discomfort = context.History.RecentFeedback.SelectMany(f => f.DiscomfortExerciseIds).ToHashSet();
        foreach (var pe in planExercises.GroupBy(e => e.ExerciseId).Select(g => g.First()))
        {
            if (!context.History.ExposuresByExercise.TryGetValue(pe.ExerciseId, out var exposures) || !context.Catalog.TryGetValue(pe.ExerciseId, out var c))
            {
                continue;
            }
            result[pe.ExerciseId] = ProgressionEngine.Decide(
                new ProgressionInput(pe.Sets, pe.Reps ?? 1, pe.RepsMax ?? pe.Reps ?? 1, pe.TargetRir, c.LoadIncrementKg, c.IsBodyweight, discomfort.Contains(pe.ExerciseId)),
                exposures, _options);
        }
        return result;
    }

    public async Task<TrainingExerciseProgressDto?> GetExerciseHistoryAsync(long userId, long exerciseId, CancellationToken cancellationToken = default)
    {
        var exposures = await TrainingHistoryReader.LoadExposuresAsync(_unitOfWork, userId, new[] { exerciseId }, null, null, 60, cancellationToken, since: Now.AddDays(-84));
        if (!exposures.ContainsKey(exerciseId))
        {
            return null;
        }
        var catalog = await TrainingHistoryReader.LoadCatalogAsync(_unitOfWork, _options, new[] { exerciseId }, cancellationToken);
        var analysis = PerformanceAnalyzer.Analyze(exposures, catalog, TrainingHistory.Empty, new Dictionary<long, ProgressionDecision>(), _options, Now);
        return analysis.Exercises.Select(ToDto).FirstOrDefault();
    }

    public async Task<TrainingRecoveryDto> GetRecoveryAsync(long userId, CancellationToken cancellationToken = default)
    {
        var active = await GetActivePlanEntityAsync(userId, cancellationToken);
        var context = await LoadContextAsync(userId, active, cancellationToken);
        return ToDto(RecoveryAssessor.Assess(context.History, context.Catalog, _options, Now));
    }

    private static TrainingRecoveryDto ToDto(RecoveryAssessment r) => new()
    {
        Status = r.Status.ToString(),
        Signals = r.Signals.ToList(),
        Recommendations = r.Recommendations.Select(x => new TrainingChangeDto { Type = x.Code, Message = x.Message }).ToList(),
        DiscomfortExerciseIds = r.DiscomfortExerciseIds.ToList(),
        DecliningExerciseIds = r.DecliningExerciseIds.ToList(),
        DaysSinceLastTrained = r.DaysSinceLastTrained.ToDictionary(kv => kv.Key.ToString(), kv => kv.Value),
        DataGaps = r.DataGaps.ToList(),
    };

    private static TrainingExerciseProgressDto ToDto(ExerciseProgress p) => new()
    {
        ExerciseId = p.ExerciseId,
        Name = p.Name,
        Region = p.Region.ToString(),
        ExposureCount = p.ExposureCount,
        FirstEstimatedOneRepMax = p.FirstEstimatedOneRepMax,
        LastEstimatedOneRepMax = p.LastEstimatedOneRepMax,
        EstimatedOneRepMaxChangePercent4Weeks = p.EstimatedOneRepMaxChange4Weeks,
        EstimatedOneRepMaxChangePercent12Weeks = p.EstimatedOneRepMaxChange12Weeks,
        FirstTopWeightKg = p.FirstTopWeightKg,
        LastTopWeightKg = p.LastTopWeightKg,
        LastBestReps = p.LastBestReps,
        TotalVolumeKg = p.TotalVolumeKg,
        Trend = p.Trend.ToString(),
        Points = p.Points.Select(x => new TrainingExercisePointDto
        {
            Date = x.Date, SessionId = x.SessionId, TopWeightKg = x.TopWeightKg, TopReps = x.TopReps,
            EstimatedOneRepMax = x.EstimatedOneRepMax, VolumeKg = x.VolumeKg, WorkingSets = x.WorkingSets,
        }).ToList(),
    };

    /// <summary>WorkoutPlan.ChangeSummary JSON icerigi.</summary>
    internal sealed record ProgramMeta
    {
        public string? Focus { get; init; }
        public string? SplitLabel { get; init; }
        public List<string> SplitReasons { get; init; } = new();
        public List<string> Notes { get; init; } = new();
        public List<string> Warnings { get; init; } = new();
        public List<TrainingChangeDto> Changes { get; init; } = new();
        public List<TrainingRegionVolumeDto> Volume { get; init; } = new();
    }
}
