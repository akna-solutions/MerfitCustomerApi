using MerfitCustomerApi.Business.Common;
using MerfitCustomerApi.Domain.Entities.Enums;

namespace MerfitCustomerApi.Business.Services.WorkoutLab;

/// <summary>
/// Uretilen programin otomatik kalite kontrol listesi. Her kontrol, programin anlik goruntusu
/// (<see cref="WorkoutPlanSimulationResult"/>) uzerinde acikca tanimlanmis bir kurala bakar ve kuralini
/// (<see cref="WorkoutPlanQualityCheck.Rule"/>) sonucla birlikte dondurur.
///
/// Kontroller algoritmanin gercek davranisini (ayni secenekler, ayni fallback zinciri) referans alir.
/// Esikler laboratuvar kurallaridir (<see cref="WorkoutPlanLabOptions"/>); bilimsel bir kalite skoru degildir
/// ve tek bir toplam puana donusturulmez.
/// </summary>
public static class WorkoutPlanQualityChecker
{
    public static List<WorkoutPlanQualityCheck> Evaluate(
        WorkoutPlanSimulationResult result,
        WorkoutPlanSimulationProfile profile,
        WorkoutPlanLabOptions labOptions)
    {
        return new List<WorkoutPlanQualityCheck>
        {
            CheckDuplicates(result),
            CheckConsecutiveMuscleGroups(result),
            CheckEquipment(result),
            CheckDuration(result),
            CheckDayCount(result, profile),
            CheckGoalAlignment(result, labOptions),
            CheckCardio(result),
            CheckExerciseVolume(result, labOptions),
            CheckPremium(result),
            CheckFallback(result),
        };
    }

    private static WorkoutPlanQualityCheck CheckDuplicates(WorkoutPlanSimulationResult result)
    {
        const string rule = "Ayni antrenman, ana havuzda kullanilmamis uygun antrenman varken ikinci kez atanirsa basarisiz; " +
            "uygun antrenman sayisi gun sayisindan azsa (algoritma bu durumda tekrar eder) uyari. Farkli antrenmanlarda ayni " +
            "egzersizin birden fazla gune dusmesi uyari, ayni gun icinde tekrar basarisiz.";
        var details = new List<string>();
        var status = WorkoutPlanQualityCheckStatus.Passed;

        var repeatedWorkouts = result.Days
            .GroupBy(d => d.Workout.Id)
            .Where(g => g.Count() > 1)
            .ToList();
        foreach (var group in repeatedWorkouts)
        {
            var dayLabels = string.Join(", ", group.Select(d => DayLabel(d.DayOfWeek)));
            details.Add($"\"{group.First().Workout.Title}\" {group.Count()} gün atandı ({dayLabels}).");
        }

        var distinctWorkouts = result.Days.Select(d => d.Workout.Id).Distinct().Count();
        if (repeatedWorkouts.Count > 0)
        {
            if (result.MainPoolCount > distinctWorkouts)
            {
                status = WorkoutPlanQualityCheckStatus.Failed;
                details.Insert(0, $"Ana havuzda {result.MainPoolCount} uygun antrenman vardı ancak yalnızca {distinctWorkouts} farklı antrenman kullanıldı.");
            }
            else
            {
                status = WorkoutPlanQualityCheckStatus.Warning;
                details.Insert(0, $"Ana havuzda yalnızca {result.MainPoolCount} uygun antrenman var, {result.Days.Count} gün için tekrar kaçınılmaz.");
            }
        }

        // Ayni gun icinde ayni egzersiz.
        foreach (var day in result.Days)
        {
            var dupInDay = day.Exercises.GroupBy(e => e.ExerciseId).Where(g => g.Count() > 1).ToList();
            foreach (var g in dupInDay)
            {
                status = WorkoutPlanQualityCheckStatus.Failed;
                details.Add($"{DayLabel(day.DayOfWeek)}: \"{g.First().Name}\" aynı gün {g.Count()} kez var.");
            }
        }

        // Farkli antrenmanlarin paylastigi egzersizler (antrenman tekrarindan kaynaklananlar haric).
        var sharedExercises = result.Days
            .GroupBy(d => d.Workout.Id)
            .Select(g => g.First())
            .SelectMany(d => d.Exercises.Select(e => new { d.DayOfWeek, e.ExerciseId, e.Name }))
            .GroupBy(x => x.ExerciseId)
            .Where(g => g.Select(x => x.DayOfWeek).Distinct().Count() > 1)
            .ToList();
        foreach (var g in sharedExercises)
        {
            if (status == WorkoutPlanQualityCheckStatus.Passed)
            {
                status = WorkoutPlanQualityCheckStatus.Warning;
            }

            details.Add($"\"{g.First().Name}\" farklı antrenmanlarda {g.Count()} gün geçiyor ({string.Join(", ", g.Select(x => DayLabel(x.DayOfWeek)))}).");
        }

        var message = status switch
        {
            WorkoutPlanQualityCheckStatus.Failed => "Program, kaçınılabilecek bir tekrar içeriyor.",
            WorkoutPlanQualityCheckStatus.Warning => repeatedWorkouts.Count > 0
                ? "Uygun antrenman sayısı yetersiz olduğu için bazı günler tekrar ediyor."
                : "Bazı egzersizler farklı günlerde tekrar ediyor.",
            _ => "Hiçbir antrenman veya egzersiz gereksiz yere tekrar etmiyor.",
        };

        return Check("DuplicateExercises", "Gereksiz egzersiz tekrarı", status, message, rule, details);
    }

