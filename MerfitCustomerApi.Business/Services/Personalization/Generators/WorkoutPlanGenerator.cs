using MerfitCustomerApi.Business.Common;
using MerfitCustomerApi.Business.Interfaces.Services;
using MerfitCustomerApi.Business.Services.Personalization.Models;
using MerfitCustomerApi.Domain.Entities;
using MerfitCustomerApi.Domain.Entities.Enums;
using MerfitCustomerApi.Domain.Exceptions;
using MerfitCustomerApi.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MerfitCustomerApi.Business.Services.Personalization.Generators;

/// <summary>
/// IWorkoutPlanGenerator'in varsayilan implementasyonu. Tamamen kurallara dayali (deterministik)
/// bir eslestirme + puanlama (scoring) algoritmasi kullanir; yapay zeka/ML KULLANMAZ.
///
/// Algoritma ozet:
/// 1) Hard-rule filtreleme: aktif, kullanicinin deneyim seviyesine uygun zorlukta, kullanicida
///    olan ekipmanla yapilabilen, tercih edilen sureyi (tolerans dahil) asmayan ve kullanicinin
///    abonelik durumuyla erisebildigi (Premium yalnizca aktif Plus uyelere) Workout'lar.
///    Hedef uyum puani bu filtrelerin YERINE GECMEZ: uygun olmayan bir antrenman, yuksek
///    FitScore'u olsa bile aday havuzuna hic girmez.
/// 2) Skorlama: deneyim uyumu + sure uyumu + one cikan icerik bonusu + hedef uyumu (FitScore,
///    bkz. WorkoutGoalFitCalculator); gun atarken ayrica tekrar-onleme (repeat) ve kas grubu
///    dengesi (consecutive muscle group) cezalari uygulanir.
/// 3) Haftalik gun sayisina gore sabit (deterministik) DayOfWeek deseni kullanilarak her gune
///    en yuksek skorlu, henuz kullanilmamis/uygun aday atanir.
/// 4) Her gunun Workout'undaki WorkoutExercise katalog satirlarindan, kullanicinin hedef/deneyim
///    kombinasyonuna gore set/tekrar/dinlenme degerleri hesaplanarak WorkoutPlanExercise uretilir.
///
/// Not (lokasyon): Workout entity'sinde bir "TrainingLocation" alani bulunmuyor (bkz. Faz 2
/// raporu). Bu nedenle lokasyon, ayri bir hard-rule olarak degil, ekipman kontrolu uzerinden
/// dolayli olarak ele alinir: kullanicinin sahip olmadigi ekipman gerektiren antrenmanlar zaten
/// elenir (bkz. asagidaki ekipman filtresi). Bu, mevcut veri modelini bozmayan guvenli bir cozumdur.
/// </summary>
public class WorkoutPlanGenerator : IWorkoutPlanGenerator
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly WorkoutPlanGenerationOptions _options;
    private readonly ILogger<WorkoutPlanGenerator> _logger;

    public WorkoutPlanGenerator(
        IUnitOfWork unitOfWork,
        IOptions<WorkoutPlanGenerationOptions> options,
        ILogger<WorkoutPlanGenerator> logger)
    {
        _unitOfWork = unitOfWork;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<WorkoutPlanGenerationResult> GenerateAsync(long userId, CancellationToken cancellationToken = default)
    {
        var profile = await _unitOfWork.Repository<UserProfile>()
            .FirstOrDefaultAsync(p => p.UserId == userId, cancellationToken);
        if (profile is null)
        {
            throw new NotFoundException(nameof(UserProfile), userId);
        }

        var activeGoal = await _unitOfWork.Repository<UserGoal>()
            .FirstOrDefaultAsync(g => g.UserId == userId && g.IsActive, cancellationToken);
        var goalType = activeGoal?.GoalType ?? profile.Goal ?? FitnessGoal.MaintainWeight;
        var goalWeights = ResolveGoalWeights(goalType);
        var experience = profile.ExperienceLevel ?? ExperienceLevel.Beginner;
        var targetDifficulty = MapExperienceToDifficulty(experience);
        // Kullanici gunleri profilinden tek tek sectiyse o gunler kullanilir; secmediyse haftalik
        // gun sayisina gore varsayilan desen (ResolveDayPattern).
        var preferredDays = TrainingDayCodes.ParseStored(profile.PreferredTrainingDays);
        var trainingDays = preferredDays.Count > 0
            ? preferredDays.Count
            : Math.Clamp(profile.TrainingDaysPerWeek ?? _options.DefaultTrainingDaysPerWeek, 2, 7);
        var durationPreference = profile.WorkoutDurationMin ?? _options.DefaultWorkoutDurationMin;
        var maxDuration = durationPreference + _options.DurationToleranceMin;

        var ownedEquipmentIds = await _unitOfWork.Repository<UserEquipment>()
            .GetQueryable()
            .Where(ue => ue.UserId == userId)
            .Select(ue => ue.EquipmentId)
            .ToListAsync(cancellationToken);

        var ownedSet = ownedEquipmentIds.ToHashSet();

        // Premium (yalnizca Plus) antrenmanlar, aktif aboneligi olmayan kullanicinin normal aday
        // havuzlarina (zorluk/ekipman fallback'leri dahil) girmez; yalnizca ucretsiz havuz tamamen
        // bos kalirsa Fallback-3 ile son care olarak acilir.
        var includePremium = await HasActivePlusSubscriptionAsync(userId, cancellationToken);

        // Aday secimi (zorluk/sure + ekipman fallback zinciri) includePremium bayragiyla calisir.
        async Task<List<Workout>> SelectEligibleAsync(bool allowPremium)
        {
            // Hard-rule 1-3: aktif + zorluk seviyesi uygun + sure toleransi icinde (birincil aday havuzu).
            var primaryCandidates = await GetActiveWorkoutsAsync(new[] { targetDifficulty }, maxDuration, allowPremium, cancellationToken);
            var eligible = await FilterByOwnedEquipmentAsync(primaryCandidates, ownedSet, cancellationToken);

            // Fallback-1: Birincil zorluk seviyesinde uygun aday bulunamazsa (once) veya bulunan
            // adaylarin hicbiri kullanicinin ekipmanina uymazsa, bitisik zorluk seviyelerini de aday
            // havuzuna dahil ederek tekrar dene. (Beginner -> Intermediate, Advanced -> Intermediate,
            // Intermediate -> her ikisi de.) Puanlama asamasinda sure/featured bonuslariyla siralama
            // yapildiginden birebir eslesen adaylar zaten one cikacaktir.
            //
            // NOT: Bu asama, birincil aday havuzu bastan bos olsa bile (orn. kullanicinin sure
            // tercihine uyan Beginner antrenman kalmamissa) calisir - onceki surumde ilk havuz bos
            // oldugunda islem burada hic denenmeden hemen hata firlatiliyordu; bu da ekipmani yeterli
            // olan kullanicilarin bile gereksiz yere "uygun antrenman bulunamadi" hatasi almasina
            // neden oluyordu. Artik zorluk/sure ve ekipman fallback'leri TEK bir kademeli zincir
            // olarak calisiyor.
            if (eligible.Count == 0)
            {
                var fallbackDifficulties = GetFallbackDifficulties(targetDifficulty);
                var expandedCandidates = await GetActiveWorkoutsAsync(fallbackDifficulties, maxDuration, allowPremium, cancellationToken);
                eligible = await FilterByOwnedEquipmentAsync(expandedCandidates, ownedSet, cancellationToken);
            }

            // Fallback-2: Hala uygun aday yoksa, hic ekipman gerektirmeyen (WorkoutEquipment kaydi
            // olmayan - yani bodyweight) tum aktif antrenmanlar son care olarak kullanilir. Zorluk ve
            // sure filtresi de burada genisletilir; ozellikle "Ekipman yok" secen kullanicilarin hic
            // antrenman gorememesi durumunun onune gecer (bkz. is kurali: ekipmansiz kullanici da
            // mutlaka bodyweight bir program alabilmeli).
            if (eligible.Count == 0)
            {
                eligible = await GetEquipmentFreeActiveWorkoutsAsync(allowPremium, cancellationToken);
            }

            return eligible;
        }

        var eligible = await SelectEligibleAsync(includePremium);

        // Fallback-3 (son care): Premium'a erisimi olmayan kullanicinin ucretsiz havuzu bostur
        // (orn. ucretsiz antrenmanlarin hicbiri kullanicinin ekipmanina uymuyor). Program
        // uretilemeden hata vermek yerine Premium antrenmanlar da aday havuzuna acilir. Bu, ucretsiz
        // havuzda hic aday varsa hic devreye girmez; yalnizca "hic program yok" durumunu onler.
        if (eligible.Count == 0 && !includePremium)
        {
            eligible = await SelectEligibleAsync(allowPremium: true);
            if (eligible.Count > 0)
            {
                _logger.LogWarning(
                    "Kullanici {UserId} icin ucretsiz antrenman havuzu bos kaldi; Premium antrenmanlar son care olarak aday havuzuna acildi.",
                    userId);
            }
        }

        if (eligible.Count == 0)
        {
            // Iki farkli basarisizlik nedeni birbirinden ayirt edilir (bkz. is kurali: generic
            // "ekipman yetersiz" mesaji her zaman gercegi yansitmaz): katalogda hic aktif antrenman
            // yoksa bu bir veri/seed sorunudur; aktif antrenman varsa ama hicbiri kullanicinin
            // ekipmanina uymuyorsa bu gercekten bir ekipman uyusmazligidir. Her iki durum da
            // PersonalizationJob.ErrorMessage'a farkli, dogru metinle yazilir (bkz.
            // PersonalizationJobProcessor.FailOrRetryJobAsync) - frontend'e her zaman ayni generic
            // hata gonderilmez.
            var anyActiveWorkoutExists = await _unitOfWork.Repository<Workout>()
                .AnyAsync(w => w.IsActive, cancellationToken);

            if (!anyActiveWorkoutExists)
            {
                throw new PersonalizationGenerationException(
                    "Aktif antrenman kataloğunda hiç kayıt bulunamadığı için kullanıcı profiline uygun antrenman bulunamadı.");
            }

            throw new PersonalizationGenerationException(
                "Kullanicinin sahip oldugu ekipmanlarla karsilanabilecek hicbir aktif antrenmanin ekipman gereksinimi saglanamadi. Lutfen ekipman tercihlerinizi guncelleyin.");
        }

        // Skorlama (deterministik matematiksel kurallar; ML/AI YOK).
        var fitScores = await CalculateGoalFitScoresAsync(eligible, goalType, goalWeights, cancellationToken);
        var scored = eligible
            .Select(w => new ScoredWorkout(w, BaseScore(w, durationPreference, targetDifficulty, fitScores[w.Id])))
            .OrderByDescending(s => s.Score)
            .ThenBy(s => s.Workout.Id) // esit skorda deterministik tie-break
            .ToList();

        var dayPattern = preferredDays.Count > 0 ? preferredDays : ResolveDayPattern(trainingDays);

        var workoutExercisesByWorkout = await _unitOfWork.Repository<WorkoutExercise>()
            .GetQueryable()
            .Where(we => eligible.Select(w => w.Id).Contains(we.WorkoutId))
            .OrderBy(we => we.Order)
            .ToListAsync(cancellationToken);

        var days = new List<WorkoutPlanDayResult>();
        var usedWorkoutIds = new List<long>();
        long? previousMuscleGroupId = null;

        foreach (var dayOfWeek in dayPattern)
        {
            var pick = SelectWorkoutForDay(scored, usedWorkoutIds, previousMuscleGroupId);
            usedWorkoutIds.Add(pick.Id);
            previousMuscleGroupId = pick.MuscleGroupId;

            var exercisesForWorkout = workoutExercisesByWorkout
                .Where(we => we.WorkoutId == pick.Id)
                .Take(_options.MaxExercisesPerDay)
                .ToList();

            var planExercises = exercisesForWorkout
                .Select(we => BuildPlanExercise(we, goalType, experience))
                .ToList();

            days.Add(new WorkoutPlanDayResult
            {
                Day = new WorkoutPlanDay
                {
                    DayOfWeek = dayOfWeek,
                    WorkoutId = pick.Id,
                    Order = 0,
                    CreatedAt = DateTime.UtcNow,
                },
                Exercises = planExercises,
            });
        }

        var summary = BuildSummary(experience, trainingDays, ownedEquipmentIds.Count);

        var plan = new WorkoutPlan
        {
            UserId = userId,
            Name = BuildPlanName(goalType, trainingDays),
            // Kalici bir "Reason/PersonalizationSummary" alani icin yeni bir kolon eklemek yerine
            // WorkoutPlan'in zaten var olan (ve bugune kadar kullanilmayan) Description alani
            // yeniden kullanilir - mevcut entity'yi gereksiz yere genisletmemek icin bilincli tercih.
            Description = summary,
            Goal = goalType,
            StartDate = DateTime.UtcNow.Date,
            EndDate = null,
            IsActive = true,
            IsAiGenerated = false,
            CreatedAt = DateTime.UtcNow,
        };

        return new WorkoutPlanGenerationResult
        {
            Plan = plan,
            Days = days,
            Summary = summary,
        };
    }

    /// <summary>
    /// Kullanicinin hedefine ait agirlik profilini dondurur. Konfigurasyon baslangicta dogrulandigi
    /// icin (bkz. WorkoutPlanGenerationOptionsValidator) eksik profil beklenmez; yine de eksikse
    /// sessizce yanlis puanlama yapmak yerine hata firlatilir.
    /// </summary>
    private GoalAttributeWeights ResolveGoalWeights(FitnessGoal goalType)
    {
        if (_options.AttributeWeightsByGoal.TryGetValue(goalType, out var weights) && weights is not null)
        {
            return weights;
        }

        throw new InvalidOperationException(
            $"{goalType} hedefi icin antrenman ozellik agirliklari yapilandirilmamis ({WorkoutPlanGenerationOptions.SectionName}:{nameof(WorkoutPlanGenerationOptions.AttributeWeightsByGoal)}).");
    }

    /// <summary>
    /// Kullanicinin aktif bir Plus aboneligi olup olmadigini dondurur. Kural, admin tarafindaki
    /// "Plus kullanici" tanimiyla aynidir: Status = Active ve suresi dolmamis (ExpiresAt bos veya ileride).
    /// </summary>
    private async Task<bool> HasActivePlusSubscriptionAsync(long userId, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        return await _unitOfWork.Repository<Subscription>()
            .AnyAsync(
                s => s.UserId == userId
                    && s.Status == SubscriptionStatus.Active
                    && (s.ExpiresAt == null || s.ExpiresAt > now),
                cancellationToken);
    }

    /// <summary>
    /// Uygun adaylarin ozellik puanlarini TEK sorguyla okuyup her aday icin hedef uyum puanini
    /// (FitScore) hesaplar. Puani hesaplanamayan (hic puan kaydi olmayan) adaylar icin null doner.
    /// Eksik puan durumu, aday basina degil uretim basina tek bir log satiriyla raporlanir.
    /// </summary>
    private async Task<Dictionary<long, decimal?>> CalculateGoalFitScoresAsync(
        List<Workout> candidates,
        FitnessGoal goalType,
        GoalAttributeWeights goalWeights,
        CancellationToken cancellationToken)
    {
        var candidateIds = candidates.Select(c => c.Id).ToList();
        var attributeScores = await _unitOfWork.Repository<WorkoutAttributeScore>()
            .GetQueryable()
            .Where(s => candidateIds.Contains(s.WorkoutId))
            .Select(s => new { s.WorkoutId, s.AttributeType, s.Score })
            .ToListAsync(cancellationToken);

        var scoresByWorkout = attributeScores
            .GroupBy(s => s.WorkoutId)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyDictionary<WorkoutAttributeType, int>)g.ToDictionary(x => x.AttributeType, x => x.Score));

        var attributeTypeCount = Enum.GetValues<WorkoutAttributeType>().Length;
        var unscoredCount = 0;
        var partiallyScoredCount = 0;
        var fitScores = new Dictionary<long, decimal?>(candidates.Count);

        foreach (var candidate in candidates)
        {
            if (!scoresByWorkout.TryGetValue(candidate.Id, out var scores))
            {
                unscoredCount++;
                fitScores[candidate.Id] = null;
                continue;
            }

            if (scores.Count < attributeTypeCount)
            {
                partiallyScoredCount++;
            }

            fitScores[candidate.Id] = WorkoutGoalFitCalculator.Calculate(scores, goalWeights);
        }

        if (unscoredCount > 0 || partiallyScoredCount > 0)
        {
            _logger.LogInformation(
                "Workout goal-fit scoring ({Goal}): {UnscoredCount} of {CandidateCount} eligible workouts have no attribute scores (ranked without goal fit), {PartialCount} are partially scored (weights normalized)",
                goalType, unscoredCount, candidates.Count, partiallyScoredCount);
        }

        return fitScores;
    }

    /// <summary>Verilen zorluk seviyeleri ve azami sure icinde, kullanicinin erisebildigi aktif Workout'lari getirir.</summary>
    private async Task<List<Workout>> GetActiveWorkoutsAsync(
        IReadOnlyCollection<DifficultyLevel> difficulties,
        int maxDuration,
        bool includePremium,
        CancellationToken cancellationToken)
    {
        return await _unitOfWork.Repository<Workout>()
            .GetQueryable()
            .Where(w => w.IsActive && difficulties.Contains(w.Difficulty) && w.DurationMin <= maxDuration)
            .Where(w => includePremium || !w.IsPremium)
            .OrderBy(w => w.Id) // deterministik temel siralama; Random KULLANILMAZ
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Verilen aday listesini, kullanicinin sahip oldugu ekipmana gore filtreler. Bir Workout'un
    /// WorkoutEquipment kaydi yoksa (ekipman gerektirmiyorsa) herkes icin uygundur; kaydi varsa
    /// gereken TUM ekipmanlarin kullanicida bulunmasi gerekir.
    /// </summary>
    private async Task<List<Workout>> FilterByOwnedEquipmentAsync(
        List<Workout> candidates,
        HashSet<long> ownedEquipmentIds,
        CancellationToken cancellationToken)
    {
        if (candidates.Count == 0)
        {
            return candidates;
        }

        var candidateIds = candidates.Select(c => c.Id).ToList();
        var requiredEquipmentByWorkout = await _unitOfWork.Repository<WorkoutEquipment>()
            .GetQueryable()
            .Where(we => candidateIds.Contains(we.WorkoutId))
            .GroupBy(we => we.WorkoutId)
            .Select(g => new { WorkoutId = g.Key, EquipmentIds = g.Select(x => x.EquipmentId).ToList() })
            .ToListAsync(cancellationToken);

        var requiredEquipmentMap = requiredEquipmentByWorkout.ToDictionary(x => x.WorkoutId, x => x.EquipmentIds);

        return candidates
            .Where(w => !requiredEquipmentMap.TryGetValue(w.Id, out var required) || required.All(ownedEquipmentIds.Contains))
            .ToList();
    }

    /// <summary>
    /// Hic WorkoutEquipment kaydi olmayan (yani tamamen bodyweight) tum aktif Workout'lari getirir.
    /// Son care fallback olarak kullanilir; zorluk/sure filtresi bilincli olarak uygulanmaz,
    /// Premium erisim kurali ise uygulanir.
    /// </summary>
    private async Task<List<Workout>> GetEquipmentFreeActiveWorkoutsAsync(bool includePremium, CancellationToken cancellationToken)
    {
        var workoutIdsWithEquipment = await _unitOfWork.Repository<WorkoutEquipment>()
            .GetQueryable()
            .Select(we => we.WorkoutId)
            .Distinct()
            .ToListAsync(cancellationToken);

        return await _unitOfWork.Repository<Workout>()
            .GetQueryable()
            .Where(w => w.IsActive && !workoutIdsWithEquipment.Contains(w.Id))
            .Where(w => includePremium || !w.IsPremium)
            .OrderBy(w => w.Id)
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Bir adayin gun atamasindan bagimsiz temel skorunu hesaplar. Her kriter ayri ve bir kez
    /// sayilir: deneyim (zorluk) uyumu, sure uyumu, featured bonusu ve hedef uyumu (FitScore).
    /// </summary>
    private decimal BaseScore(Workout workout, int durationPreference, DifficultyLevel targetDifficulty, decimal? goalFitScore)
    {
        decimal score = ExperienceScore(workout.Difficulty, targetDifficulty);

        if (workout.DurationMin <= durationPreference)
        {
            score += _options.DurationMatchScore;
        }
        else
        {
            // Tolerans icinde ama tercih edilenden uzun: sureye orantili kismi puan.
            var overBy = workout.DurationMin - durationPreference;
            var toleranceRatio = _options.DurationToleranceMin == 0
                ? 0m
                : 1m - ((decimal)overBy / _options.DurationToleranceMin);
            score += Math.Round(_options.DurationMatchScore * Math.Max(0m, toleranceRatio));
        }

        if (workout.IsFeatured)
        {
            score += _options.FeaturedBonusScore;
        }

        // FitScore hesaplanamiyorsa (puan kaydi yok) hedef uyum katkisi eklenmez; aday mevcut
        // kriterlerle siralanmaya devam eder. Hicbir adayin puani yoksa siralama eskisiyle aynidir.
        if (goalFitScore is not null)
        {
            score += goalFitScore.Value * _options.GoalFitMaxScore / 100m;
        }

        return score;
    }

    /// <summary>
    /// Zorluk seviyesi kullanicinin seviyesiyle birebir eslesiyorsa tam deneyim puani, bitisik
    /// seviyedeyse (fallback havuzlari) kismi puan, daha uzaksa (yalnizca bodyweight fallback'inde
    /// gorulebilir) puan vermez.
    /// </summary>
    private int ExperienceScore(DifficultyLevel workoutDifficulty, DifficultyLevel targetDifficulty)
    {
        var distance = Math.Abs((int)workoutDifficulty - (int)targetDifficulty);
        return distance switch
        {
            0 => _options.ExperienceMatchScore,
            1 => _options.AdjacentExperienceMatchScore,
            _ => 0,
        };
    }

    /// <summary>
    /// Skorlanmis adaylar arasindan, tekrar-onleme ve kas grubu dengesi kurallarini dinamik
    /// olarak uygulayarak bir gun icin en uygun Workout'u secer. Tum adaylar zaten kullanildiysa
    /// (haftalik gun sayisi aday sayisindan fazlaysa) en yuksek skorlu adaya donulur - ayni
    /// antrenmanin haftada birden fazla kez gorunmesi, hic antrenman atanmamasindan iyidir.
    /// </summary>
    private Workout SelectWorkoutForDay(List<ScoredWorkout> scored, List<long> usedWorkoutIds, long? previousMuscleGroupId)
    {
        var best = scored
            .Select(s =>
            {
                var adjusted = s.Score;
                if (usedWorkoutIds.Contains(s.Workout.Id))
                {
                    adjusted -= _options.RepeatPenaltyScore;
                }

                if (previousMuscleGroupId is not null && s.Workout.MuscleGroupId == previousMuscleGroupId)
                {
                    adjusted -= _options.ConsecutiveMuscleGroupPenaltyScore;
                }

                return (s.Workout, Adjusted: adjusted);
            })
            .OrderByDescending(x => x.Adjusted)
            .ThenBy(x => x.Workout.Id)
            .First();

        return best.Workout;
    }

    private WorkoutPlanExercise BuildPlanExercise(WorkoutExercise catalogExercise, FitnessGoal goal, ExperienceLevel experience)
    {
        var sets = ResolveSets(goal, experience);
        var repsRange = ResolveRepsRange(goal);
        var restSeconds = _options.RestSecondsByGoal.TryGetValue(goal, out var rest) ? rest : _options.DefaultRestSeconds;

        return new WorkoutPlanExercise
        {
            ExerciseId = catalogExercise.ExerciseId,
            Order = catalogExercise.Order,
            Sets = sets,
            // Sureye dayali (DurationSeconds dolu) egzersizlerde tekrar hedefi anlamsizdir;
            // aksi halde hedef tekrar araliginin alt sinirindan (guvenli/tutucu baslangic) baslanir.
            Reps = catalogExercise.DurationSeconds.HasValue ? null : repsRange.Min,
            RestSeconds = restSeconds,
            DurationSeconds = catalogExercise.DurationSeconds,
            CreatedAt = DateTime.UtcNow,
        };
    }

    private int ResolveSets(FitnessGoal goal, ExperienceLevel experience)
    {
        var key = $"{goal}:{experience}";
        return _options.SetsByGoalAndExperience.TryGetValue(key, out var sets) ? sets : _options.DefaultSets;
    }

    private (int Min, int Max) ResolveRepsRange(FitnessGoal goal)
    {
        return _options.RepsRangeByGoal.TryGetValue(goal.ToString(), out var range) ? range : _options.DefaultRepsRange;
    }

    /// <summary>
    /// Haftalik antrenman gunu sayisina gore sabit, deterministik bir DayOfWeek deseni dondurur.
    /// Gunler string olarak degil DayOfWeek enum'u ile temsil edilir.
    /// </summary>
    private static IReadOnlyList<DayOfWeek> ResolveDayPattern(int trainingDays) => trainingDays switch
    {
        2 => new[] { DayOfWeek.Tuesday, DayOfWeek.Friday },
        3 => new[] { DayOfWeek.Monday, DayOfWeek.Wednesday, DayOfWeek.Friday },
        4 => new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Thursday, DayOfWeek.Saturday },
        5 => new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Friday, DayOfWeek.Saturday },
        6 => new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday },
        _ => new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday },
    };

    private static DifficultyLevel MapExperienceToDifficulty(ExperienceLevel level) => level switch
    {
        ExperienceLevel.Beginner => DifficultyLevel.Beginner,
        ExperienceLevel.Intermediate => DifficultyLevel.Intermediate,
        ExperienceLevel.Advanced => DifficultyLevel.Advanced,
        _ => DifficultyLevel.Beginner,
    };

    /// <summary>
    /// Birincil zorluk seviyesinde ekipman uyusmazligi yasandiginda denenmesi gereken bitisik
    /// zorluk seviyelerini dondurur. Beginner icin Intermediate, Advanced icin Intermediate,
    /// Intermediate icin her ikisi de aday olarak kabul edilir.
    /// </summary>
    private static IReadOnlyList<DifficultyLevel> GetFallbackDifficulties(DifficultyLevel primary) => primary switch
    {
        DifficultyLevel.Beginner => new[] { DifficultyLevel.Intermediate },
        DifficultyLevel.Advanced => new[] { DifficultyLevel.Intermediate },
        _ => new[] { DifficultyLevel.Beginner, DifficultyLevel.Advanced },
    };

    private static string BuildPlanName(FitnessGoal goal, int trainingDays)
    {
        var goalLabel = goal switch
        {
            FitnessGoal.LoseWeight => "Kilo Verme",
            FitnessGoal.BuildMuscle => "Kas Kazanımı",
            FitnessGoal.GetStronger => "Güçlenme",
            FitnessGoal.ImproveEndurance => "Dayanıklılık",
            FitnessGoal.ImproveFitness => "Genel Fitness",
            FitnessGoal.MaintainWeight => "Kilo Koruma",
            _ => "Kişisel Program",
        };

        return $"{goalLabel} - {trainingDays} Gün";
    }

    /// <summary>
    /// Kullaniciya gosterilecek, tamamen kural tabanli (AI kullanilmadan) bir aciklama uretir.
    /// </summary>
    private static string BuildSummary(ExperienceLevel experience, int trainingDays, int equipmentCount)
    {
        var experienceLabel = experience switch
        {
            ExperienceLevel.Beginner => "başlangıç",
            ExperienceLevel.Intermediate => "orta",
            ExperienceLevel.Advanced => "ileri",
            _ => "başlangıç",
        };

        var equipmentPhrase = equipmentCount > 0
            ? "mevcut ekipmanlarına göre"
            : "ekipman gerektirmeyen hareketlerle";

        var experienceCapitalized = char.ToUpperInvariant(experienceLabel[0]) + experienceLabel[1..];

        return $"{experienceCapitalized} seviyene, haftada {trainingDays} gün antrenman tercihine ve {equipmentPhrase} hazırlandı.";
    }

    private readonly record struct ScoredWorkout(Workout Workout, decimal Score);
}
