using MerfitCustomerApi.Business.Common;
using MerfitCustomerApi.Domain.Entities.Enums;
using MerfitCustomerApi.Domain.Exceptions;

namespace MerfitCustomerApi.Business.Services.Training;

/// <summary>
/// Deterministik program olusturucu. Mevcut egzersiz veritabanindan (yalnizca verilen katalog listesinden)
/// secim yapar; yeni egzersiz uretmez. Ayni girdi her zaman ayni programi uretir (esitlikte egzersiz Id'si).
///
/// Adimlar: split -> gunler -> bolge basina haftalik hacim hedefi -> gun/bolge basina set -> egzersiz secimi
/// (bilesik once, patern ve kas tekrarini onleyerek) -> recete (set/tekrar/RIR/dinlenme) -> seans suresine sigdirma
/// -> hacim ust siniri -> agirlik yonlendirmesi (gecmis yoksa kalibrasyon, uydurma agirlik yok).
/// </summary>
public sealed class ProgramBuilder
{
    private static readonly MuscleRegion[] RegionOrder =
    {
        MuscleRegion.Quadriceps, MuscleRegion.Chest, MuscleRegion.Back, MuscleRegion.Hamstrings, MuscleRegion.Shoulders,
        MuscleRegion.Glutes, MuscleRegion.Triceps, MuscleRegion.Biceps, MuscleRegion.Calves, MuscleRegion.Core,
    };

    private readonly TrainingEngineOptions _options;

    public ProgramBuilder(TrainingEngineOptions options)
    {
        _options = options;
    }