    private static WorkoutPlanQualityCheck CheckConsecutiveMuscleGroups(WorkoutPlanSimulationResult result)
    {
        const string rule = "Takvimde art arda gelen iki antrenman gününde (Pazar→Pazartesi dahil) ana antrenmanın kas grubu aynıysa " +
            "veya iki günün egzersizlerinin yarısından fazlası aynı birincil kas grubunu çalıştırıyorsa uyarı.";
        var details = new List<string>();
        var ordered = result.Days.OrderBy(d => d.DayIndex).ToList();
        var adjacentPairs = 0;

        for (var i = 0; i < ordered.Count; i++)
        {
            var current = ordered[i];
            var next = ordered[(i + 1) % ordered.Count];
            if (ordered.Count < 2 || ReferenceEquals(current, next))
            {
                break;
            }

            // Son gun -> ilk gun yalnizca Pazar -> Pazartesi ise ardisiktir (haftalik tekrar).
            var isAdjacent = (next.DayIndex - current.DayIndex + 7) % 7 == 1;
            if (!isAdjacent)
            {
                continue;
            }

            adjacentPairs++;
            if (current.Workout.MuscleGroupId is not null && current.Workout.MuscleGroupId == next.Workout.MuscleGroupId)
            {
                details.Add($"{DayLabel(current.DayOfWeek)} ve {DayLabel(next.DayOfWeek)}: iki ana antrenman da {current.Workout.MuscleGroupName ?? "aynı kas grubu"} odaklı.");
                continue;
            }

            var overlap = MuscleOverlap(current, next);
            if (overlap is not null)
            {
                details.Add($"{DayLabel(current.DayOfWeek)} ve {DayLabel(next.DayOfWeek)}: egzersizlerin çoğu {overlap} çalıştırıyor.");
            }
        }

        if (details.Count > 0)
        {
            return Check("ConsecutiveMuscleGroups", "Ardışık günlerde aynı kas grubu", WorkoutPlanQualityCheckStatus.Warning,
                "Aynı kas grubu art arda günlerde çalıştırılıyor; toparlanma süresi kısalabilir.", rule, details);
        }

        var passMessage = adjacentPairs == 0
            ? "Programda art arda gelen antrenman günü yok."
            : "Art arda gelen günlerde farklı kas grupları çalıştırılıyor.";
        return Check("ConsecutiveMuscleGroups", "Ardışık günlerde aynı kas grubu", WorkoutPlanQualityCheckStatus.Passed, passMessage, rule, details);
    }

    private static string? MuscleOverlap(SimulationDay a, SimulationDay b)
    {
        static (long? Id, string? Name, int Count)? Dominant(SimulationDay day)
        {
            var top = day.Exercises
                .Where(e => e.MuscleGroupId is not null)
                .GroupBy(e => e.MuscleGroupId)
                .OrderByDescending(g => g.Count())
                .FirstOrDefault();
            return top is null ? null : (top.Key, top.First().MuscleGroupName, top.Count());
        }

        var da = Dominant(a);
        var db = Dominant(b);
        if (da is null || db is null || da.Value.Id != db.Value.Id)
        {
            return null;
        }

        var majorityA = da.Value.Count * 2 > a.Exercises.Count;
        var majorityB = db.Value.Count * 2 > b.Exercises.Count;
        return majorityA && majorityB ? (da.Value.Name ?? "aynı kas grubunu") : null;
    }

    private static WorkoutPlanQualityCheck CheckEquipment(WorkoutPlanSimulationResult result)
    {
        const string rule = "Ana antrenmanın ve antrenman sonu kardiyonun katalogda tanımlı tüm ekipmanları (WorkoutEquipment) " +
            "profile verilen ekipmanlar içinde olmalı; değilse başarısız.";
        var owned = result.Equipment.Select(e => e.Id).ToHashSet();
        var details = new List<string>();

        foreach (var day in result.Days)
        {
            foreach (var workout in new[] { day.Workout, day.Cardio }.Where(w => w is not null))
            {
                var missing = workout!.RequiredEquipment.Where(e => !owned.Contains(e.Id)).ToList();
                if (missing.Count > 0)
                {
                    details.Add($"{DayLabel(day.DayOfWeek)}: \"{workout.Title}\" için eksik ekipman: {string.Join(", ", missing.Select(m => m.Name))}.");
                }
            }
        }

        if (details.Count > 0)
        {
            return Check("EquipmentCompatibility", "Ekipman uyumu", WorkoutPlanQualityCheckStatus.Failed,
                "Seçilen ekipmanla yapılamayan antrenman var.", rule, details);
        }

        var message = result.Equipment.Count == 0
            ? "Tüm antrenmanlar ekipmansız yapılabiliyor."
            : "Tüm antrenmanlar seçilen ekipmanla yapılabiliyor.";
        return Check("EquipmentCompatibility", "Ekipman uyumu", WorkoutPlanQualityCheckStatus.Passed, message, rule, details);
    }

    private static WorkoutPlanQualityCheck CheckDuration(WorkoutPlanSimulationResult result)
    {
        var rule = $"Her günün süresi (ana antrenman + kardiyo, katalogdaki dakika değerleri) tercih edilen süre ({result.DurationPreferenceMin} dk) " +
            $"içindeyse başarılı, algoritmanın toleransı (+{result.DurationToleranceMin} dk, en fazla {result.MaxDurationMin} dk) içindeyse uyarı, " +
            "toleransı aşıyorsa başarısız.";
        var details = new List<string>();
        var status = WorkoutPlanQualityCheckStatus.Passed;

        foreach (var day in result.Days)
        {
            if (day.EstimatedDurationMin > result.MaxDurationMin)
            {
                status = WorkoutPlanQualityCheckStatus.Failed;
                details.Add($"{DayLabel(day.DayOfWeek)}: {day.EstimatedDurationMin} dk (tolerans sınırı {result.MaxDurationMin} dk).");
            }
            else if (day.EstimatedDurationMin > result.DurationPreferenceMin)
            {
                if (status == WorkoutPlanQualityCheckStatus.Passed)
                {
                    status = WorkoutPlanQualityCheckStatus.Warning;
                }

                details.Add($"{DayLabel(day.DayOfWeek)}: {day.EstimatedDurationMin} dk (tercihten {day.EstimatedDurationMin - result.DurationPreferenceMin} dk uzun, tolerans içinde).");
            }
        }

        var message = status switch
        {
            WorkoutPlanQualityCheckStatus.Failed => result.PoolStage == nameof(Personalization.Models.WorkoutCandidatePoolStage.EquipmentFreeAnyDifficulty)
                ? "Bazı günler tolerans dışında; ekipmansız fallback havuzu süre filtresi uygulamaz."
                : "Bazı günler tercih edilen sürenin toleransını aşıyor.",
            WorkoutPlanQualityCheckStatus.Warning => "Bazı günler tercih edilen süreden uzun ama tolerans içinde.",
            _ => "Tüm günler tercih edilen süre içinde.",
        };
        if (result.UsedDefaultDuration)
        {
            details.Insert(0, $"Süre seçilmedi; algoritmanın varsayılanı ({result.DurationPreferenceMin} dk) kullanıldı.");
        }

        return Check("DurationFit", "Antrenman süresi", status, message, rule, details);
    }