    public GeneratedProgram Build(
        TrainingProfile profile,
        IReadOnlyList<CatalogExercise> catalog,
        TrainingHistory history,
        ProgramAdjustments adjustments,
        DateTime now)
    {
        var notes = new List<string>();
        var warnings = new List<string>();

        var split = SplitSelector.Select(profile, _options);
        var weekDays = TrainingDayPlanner.ChooseDays(profile.AvailableDays, split.EffectiveDaysPerWeek);
        if (weekDays.Count < split.Days.Count)
        {
            // Musait gun sayisi split'ten azsa (en az 2 musait gun garantilenmez) gunler basa sarar.
            notes.Add("Müsait gün sayısı split gün sayısından az; antrenmanlar müsait günlere sırayla yerleştirildi.");
        }

        var pool = BuildPool(profile, catalog, adjustments, warnings);
        if (pool.Count == 0)
        {
            throw new AppValidationException(
                "Ekipmanına ve tercihlerine uygun egzersiz bulunamadı. Ekipman listeni veya kaçındığın hareketleri kontrol et.");
        }

        var targets = WeeklyTargets(profile, history, adjustments, split, notes);
        var frequency = RegionOrder.ToDictionary(r => r, r => split.Days.Count(d => d.Regions.Contains(r)));

        var usedThisWeek = new Dictionary<long, int>();
        var plannedDays = new List<PlannedDay>();
        // Haftalik dolayli hacim (yardimci kaslardan), kucuk kaslarin dogrudan hedefini azaltmak icin.
        var indirectWeekly = RegionOrder.ToDictionary(r => r, _ => 0m);

        // Once buyuk kaslar tum gunlerde, sonra kucuk kaslar; boylece pres / cekis hareketlerinin triceps / biceps'e
        // yazdigi yardimci hacim, kol egzersizlerinin set sayisindan dusulebilir.
        var dayAllocations = split.Days.Select(_ => new List<(MuscleRegion Region, CatalogExercise Exercise, int Sets, string Reason, IReadOnlyList<long> Alternatives)>()).ToList();
        foreach (var largePhase in new[] { true, false })
        {
            for (var dayIndex = 0; dayIndex < split.Days.Count; dayIndex++)
            {
                var template = split.Days[dayIndex];
                var allocation = dayAllocations[dayIndex];
                foreach (var region in RegionOrder.Where(r => template.Regions.Contains(r) && IsLargePhase(r) == largePhase))
                {
                    var weeklyTarget = targets.GetValueOrDefault(region);
                    if (weeklyTarget <= 0 || frequency[region] == 0)
                    {
                        continue;
                    }

                    var weeklyDirect = largePhase ? weeklyTarget : Math.Max(MinWeekly(region), weeklyTarget - indirectWeekly[region]);
                    var daySets = (int)Math.Round(weeklyDirect / frequency[region], MidpointRounding.AwayFromZero);
                    if (daySets <= 0)
                    {
                        continue;
                    }

                    var exerciseCount = daySets <= 4 || region is MuscleRegion.Calves or MuscleRegion.Core or MuscleRegion.Glutes
                        ? 1
                        : Math.Min(_options.MaxExercisesPerRegionPerDay, (int)Math.Ceiling(daySets / 4m));
                    var perExercise = SplitSets(daySets, exerciseCount);

                    for (var slot = 0; slot < exerciseCount; slot++)
                    {
                        var chosen = Choose(region, slot, template, dayIndex, split, pool, allocation, usedThisWeek, profile, history);
                        if (chosen is null)
                        {
                            if (slot == 0)
                            {
                                warnings.Add($"{MuscleRegionMapper.Label(region)} için uygun egzersiz bulunamadı ({template.Title}).");
                            }
                            break;
                        }

                        allocation.Add((region, chosen.Value.Exercise, perExercise[slot], chosen.Value.Reason, chosen.Value.Alternatives));
                        usedThisWeek[chosen.Value.Exercise.Id] = usedThisWeek.GetValueOrDefault(chosen.Value.Exercise.Id) + 1;
                        if (largePhase)
                        {
                            foreach (var secondary in chosen.Value.Exercise.SecondaryRegions.Where(indirectWeekly.ContainsKey))
                            {
                                indirectWeekly[secondary] += perExercise[slot] * _options.SecondaryMuscleSetWeight;
                            }
                        }
                    }
                }
            }
        }

        for (var dayIndex = 0; dayIndex < split.Days.Count; dayIndex++)
        {
            var template = split.Days[dayIndex];
            var ordered = dayAllocations[dayIndex]
                .OrderBy(a => a.Exercise.Mechanic == ExerciseMechanicType.Compound ? 0 : 1)
                .ThenBy(a => a.Region == MuscleRegion.Core ? 1 : 0)
                .ThenBy(a => Array.IndexOf(RegionOrder, a.Region))
                .ThenBy(a => a.Exercise.Id)
                .ToList();

            var exercises = new List<PlannedExercise>();
            var mainLiftAssigned = false;
            foreach (var a in ordered)
            {
                var isMain = !mainLiftAssigned && a.Exercise.Mechanic == ExerciseMechanicType.Compound;
                mainLiftAssigned |= isMain;
                exercises.Add(Prescribe(a.Exercise, a.Region, a.Sets, isMain, profile, history, adjustments, a.Reason, a.Alternatives, now));
            }

            exercises = FitToDuration(exercises, profile, template, notes);
            exercises = exercises.Select((e, i) => e with { Order = i + 1 }).ToList();
            var dow = weekDays.Count == 0 ? DayOfWeek.Monday : weekDays[dayIndex % weekDays.Count];
            plannedDays.Add(new PlannedDay(dow, dayIndex + 1, template, exercises, EstimateMinutes(exercises)));
        }

        plannedDays = EnforceVolumeCaps(plannedDays, targets, notes);
        var volume = ComputeVolume(plannedDays, targets);

        if (adjustments.IsDeload)
        {
            notes.Add($"Deload versiyonu: setler yaklaşık %{(int)((1 - _options.DeloadSetFactor) * 100)} azaltıldı, hedef RIR artırıldı ve geçmişten gelen ağırlıklar %{_options.DeloadLoadReductionPercent:0} düşürüldü.");
        }

        notes.AddRange(adjustments.Reasons);
        AddMetadataWarnings(pool, warnings);

        return new GeneratedProgram
        {
            Focus = profile.Focus,
            Split = split,
            Days = plannedDays,
            WeeklyVolume = volume,
            Notes = notes.Distinct().ToList(),
            MetadataWarnings = warnings.Distinct().ToList(),
            IsDeload = adjustments.IsDeload,
            BlockWeeks = adjustments.IsDeload ? 1 : _options.BlockWeeks,
        };
    }

    private static bool IsLargePhase(MuscleRegion region) => MuscleRegionMapper.IsLarge(region);

    private decimal MinWeekly(MuscleRegion region) => MuscleRegionMapper.IsLarge(region) ? 4m : 2m;

    private List<CatalogExercise> BuildPool(TrainingProfile profile, IReadOnlyList<CatalogExercise> catalog, ProgramAdjustments adjustments, List<string> warnings)
    {
        var pool = catalog
            .Where(e => RegionOrder.Contains(e.PrimaryRegion))
            .Where(e => ExerciseMetadataResolver.IsAvailable(e, profile.EquipmentIds, profile.HasGymAccess))
            .Where(e => !profile.AvoidedExerciseIds.Contains(e.Id))
            .Where(e => !adjustments.ExcludedExerciseIds.Contains(e.Id))
            .Where(e => !profile.LimitedRegions.Contains(e.PrimaryRegion))
            .ToList();

        var otherCount = catalog.Count(e => e.PrimaryRegion == MuscleRegion.Other);
        if (otherCount > 0)
        {
            warnings.Add($"{otherCount} egzersizin ana kas grubu bir bölgeyle eşlenemedi ve programa alınmadı.");
        }

        return pool;
    }

    private Dictionary<MuscleRegion, decimal> WeeklyTargets(
        TrainingProfile profile, TrainingHistory history, ProgramAdjustments adjustments, SplitDecision split, List<string> notes)
    {
        var baseSets = profile.Experience switch
        {
            ExperienceLevel.Advanced => _options.AdvancedWeeklySets,
            ExperienceLevel.Intermediate => _options.IntermediateWeeklySets,
            _ => _options.BeginnerWeeklySets,
        };
        var goalFactor = profile.Focus switch
        {
            TrainingFocus.Hypertrophy => 1m,
            TrainingFocus.Strength => 0.8m,
            TrainingFocus.FatLoss => 0.85m,
            _ => 0.7m,
        };

        var factor = goalFactor * adjustments.VolumeFactor;
        if (history.CompletionRate is double rate && history.ScheduledSessionsLast4Weeks >= 4 && rate < _options.LowAdherenceThreshold)
        {
            factor *= _options.LowAdherenceVolumeFactor;
            notes.Add($"Son 4 haftada antrenmanların %{(int)Math.Round(rate * 100)}'i tamamlandı; seansları tamamlamayı kolaylaştırmak için haftalık hacim %{(int)Math.Round((1 - _options.LowAdherenceVolumeFactor) * 100)} azaltıldı.");
        }

        if (adjustments.IsDeload)
        {
            factor *= _options.DeloadSetFactor;
        }

        var result = new Dictionary<MuscleRegion, decimal>();
        foreach (var region in RegionOrder)
        {
            if (!split.Days.Any(d => d.Regions.Contains(region)))
            {
                continue;
            }

            if (profile.LimitedRegions.Contains(region))
            {
                notes.Add($"{MuscleRegionMapper.Label(region)} kısıtlı bölge olarak işaretlendiği için doğrudan çalıştırılmadı.");
                continue;
            }

            var target = baseSets * factor;
            if (!MuscleRegionMapper.IsLarge(region)) target *= _options.SmallMuscleFactor;
            if (region == MuscleRegion.Glutes) target *= _options.GluteDirectFactor;
            if (profile.PriorityRegions.Contains(region)) target *= _options.PriorityMuscleFactor;
            if (adjustments.RegionFactors.TryGetValue(region, out var rf)) target *= rf;

            var cap = MuscleRegionMapper.IsLarge(region) ? _options.MaxWeeklySetsLargeMuscle : _options.MaxWeeklySetsSmallMuscle;
            result[region] = Math.Round(Math.Clamp(target, adjustments.IsDeload ? 1m : MinWeekly(region), cap), 1);
        }

        if (profile.PriorityRegions.Count > 0)
        {
            notes.Add("Öncelikli kas grupların için haftalık hacim artırıldı: "
                + string.Join(", ", profile.PriorityRegions.Select(MuscleRegionMapper.Label)) + ".");
        }

        return result;
    }