    private static WorkoutPlanQualityCheck CheckDayCount(WorkoutPlanSimulationResult result, WorkoutPlanSimulationProfile profile)
    {
        const string rule = "Belirli günler seçildiyse program tam olarak o günlerde olmalı; seçilmediyse gün sayısı seçilen haftalık " +
            "sayıya (algoritmanın 2-7 sınırıyla) eşit olmalı. Uymazsa başarısız; sayı 2-7 dışından sınıra çekildiyse uyarı.";
        var details = new List<string>();
        var actualCodes = result.Days.Select(d => d.DayOfWeek).ToList();
        details.Add($"Program günleri: {string.Join(", ", actualCodes.Select(DayLabel))}.");

        var requestedDays = profile.TrainingDays
            .Select(code => TrainingDayCodes.TryParseCode(code, out var d) ? TrainingDayCodes.ToCode(d) : null)
            .Where(code => code is not null)
            .Distinct()
            .ToList();

        if (requestedDays.Count > 0)
        {
            var match = requestedDays.Count == actualCodes.Count && requestedDays.All(actualCodes.Contains);
            return Check("TrainingDayCount", "Haftalık gün sayısı", match ? WorkoutPlanQualityCheckStatus.Passed : WorkoutPlanQualityCheckStatus.Failed,
                match
                    ? $"Program seçilen {requestedDays.Count} günde ({string.Join(", ", requestedDays.Select(DayLabel!))})."
                    : "Program günleri seçilen günlerle uyuşmuyor.",
                rule, details);
        }

        var requested = profile.TrainingDaysPerWeek;
        var expected = result.TrainingDays;
        if (actualCodes.Count != expected)
        {
            return Check("TrainingDayCount", "Haftalık gün sayısı", WorkoutPlanQualityCheckStatus.Failed,
                $"Beklenen {expected} gün, programda {actualCodes.Count} gün var.", rule, details);
        }

        if (requested is null)
        {
            details.Insert(0, $"Gün sayısı seçilmedi; algoritmanın varsayılanı ({expected}) kullanıldı.");
        }
        else if (requested != expected)
        {
            return Check("TrainingDayCount", "Haftalık gün sayısı", WorkoutPlanQualityCheckStatus.Warning,
                $"Seçilen {requested} gün, algoritma sınırı nedeniyle {expected} güne çekildi.", rule, details);
        }

        return Check("TrainingDayCount", "Haftalık gün sayısı", WorkoutPlanQualityCheckStatus.Passed,
            $"Program haftada {expected} gün.", rule, details);
    }

    private static WorkoutPlanQualityCheck CheckGoalAlignment(WorkoutPlanSimulationResult result, WorkoutPlanLabOptions options)
    {
        var rule = $"Ana antrenmanların hedef uyum puanı (FitScore: antrenmanın Hipertrofi/Kuvvet/Kardiyo puanlarının bu hedefin " +
            $"ağırlıklarıyla ortalaması, 0-100) ortalaması ≥ {options.GoalFitPassThreshold} ise başarılı, {options.GoalFitWarningThreshold}-" +
            $"{options.GoalFitPassThreshold} arası uyarı, altı başarısız. Puanı olmayan antrenman varsa en fazla uyarı. " +
            "Eşikler laboratuvar kuralıdır, bilimsel bir ölçüt değildir.";
        var details = new List<string>
        {
            $"Hedef kuralları: {result.SetsRule} set, {result.RepsMinRule}-{result.RepsMaxRule} tekrar, {result.RestSecondsRule} sn dinlenme.",
        };

        var mainWorkouts = result.Days.Select(d => d.Workout).GroupBy(w => w.Id).Select(g => g.First()).ToList();
        var scored = mainWorkouts.Where(w => w.FitScore is not null).ToList();
        var unscored = mainWorkouts.Count - scored.Count;

        if (scored.Count == 0)
        {
            return Check("GoalAlignment", "Hedef uyumu", WorkoutPlanQualityCheckStatus.Warning,
                "Seçilen antrenmanların hiçbirinde hedef özellik puanı yok; hedef uyumu ölçülemedi (sıralama yalnızca deneyim, süre ve öne çıkan bonusuyla yapıldı).",
                rule, details);
        }

        var average = Math.Round(scored.Average(w => w.FitScore!.Value), 1);
        foreach (var w in scored)
        {
            details.Add($"\"{w.Title}\": FitScore {Math.Round(w.FitScore!.Value, 1)} (H {w.Hypertrophy?.ToString() ?? "-"}, K {w.Strength?.ToString() ?? "-"}, Kd {w.CardioScore?.ToString() ?? "-"}).");
        }

        var status = average >= options.GoalFitPassThreshold
            ? WorkoutPlanQualityCheckStatus.Passed
            : average >= options.GoalFitWarningThreshold
                ? WorkoutPlanQualityCheckStatus.Warning
                : WorkoutPlanQualityCheckStatus.Failed;

        if (unscored > 0)
        {
            details.Add($"{unscored} antrenmanın hedef özellik puanı yok; ortalamaya katılmadı.");
            if (status == WorkoutPlanQualityCheckStatus.Passed)
            {
                status = WorkoutPlanQualityCheckStatus.Warning;
            }
        }

        var message = status switch
        {
            WorkoutPlanQualityCheckStatus.Passed => $"Ana antrenmanların ortalama hedef uyumu {average}.",
            WorkoutPlanQualityCheckStatus.Warning => unscored > 0 && average >= options.GoalFitPassThreshold
                ? $"Ortalama hedef uyumu {average}, ancak bazı antrenmanlar puanlanmamış."
                : $"Ortalama hedef uyumu {average}; hedefle kısmen uyumlu.",
            _ => $"Ortalama hedef uyumu {average}; seçilen antrenmanlar hedefle zayıf uyumlu.",
        };
        return Check("GoalAlignment", "Hedef uyumu", status, message, rule, details);
    }