    private static int[] SplitSets(int total, int count)
    {
        var result = new int[count];
        for (var i = 0; i < count; i++)
        {
            result[i] = total / count + (i < total % count ? 1 : 0);
        }
        return result;
    }

    private (CatalogExercise Exercise, string Reason, IReadOnlyList<long> Alternatives)? Choose(
        MuscleRegion region,
        int slot,
        DayTemplate template,
        int dayIndex,
        SplitDecision split,
        List<CatalogExercise> pool,
        List<(MuscleRegion Region, CatalogExercise Exercise, int Sets, string Reason, IReadOnlyList<long> Alternatives)> dayAllocation,
        Dictionary<long, int> usedThisWeek,
        TrainingProfile profile,
        TrainingHistory history)
    {
        var usedToday = dayAllocation.Select(a => a.Exercise.Id).ToHashSet();
        var patternsToday = dayAllocation.Where(a => a.Exercise.Pattern.HasValue).Select(a => a.Exercise.Pattern!.Value).ToList();
        var regionCandidates = pool.Where(e => e.PrimaryRegion == region && !usedToday.Contains(e.Id)).ToList();
        if (regionCandidates.Count == 0)
        {
            return null;
        }

        var allowed = AllowedDifficulties(profile.Experience);
        var withinLevel = regionCandidates.Where(e => allowed.Contains(e.Difficulty)).ToList();
        var candidates = withinLevel.Count > 0 ? withinLevel : regionCandidates;
        var usingFallbackDifficulty = withinLevel.Count == 0;

        // Ayni gunun tekrar eden sablonu (A/B) varsa orta / ileri seviyede cesitlilik, baslangicta tutarlilik.
        var hasTwin = split.Days.Count(d => d.Focus == template.Focus) > 1;

        var scored = candidates
            .Select(e => (Exercise: e, Score: Score(e, region, slot, patternsToday, usedThisWeek, hasTwin, profile, history)))
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Exercise.Id)
            .ToList();

        var best = scored[0].Exercise;
        var alternatives = pool
            .Where(e => e.PrimaryRegion == region && e.Id != best.Id)
            .Select(e => (Exercise: e, Score: Score(e, region, slot, patternsToday, usedThisWeek, hasTwin, profile, history)))
            .OrderByDescending(x => x.Exercise.Mechanic == best.Mechanic)
            .ThenByDescending(x => x.Score)
            .ThenBy(x => x.Exercise.Id)
            .Take(3)
            .Select(x => x.Exercise.Id)
            .ToList();

        var reasons = new List<string> { $"{MuscleRegionMapper.Label(region)} için {(best.Mechanic == ExerciseMechanicType.Compound ? "bileşik" : "izolasyon")} hareket" };
        if (profile.PreferredExerciseIds.Contains(best.Id)) reasons.Add("tercih ettiğin egzersiz");
        if (history.CurrentProgramExerciseIds.Contains(best.Id)) reasons.Add("önceki programla süreklilik");
        else if (history.ExposuresByExercise.ContainsKey(best.Id)) reasons.Add("daha önce yaptığın hareket");
        if (usingFallbackDifficulty) reasons.Add("seviyene uygun alternatif bulunamadığı için bir üst zorluk");
        if (best.MechanicInferred) reasons.Add("bileşik/izolasyon bilgisi tahmini");