    private static WorkoutPlanQualityCheck CheckCardio(WorkoutPlanSimulationResult result)
    {
        var rule = $"Hedefin haftalık kardiyo sayısı ({result.CardioTarget}, gün sayısıyla sınırlı) karşılanmalı. Eksikse uyarı. " +
            "İki kardiyo günü art arda geliyorsa ve aralarına boş gün koymak mümkünse uyarı. Direnç antrenmanı bulunamadığı için " +
            "ana günlerde kardiyo ağırlıklı antrenman kullanıldıysa uyarı.";
        var details = new List<string>();
        var status = WorkoutPlanQualityCheckStatus.Passed;
        var cardioDays = result.Days
            .Where(d => d.Cardio is not null || d.IsMixedCardioDay)
            .OrderBy(d => d.DayIndex)
            .ToList();

        details.Add(cardioDays.Count == 0
            ? "Programda kardiyo bileşeni yok."
            : $"Kardiyo günleri: {string.Join(", ", cardioDays.Select(d => d.Cardio is null ? $"{DayLabel(d.DayOfWeek)} (karma ana antrenman)" : $"{DayLabel(d.DayOfWeek)} ({d.Cardio.Title}, {d.Cardio.DurationMin} dk)"))}.");

        string message;
        if (result.CardioDays < result.CardioTarget)
        {
            status = WorkoutPlanQualityCheckStatus.Warning;
            message = $"Hedef haftada {result.CardioTarget} kardiyo, programda {result.CardioDays}. Uygun kardiyo antrenmanı yok ya da süre bütçesi yetmedi.";
        }
        else
        {
            message = result.CardioTarget == 0
                ? "Bu hedef için kardiyo öngörülmüyor."
                : $"Haftalık kardiyo hedefi ({result.CardioTarget}) karşılandı.";
        }

        // Art arda kardiyo gunleri (gunler arasinda bosluk birakmak mumkunken).
        var dayCount = result.Days.Count;
        if (cardioDays.Count >= 2 && cardioDays.Count * 2 <= dayCount)
        {
            for (var i = 1; i < cardioDays.Count; i++)
            {
                if (cardioDays[i].DayIndex - cardioDays[i - 1].DayIndex == 1)
                {
                    status = WorkoutPlanQualityCheckStatus.Warning;
                    details.Add($"{DayLabel(cardioDays[i - 1].DayOfWeek)} ve {DayLabel(cardioDays[i].DayOfWeek)} art arda kardiyo içeriyor.");
                }
            }
        }

        if (result.CardioDominantMainDaysFallback)
        {
            status = WorkoutPlanQualityCheckStatus.Warning;
            details.Add("Uygun direnç antrenmanı bulunamadığı için ana günlerde kardiyo ağırlıklı antrenman kullanıldı.");
        }

        return Check("CardioDistribution", "Kardiyo dağılımı", status, message, rule, details);
    }

    private static WorkoutPlanQualityCheck CheckExerciseVolume(WorkoutPlanSimulationResult result, WorkoutPlanLabOptions options)
    {
        var rule = $"Her ana antrenman gününde en az {options.MinExercisesPerDay} egzersiz olmalı; daha az ise uyarı, hiç yoksa başarısız. " +
            $"Algoritma bir güne en fazla {result.MaxExercisesPerDay} egzersiz koyar.";
        var details = new List<string>();
        var status = WorkoutPlanQualityCheckStatus.Passed;

        foreach (var day in result.Days)
        {
            if (day.Exercises.Count == 0)
            {
                status = WorkoutPlanQualityCheckStatus.Failed;
                details.Add($"{DayLabel(day.DayOfWeek)}: \"{day.Workout.Title}\" antrenmanının katalogda egzersizi yok.");
            }
            else if (day.Exercises.Count < options.MinExercisesPerDay)
            {
                if (status == WorkoutPlanQualityCheckStatus.Passed)
                {
                    status = WorkoutPlanQualityCheckStatus.Warning;
                }

                details.Add($"{DayLabel(day.DayOfWeek)}: {day.Exercises.Count} egzersiz.");
            }

            if (day.TruncatedExerciseCount > 0)
            {
                details.Add($"{DayLabel(day.DayOfWeek)}: katalogdaki {day.TruncatedExerciseCount} egzersiz günlük sınır nedeniyle programa alınmadı.");
            }
        }

        var message = status switch
        {
            WorkoutPlanQualityCheckStatus.Failed => "Egzersizi olmayan antrenman günü var.",
            WorkoutPlanQualityCheckStatus.Warning => "Bazı günlerde egzersiz sayısı az.",
            _ => $"Her günde en az {options.MinExercisesPerDay} egzersiz var (toplam {result.Metrics.TotalExercises}).",
        };
        return Check("ExerciseVolume", "Yeterli egzersiz", status, message, rule, details);
    }

    private static WorkoutPlanQualityCheck CheckPremium(WorkoutPlanSimulationResult result)
    {
        const string rule = "Free kullanıcıya Premium antrenman yalnızca ücretsiz havuz tamamen boş kalırsa (Fallback-3) atanabilir: " +
            "bu durumda uyarı, aksi halde başarısız. Plus kullanıcı için Premium antrenmanlar serbesttir.";
        var premium = result.Days
            .SelectMany(d => new[] { d.Workout, d.Cardio })
            .Where(w => w is not null && w.IsPremium)
            .Select(w => w!.Title)
            .Distinct()
            .ToList();
        var details = premium.Count == 0
            ? new List<string> { "Programda Premium antrenman yok." }
            : new List<string> { $"Premium antrenmanlar: {string.Join(", ", premium)}." };

        if (result.IncludePremium)
        {
            return Check("PremiumAccess", "Premium erişimi", WorkoutPlanQualityCheckStatus.Passed,
                "Plus kullanıcı; Premium antrenmanlar aday havuzunda.", rule, details);
        }

        if (premium.Count == 0)
        {
            return Check("PremiumAccess", "Premium erişimi", WorkoutPlanQualityCheckStatus.Passed,
                "Free kullanıcıya Premium antrenman atanmadı.", rule, details);
        }

        return result.PremiumLastResortUsed
            ? Check("PremiumAccess", "Premium erişimi", WorkoutPlanQualityCheckStatus.Warning,
                "Ücretsiz havuz boş kaldığı için Free kullanıcıya Premium antrenman son çare olarak atandı.", rule, details)
            : Check("PremiumAccess", "Premium erişimi", WorkoutPlanQualityCheckStatus.Failed,
                "Free kullanıcıya Premium antrenman atandı.", rule, details);
    }

    private static WorkoutPlanQualityCheck CheckFallback(WorkoutPlanSimulationResult result)
    {
        const string rule = "Aday havuzu kullanıcının seviyesi, süresi ve ekipmanıyla oluştuysa başarılı. Bitişik zorluk (Fallback-1), " +
            "ekipmansız tüm antrenmanlar (Fallback-2) veya Premium son çare (Fallback-3) kullanıldıysa uyarı.";
        var details = new List<string>
        {
            $"Uygun aday: {result.EligibleCount}, ana gün havuzu: {result.MainPoolCount}.",
        };

        var (status, message) = result.PoolStage switch
        {
            nameof(Personalization.Models.WorkoutCandidatePoolStage.AdjacentDifficulty) => (WorkoutPlanQualityCheckStatus.Warning,
                "Seviyeye uygun aday bulunamadı; bitişik zorluk seviyeleri kullanıldı (Fallback-1)."),
            nameof(Personalization.Models.WorkoutCandidatePoolStage.EquipmentFreeAnyDifficulty) => (WorkoutPlanQualityCheckStatus.Warning,
                "Seviye/süre/ekipman havuzları boş kaldı; zorluk ve süre filtresi olmadan ekipmansız antrenmanlar kullanıldı (Fallback-2)."),
            _ => (WorkoutPlanQualityCheckStatus.Passed, "Birincil aday havuzu kullanıldı; fallback gerekmedi."),
        };

        if (result.PremiumLastResortUsed)
        {
            status = WorkoutPlanQualityCheckStatus.Warning;
            details.Add("Ücretsiz havuz boş kaldı; Premium antrenmanlar son çare olarak açıldı (Fallback-3).");
        }

        return Check("FallbackUsage", "Fallback kullanımı", status, message, rule, details);
    }

    private static WorkoutPlanQualityCheck Check(string code, string title, WorkoutPlanQualityCheckStatus status, string message, string rule, List<string> details) =>
        new() { Code = code, Title = title, Status = status, Message = message, Rule = rule, Details = details };

    private static readonly Dictionary<string, string> DayLabels = new()
    {
        ["Mon"] = "Pazartesi",
        ["Tue"] = "Salı",
        ["Wed"] = "Çarşamba",
        ["Thu"] = "Perşembe",
        ["Fri"] = "Cuma",
        ["Sat"] = "Cumartesi",
        ["Sun"] = "Pazar",
    };

    internal static string DayLabel(string? code) => code is not null && DayLabels.TryGetValue(code, out var label) ? label : code ?? "-";
}