        return (best, string.Join(", ", reasons) + ".", alternatives);
    }

    private static HashSet<DifficultyLevel> AllowedDifficulties(ExperienceLevel experience) => experience switch
    {
        ExperienceLevel.Advanced => new() { DifficultyLevel.Beginner, DifficultyLevel.Intermediate, DifficultyLevel.Advanced },
        ExperienceLevel.Intermediate => new() { DifficultyLevel.Beginner, DifficultyLevel.Intermediate },
        _ => new() { DifficultyLevel.Beginner },
    };

    private static int Score(
        CatalogExercise e,
        MuscleRegion region,
        int slot,
        List<MovementPattern> patternsToday,
        Dictionary<long, int> usedThisWeek,
        bool hasTwin,
        TrainingProfile profile,
        TrainingHistory history)
    {
        var score = 0;
        var compound = e.Mechanic == ExerciseMechanicType.Compound;
        if (MuscleRegionMapper.IsLarge(region))
        {
            // Buyuk kasta ilk slot bilesik, ikinci slot farkli patern (genelde izolasyon) tercih edilir.
            score += slot == 0 ? (compound ? 30 : 0) : (compound ? 5 : 15);
        }
        else if (region != MuscleRegion.Core)
        {
            score += compound ? 0 : 10;
        }

        var expLevel = (int)profile.Experience;
        var diff = (int)e.Difficulty;
        score += diff == expLevel ? 15 : diff < expLevel ? 8 : -20;

        if (profile.PreferredExerciseIds.Contains(e.Id)) score += 25;
        if (history.CurrentProgramExerciseIds.Contains(e.Id)) score += 12;
        else if (history.ExposuresByExercise.ContainsKey(e.Id)) score += 8;

        if (e.Pattern.HasValue && e.Pattern != MovementPattern.Isolation && patternsToday.Contains(e.Pattern.Value)) score -= 25;
        if (e.SecondaryRegions.Any(profile.LimitedRegions.Contains)) score -= 40;
        if (e.IsTimeBased && region != MuscleRegion.Core) score -= 15;
        if (e.MechanicInferred) score -= 2;

        if (usedThisWeek.TryGetValue(e.Id, out var times) && times > 0)
        {
            score += profile.Experience == ExperienceLevel.Beginner || !hasTwin ? 5 : -10;
        }

        return score;
    }

    private PlannedExercise Prescribe(
        CatalogExercise exercise,
        MuscleRegion region,
        int sets,
        bool isMainLift,
        TrainingProfile profile,
        TrainingHistory history,
        ProgramAdjustments adjustments,
        string reason,
        IReadOnlyList<long> alternatives,
        DateTime now)
    {
        var scheme = PrescriptionRules.For(profile.Focus, exercise.Mechanic, profile.Experience, isMainLift);
        var rir = adjustments.IsDeload ? Math.Min(4, scheme.TargetRir + 2) : scheme.TargetRir;
        var finalSets = Math.Clamp(sets, adjustments.IsDeload ? 1 : _options.MinSetsPerExercise, _options.MaxSetsPerExercise);
        if (profile.Focus == TrainingFocus.Strength && isMainLift && !adjustments.IsDeload)
        {
            finalSets = Math.Max(3, finalSets);
        }

        var rest = scheme.RestSeconds + adjustments.ExtraRestSeconds;
        var prescription = exercise.IsTimeBased
            ? new Prescription(finalSets, 0, 0, rir, rest, PrescriptionRules.TimeBasedSeconds(profile.Focus, profile.Experience))
            : new Prescription(finalSets, scheme.RepsMin, scheme.RepsMax, rir, rest);

        WeightGuidanceType guidance;
        decimal? suggested = null;
        if (exercise.IsBodyweight || exercise.IsTimeBased)
        {
            guidance = WeightGuidanceType.Bodyweight;
        }
        else
        {
            var exposures = history.ExposuresByExercise.GetValueOrDefault(exercise.Id) ?? Array.Empty<ExerciseExposure>();
            suggested = LoadMath.SuggestFromHistory(exposures, scheme.RepsMin, scheme.RepsMax, scheme.TargetRir, exercise.LoadIncrementKg, now);
            if (suggested.HasValue && adjustments.IsDeload)
            {
                suggested = LoadMath.RoundDown(suggested.Value * (1 - _options.DeloadLoadReductionPercent / 100m), exercise.LoadIncrementKg);
                if (suggested <= 0) suggested = null;
            }
            guidance = suggested.HasValue ? WeightGuidanceType.FromHistory : WeightGuidanceType.RampUp;
        }

        return new PlannedExercise
        {
            Exercise = exercise,
            TargetRegion = region,
            Prescription = prescription,
            WeightGuidance = guidance,
            SuggestedWeightKg = suggested,
            AlternativeExerciseIds = alternatives,
            SelectionReason = reason,
        };
    }

    private int EstimateMinutes(IEnumerable<PlannedExercise> exercises)
    {
        var seconds = exercises.Sum(e => e.Prescription.Sets * ((e.Prescription.DurationSeconds ?? _options.WorkSecondsPerSet) + e.Prescription.RestSeconds));
        return _options.WarmupMinutes + (int)Math.Ceiling(seconds / 60m);
    }

    /// <summary>
    /// Seans suresini asan gunlerde once oncelikli olmayan kucuk kas izolasyonlarinin, sonra diger izolasyonlarin,
    /// en son ikinci bilesiklerin setleri azaltilir; hepsi alt sinirdaysa en dusuk oncelikli egzersiz cikarilir.
    /// Bir bolgenin tek bilesik hareketi ve gunun ana hareketi cikarilmaz.
    /// </summary>
    private List<PlannedExercise> FitToDuration(List<PlannedExercise> exercises, TrainingProfile profile, DayTemplate template, List<string> notes)
    {
        var list = exercises.ToList();
        var budget = profile.SessionMinutes + 5;
        var trimmed = false;

        while (list.Count > _options.MaxExercisesPerDay)
        {
            list.Remove(list.OrderBy(e => Priority(e, profile)).First());
            trimmed = true;
        }

        var guard = 0;
        while (EstimateMinutes(list) > budget && guard++ < 200)
        {
            var reducible = list
                .Where(e => e.Prescription.Sets > _options.MinSetsPerExercise)
                .OrderBy(e => Priority(e, profile))
                .ThenByDescending(e => e.Order)
                .FirstOrDefault();
            if (reducible is not null && Priority(reducible, profile) < 50)
            {
                var index = list.IndexOf(reducible);
                list[index] = reducible with { Prescription = reducible.Prescription with { Sets = reducible.Prescription.Sets - 1 } };
                trimmed = true;
                continue;
            }

            var removable = list
                .Where(e => !(e.Exercise.Mechanic == ExerciseMechanicType.Compound
                              && list.Count(x => x.TargetRegion == e.TargetRegion && x.Exercise.Mechanic == ExerciseMechanicType.Compound) == 1
                              && MuscleRegionMapper.IsLarge(e.TargetRegion)))
                .OrderBy(e => Priority(e, profile))
                .FirstOrDefault();
            if (removable is not null && list.Count > 2)
            {
                list.Remove(removable);
                trimmed = true;
                continue;
            }

            if (reducible is not null)
            {
                var index = list.IndexOf(reducible);
                list[index] = reducible with { Prescription = reducible.Prescription with { Sets = reducible.Prescription.Sets - 1 } };
                trimmed = true;
                continue;
            }

            // Son care: kisa seansta her buyuk kas icin ayri bilesik sigmiyor. En dusuk oncelikli bolgenin
            // (bolge sirasinda en sonda olan) hareketi cikarilir; o kas haftanin diger gunlerinde calisir.
            if (list.Count > 3)
            {
                var drop = list
                    .OrderBy(e => Priority(e, profile))
                    .ThenByDescending(e => Array.IndexOf(RegionOrder, e.TargetRegion))
                    .First();
                list.Remove(drop);
                trimmed = true;
                continue;
            }

            break;
        }

        if (trimmed)
        {
            notes.Add($"{template.Title}: {profile.SessionMinutes} dk seans süresine sığması için set veya egzersiz sayısı azaltıldı.");
        }

        return list;
    }

    /// <summary>Kucuk deger = once kirpilir.</summary>
    private static int Priority(PlannedExercise e, TrainingProfile profile)
    {
        var p = e.Exercise.Mechanic == ExerciseMechanicType.Compound ? 40 : 10;
        if (MuscleRegionMapper.IsLarge(e.TargetRegion)) p += 15;
        if (profile.PriorityRegions.Contains(e.TargetRegion)) p += 30;
        if (e.TargetRegion is MuscleRegion.Calves or MuscleRegion.Core) p -= 5;
        return p;
    }

    private List<PlannedDay> EnforceVolumeCaps(List<PlannedDay> days, Dictionary<MuscleRegion, decimal> targets, List<string> notes)
    {
        foreach (var region in RegionOrder)
        {
            var cap = MuscleRegionMapper.IsLarge(region) ? _options.MaxWeeklySetsLargeMuscle : _options.MaxWeeklySetsSmallMuscle;
            var guard = 0;
            var reduced = false;
            while (TotalFor(days, region) > cap && guard++ < 50)
            {
                var direct = days
                    .SelectMany((d, di) => d.Exercises.Select((e, ei) => (di, ei, e)))
                    .Where(x => x.e.TargetRegion == region)
                    .OrderBy(x => x.e.Exercise.Mechanic == ExerciseMechanicType.Compound ? 1 : 0)
                    .ThenByDescending(x => x.e.Prescription.Sets)
                    .ToList();
                var candidate = direct.FirstOrDefault(x => x.e.Prescription.Sets > _options.MinSetsPerExercise);
                if (candidate.e is not null)
                {
                    var day = days[candidate.di];
                    var exercises = day.Exercises.ToList();
                    exercises[candidate.ei] = candidate.e with { Prescription = candidate.e.Prescription with { Sets = candidate.e.Prescription.Sets - 1 } };
                    days[candidate.di] = day with { Exercises = exercises, EstimatedMinutes = EstimateMinutes(exercises) };
                    reduced = true;
                    continue;
                }

                // Setler en azda ve fazlalik yardimci hacimden geliyor: dogrudan izolasyon hareketi bir gunden
                // cikarilir (bilesikler korunur, cunku ana kaslarin hacmini tasirlar).
                var removable = direct.FirstOrDefault(x => x.e.Exercise.Mechanic == ExerciseMechanicType.Isolation && days[x.di].Exercises.Count > 2);
                if (removable.e is null)
                {
                    break;
                }

                var removeDay = days[removable.di];
                var remaining = removeDay.Exercises.Where((_, i) => i != removable.ei).Select((e, i) => e with { Order = i + 1 }).ToList();
                days[removable.di] = removeDay with { Exercises = remaining, EstimatedMinutes = EstimateMinutes(remaining) };
                reduced = true;
            }

            if (reduced)
            {
                notes.Add($"{MuscleRegionMapper.Label(region)} haftalık hacmi {cap} set üst sınırını aşmaması için azaltıldı (yardımcı kas setleri 0,5 sayıldı).");
            }
        }

        return days;
    }

    private decimal TotalFor(IEnumerable<PlannedDay> days, MuscleRegion region) => days.SelectMany(d => d.Exercises).Sum(e =>
        (e.TargetRegion == region ? e.Prescription.Sets : 0m)
        + (e.Exercise.SecondaryRegions.Contains(region) && e.TargetRegion != region ? e.Prescription.Sets * _options.SecondaryMuscleSetWeight : 0m));

    private List<RegionVolume> ComputeVolume(IReadOnlyList<PlannedDay> days, Dictionary<MuscleRegion, decimal> targets)
    {
        var all = days.SelectMany(d => d.Exercises).ToList();
        return RegionOrder
            .Select(r => new RegionVolume(
                r,
                targets.GetValueOrDefault(r),
                all.Where(e => e.TargetRegion == r).Sum(e => (decimal)e.Prescription.Sets),
                all.Where(e => e.TargetRegion != r && e.Exercise.SecondaryRegions.Contains(r)).Sum(e => e.Prescription.Sets * _options.SecondaryMuscleSetWeight)))
            .Where(v => v.TargetSets > 0 || v.PlannedTotalSets > 0)
            .ToList();
    }

    private static void AddMetadataWarnings(List<CatalogExercise> pool, List<string> warnings)
    {
        var inferredMechanic = pool.Count(e => e.MechanicInferred);
        if (inferredMechanic > 0)
        {
            warnings.Add($"{inferredMechanic} egzersizde bileşik/izolasyon bilgisi veritabanında yok, adından tahmin edildi.");
        }

        var noSecondary = pool.Count(e => e.Mechanic == ExerciseMechanicType.Compound && e.SecondaryRegions.Count == 0);
        if (noSecondary > 0)
        {
            warnings.Add($"{noSecondary} bileşik egzersizde yardımcı kas bilgisi yok; hacme yalnızca ana kas sayıldı.");
        }

        var inferredEquipment = pool.Count(e => e.EquipmentInferred);
        if (inferredEquipment > 0)
        {
            warnings.Add($"{inferredEquipment} egzersizin ekipmanı serbest metinden eşlendi (egzersiz-ekipman bağlantısı yok).");
        }
    }
}
