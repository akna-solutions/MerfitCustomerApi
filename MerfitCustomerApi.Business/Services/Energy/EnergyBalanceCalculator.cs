using MerfitCustomerApi.Business.Common;
using MerfitCustomerApi.Business.Dtos.Customer.Energy;
using MerfitCustomerApi.Business.Services.Nutrition;
using MerfitCustomerApi.Domain.Entities.Enums;

namespace MerfitCustomerApi.Business.Services.Energy;

/// <summary>Bir gunun kalori dengesi hesabi icin saf girdi (veritabanindan bagimsiz).</summary>
public sealed class EnergyDayInput
{
    public DateOnly Date { get; init; }
    public bool IsToday { get; init; }
    /// <summary>Bugun icin gunun gecen orani (0-1); gecmis gunlerde 1.</summary>
    public double ElapsedDayFraction { get; init; } = 1;
    public DateTime NowUtc { get; init; }

    /// <summary>O gune ait (ya da oncesindeki en yakin) tartim, yoksa profil kilosu.</summary>
    public decimal? WeightKg { get; init; }
    public bool WeightFromMeasurement { get; init; }
    public decimal? HeightCm { get; init; }
    /// <summary>O gunku yas.</summary>
    public int? Age { get; init; }
    public Gender? Gender { get; init; }

    public IReadOnlyList<EnergyMealEntry> Meals { get; init; } = Array.Empty<EnergyMealEntry>();
    public EnergyStepDay? Steps { get; init; }
    public IReadOnlyList<EnergyWorkoutEntry> Workouts { get; init; } = Array.Empty<EnergyWorkoutEntry>();

    public int? TargetCalories { get; init; }
    public int? ProteinTarget { get; init; }
    public int? CarbsTarget { get; init; }
    public int? FatTarget { get; init; }
    /// <summary>Hedefin ongordugu denge (hedef - TDEE).</summary>
    public int? PlannedBalance { get; init; }
    /// <summary>"lose" | "maintain" | "gain" | "unknown".</summary>
    public string GoalDirection { get; init; } = "unknown";
}

public sealed record EnergyMealEntry(MealType MealType, decimal Calories, decimal Protein, decimal Carbs, decimal Fat);

/// <summary>DailyActivity satirinin hesaba giren kismi. Saatlik adimlar cihazin yerel saatine (UtcOffsetMinutes) goredir.</summary>
public sealed record EnergyStepDay(
    int TotalSteps, int DistanceMeters, int CaloriesBurned, int[] HourlySteps, int UtcOffsetMinutes, bool IsSuspicious, DateTime LastSyncedAt);

/// <summary>
/// Tamamlanmis bir antrenman. Calories saklanan (sunucu hesapli) net kaloridir; yoksa null.
/// Aktif pencere [CompletedAt - ActiveSeconds, CompletedAt] olarak alinir (duraklatma suresi pencereye girmez).
/// </summary>
public sealed record EnergyWorkoutEntry(
    long SessionId, string Title, WorkoutEnergyKind Kind, DateTime CompletedAtUtc, int? ActiveSeconds, int? Calories);

/// <summary>
/// Gunluk kalori dengesinin merkezi ve saf hesaplayicisi. Veritabanina erismez; ayni girdi her zaman ayni sonucu verir.
///
/// Yontem (uygulama genelinde tek yontem): Harcanan = BMR + adimlardan aktif kalori + antrenman net kalorisi +
/// besinlerin termik etkisi. Aktivite carpani (TDEE = BMR x carpan) harcamada KULLANILMAZ; carpan yalnizca beslenme
/// hedefinin (NutritionGoal) hesabinda vardir. Boylece ayni hareket hem carpanla hem adim/antrenmanla iki kez sayilmaz.
///
/// Cifte sayimi onleyen kurallar:
/// 1. Adim kalorisi (km x kg x 0,5 kcal) ve antrenman kalorisi ((MET-1) x kg x saat) NET'tir: dinlenme enerjisi
///    yalnizca BMR'de bulunur.
/// 2. Antrenmanin aktif penceresine dusen saatlik adimlar adim kalorisinden dusulur; o surenin enerjisi yalnizca
///    antrenmandan gelir (yuruyus/kosu antrenmani sirasinda sayilan adimlar iki kez eklenmez).
/// 3. Yuruyus ve gunluk hareketlilik ayni adimlarin bolunmesidir (saatlik esik); toplamlari adim kalorisidir.
/// 4. Hedef kalori ozellikle "antrenman kalorisi geri eklenerek" buyutulmez; hedef zaten ortalama aktiviteyi icerir.
/// </summary>
public sealed class EnergyBalanceCalculator
{
    private const int HoursPerDay = 24;

    private readonly EnergyBalanceOptions _options;
    private readonly NutritionCalculationOptions _nutrition;

    public EnergyBalanceCalculator(EnergyBalanceOptions options, NutritionCalculationOptions nutrition)
    {
        _options = options;
        _nutrition = nutrition;
    }

    public EnergyDayDto Calculate(EnergyDayInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var fraction = input.IsToday ? Math.Clamp(input.ElapsedDayFraction, 0, 1) : 1;

        var dto = new EnergyDayDto
        {
            Date = input.Date,
            IsToday = input.IsToday,
            ElapsedDayFraction = Math.Round(fraction, 3),
            TargetCalories = input.TargetCalories,
            PlannedBalance = input.PlannedBalance,
            GoalDirection = input.GoalDirection,
        };

        // ---- Alinan ----
        var nutrition = BuildNutrition(input);
        dto.Nutrition = nutrition;
        dto.IntakeCalories = nutrition.Calories;
        if (input.TargetCalories is { } target)
        {
            dto.RemainingCalories = target - nutrition.Calories;
        }

        // ---- Veri durumu ----
        var quality = dto.DataQuality;
        quality.HasMeals = nutrition.ItemCount > 0;
        quality.IsPartialDay = input.IsToday;
        quality.WeightFromMeasurement = input.WeightFromMeasurement;
        quality.WeightKgUsed = input.WeightKg;
        if (!IsPlausible(input.WeightKg, _nutrition.PlausibleMinWeightKg, _nutrition.PlausibleMaxWeightKg))
        {
            quality.MissingProfileFields.Add("weight");
        }

        if (!IsPlausible(input.HeightCm, _nutrition.PlausibleMinHeightCm, _nutrition.PlausibleMaxHeightCm))
        {
            quality.MissingProfileFields.Add("height");
        }

        if (input.Age is not { } age || age < _nutrition.PlausibleMinAge || age > _nutrition.PlausibleMaxAge)
        {
            quality.MissingProfileFields.Add("birthDate");
        }

        quality.ProfileComplete = quality.MissingProfileFields.Count == 0;
        quality.GenderEstimated = input.Gender is null;

        // ---- Harcanan ----
        var expenditure = dto.Expenditure;
        int? bmrFull = quality.ProfileComplete
            ? (int)Math.Round(CalculateBmr(input.Gender, input.WeightKg!.Value, input.HeightCm!.Value, input.Age!.Value), 0, MidpointRounding.AwayFromZero)
            : null;
        expenditure.BmrFullDay = bmrFull;
        expenditure.Bmr = bmrFull is null ? null : (int)Math.Round(bmrFull.Value * fraction, MidpointRounding.AwayFromZero);

        // Antrenmanlar
        var activity = dto.Activity;
        foreach (var workout in input.Workouts.OrderBy(w => w.CompletedAtUtc))
        {
            if (workout.Calories is null)
            {
                quality.WorkoutsWithoutCalories++;
            }

            var minutes = workout.ActiveSeconds is > 0 ? (int)Math.Round(workout.ActiveSeconds.Value / 60d) : 0;
            activity.Workouts.Add(new EnergyWorkoutDto
            {
                SessionId = workout.SessionId,
                Title = workout.Title,
                Kind = workout.Kind.ToString().ToLowerInvariant(),
                CompletedAt = DateTime.SpecifyKind(workout.CompletedAtUtc, DateTimeKind.Utc),
                Minutes = minutes,
                Calories = Math.Max(0, workout.Calories ?? 0),
            });
        }

        activity.WorkoutMinutes = activity.Workouts.Sum(w => w.Minutes);
        activity.WorkoutCalories = activity.Workouts.Sum(w => w.Calories);
        expenditure.Workout = activity.WorkoutCalories;

        // Adimlar (antrenmanla cakisan kisim dusulerek)
        if (input.Steps is { } steps && steps.TotalSteps > 0)
        {
            quality.HasSteps = true;
            quality.StepsVerified = !steps.IsSuspicious;
            quality.StepsLastSyncedAt = DateTime.SpecifyKind(steps.LastSyncedAt, DateTimeKind.Utc);
            quality.StepSyncStale = input.IsToday
                && (input.NowUtc - steps.LastSyncedAt).TotalMinutes > _options.StepSyncStaleMinutes;
            ApplySteps(input, steps, dto);
        }
        else if (input.Steps is { } emptySteps)
        {
            quality.StepsLastSyncedAt = DateTime.SpecifyKind(emptySteps.LastSyncedAt, DateTimeKind.Utc);
            quality.StepSyncStale = input.IsToday
                && (input.NowUtc - emptySteps.LastSyncedAt).TotalMinutes > _options.StepSyncStaleMinutes;
        }

        expenditure.ThermicEffect = (int)Math.Round(nutrition.Calories * _options.ThermicEffectRatio, MidpointRounding.AwayFromZero);

        if (expenditure.Bmr is { } bmr)
        {
            dto.ExpenditureCalories = bmr + expenditure.Walking + expenditure.DailyMovement + expenditure.Workout + expenditure.ThermicEffect;
            dto.Balance = dto.IntakeCalories - dto.ExpenditureCalories;
            dto.BalanceKind = Math.Abs(dto.Balance.Value) <= _options.BalanceToleranceKcal / 3 ? "balanced"
                : dto.Balance > 0 ? "surplus" : "deficit";
        }

        quality.IsComplete = !input.IsToday && quality.HasMeals && dto.ExpenditureCalories is not null;

        Evaluate(dto);
        dto.Insights = BuildInsights(input, dto);
        return dto;
    }

    /// <summary>Mifflin-St Jeor; katsayilar beslenme hedefiyle ayni ayardan gelir.</summary>
    public decimal CalculateBmr(Gender? gender, decimal weightKg, decimal heightCm, int age)
    {
        var baseValue = (_nutrition.BmrWeightCoefficient * weightKg)
            + (_nutrition.BmrHeightCoefficient * heightCm)
            - (_nutrition.BmrAgeCoefficient * age);
        return baseValue + gender switch
        {
            Gender.Male => _nutrition.BmrMaleConstant,
            Gender.Female => _nutrition.BmrFemaleConstant,
            _ => _nutrition.BmrOtherGenderConstant,
        };
    }

    /// <summary>
    /// Antrenmanin aktif penceresiyle cakisan adimlari saat kovalarina orantili olarak bulur. Adimlarin saati cihazin
    /// yerel saatidir: kova h, UTC'de [gun + h saat - offset, +1 saat) araligidir.
    /// </summary>
    public static double[] OverlappingStepsPerHour(DateOnly date, EnergyStepDay steps, IEnumerable<EnergyWorkoutEntry> workouts)
    {
        var overlap = new double[HoursPerDay];
        var hourly = steps.HourlySteps;
        if (hourly is null || hourly.Length != HoursPerDay)
        {
            return overlap;
        }

        var dayStartUtc = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc).AddMinutes(-steps.UtcOffsetMinutes);
        foreach (var workout in workouts)
        {
            if (workout.ActiveSeconds is not > 0)
            {
                continue;
            }

            var end = DateTime.SpecifyKind(workout.CompletedAtUtc, DateTimeKind.Utc);
            var start = end.AddSeconds(-workout.ActiveSeconds.Value);
            for (var h = 0; h < HoursPerDay; h++)
            {
                var hourStart = dayStartUtc.AddHours(h);
                var hourEnd = hourStart.AddHours(1);
                var minutes = (Min(end, hourEnd) - Max(start, hourStart)).TotalMinutes;
                if (minutes > 0)
                {
                    overlap[h] += hourly[h] * Math.Min(1, minutes / 60d);
                }
            }
        }

        for (var h = 0; h < HoursPerDay; h++)
        {
            overlap[h] = Math.Min(overlap[h], hourly[h]);
        }

        return overlap;
    }

    private void ApplySteps(EnergyDayInput input, EnergyStepDay steps, EnergyDayDto dto)
    {
        var hourly = steps.HourlySteps is { Length: HoursPerDay } ? steps.HourlySteps : null;
        var kcalPerStep = (double)steps.CaloriesBurned / steps.TotalSteps;

        double walkingSteps = 0, movementSteps = 0, overlapSteps = 0;
        if (hourly is null)
        {
            movementSteps = steps.TotalSteps;
        }
        else
        {
            var overlap = OverlappingStepsPerHour(input.Date, steps, input.Workouts);
            for (var h = 0; h < HoursPerDay; h++)
            {
                overlapSteps += overlap[h];
                var remaining = hourly[h] - overlap[h];
                if (remaining >= _options.WalkingHourStepThreshold)
                {
                    walkingSteps += remaining;
                }
                else
                {
                    movementSteps += remaining;
                }
            }
        }

        var activity = dto.Activity;
        activity.Steps = steps.TotalSteps;
        activity.DistanceMeters = steps.DistanceMeters;
        activity.WalkingSteps = (int)Math.Round(walkingSteps);
        activity.OverlappingSteps = (int)Math.Round(overlapSteps);

        var expenditure = dto.Expenditure;
        expenditure.Walking = (int)Math.Round(walkingSteps * kcalPerStep, MidpointRounding.AwayFromZero);
        expenditure.DailyMovement = (int)Math.Round(movementSteps * kcalPerStep, MidpointRounding.AwayFromZero);
        // Yuvarlama farki nedeniyle toplam adim kalorisini asmasin.
        var counted = steps.CaloriesBurned - (int)Math.Round(overlapSteps * kcalPerStep, MidpointRounding.AwayFromZero);
        if (expenditure.Walking + expenditure.DailyMovement > counted)
        {
            expenditure.DailyMovement = Math.Max(0, counted - expenditure.Walking);
        }

        expenditure.DeduplicatedStepCalories = steps.CaloriesBurned - expenditure.Walking - expenditure.DailyMovement;
    }

    private void Evaluate(EnergyDayDto dto)
    {
        var tol = _options.BalanceToleranceKcal;
        var quality = dto.DataQuality;

        if (dto.ExpenditureCalories is null)
        {
            Set(dto, EnergyStatuses.InsufficientData, EnergyTones.Blue,
                "Harcanan kaloriyi hesaplamak için profilinde boy, kilo ve doğum tarihi gerekli.");
            return;
        }

        if (!quality.HasMeals)
        {
            Set(dto, EnergyStatuses.InsufficientData, EnergyTones.Blue, dto.IsToday
                ? "Henüz besin kaydı yok. Öğün ekledikçe kalori dengen güncellenir."
                : "Bu gün için besin kaydı yok; kalori dengesi yorumlanamıyor.");
            return;
        }

        if (dto.IsToday)
        {
            // Gun bitmeden fazla/acik yargisi yapilmaz; yalnizca hedefin asilip asilmadigi soylenir.
            if (dto.RemainingCalories is { } remaining)
            {
                var overLimit = dto.GoalDirection == "gain" ? _options.MaxControlledSurplusKcal : tol;
                if (remaining < -overLimit)
                {
                    Set(dto, EnergyStatuses.Surplus, EnergyTones.Orange,
                        $"Günlük kalori hedefini {Fmt(-remaining)} kcal aştın.");
                    return;
                }

                Set(dto, EnergyStatuses.InProgress, EnergyTones.Blue, remaining >= 0
                    ? $"Hedefine {Fmt(remaining)} kcal kaldı. Gün devam ediyor; harcaman şu ana kadarki kısımdır."
                    : $"Hedefinin {Fmt(-remaining)} kcal üzerindesin. Gün devam ediyor.");
                return;
            }

            Set(dto, EnergyStatuses.InProgress, EnergyTones.Blue, "Gün devam ediyor; harcaman şu ana kadarki kısımdır.");
            return;
        }

        var balance = dto.Balance!.Value;
        var intakeTooLow = dto.IntakeCalories < _nutrition.MinimumDailyCalories;
        switch (dto.GoalDirection)
        {
            case "lose":
                if (balance < -_options.MaxSustainableDeficitKcal || intakeTooLow)
                {
                    Set(dto, EnergyStatuses.Attention, EnergyTones.Red, intakeTooLow
                        ? "Kayıtlı alımın güvenli alt sınırın altında. Çok düşük kalori sürdürülebilir değildir; tüm öğünlerini kaydettiğinden emin ol."
                        : $"Açığın {Fmt(-balance)} kcal; önerilen sınırın üzerinde. Daha ılımlı bir açık kas kaybını ve yorgunluğu azaltır.");
                }
                else if (balance <= -tol)
                {
                    Set(dto, EnergyStatuses.Aligned, EnergyTones.Green, $"{Fmt(-balance)} kcal açık: kilo verme hedefinle uyumlu.");
                }
                else if (balance < tol)
                {
                    Set(dto, EnergyStatuses.Neutral, EnergyTones.Blue, "Dengeye yakın bir gün; kilo verme için hafif bir açık gerekir.");
                }
                else
                {
                    Set(dto, EnergyStatuses.Surplus, EnergyTones.Orange, $"{Fmt(balance)} kcal fazla: kilo verme hedefinin tersi yönde.");
                }

                break;
            case "gain":
                if (balance > _options.MaxControlledSurplusKcal)
                {
                    Set(dto, EnergyStatuses.Surplus, EnergyTones.Orange, $"{Fmt(balance)} kcal fazla: kontrollü fazlanın üzerinde, yağ artışı hızlanabilir.");
                }
                else if (balance >= tol)
                {
                    Set(dto, EnergyStatuses.Aligned, EnergyTones.Green, $"{Fmt(balance)} kcal kontrollü fazla: kilo alma hedefinle uyumlu.");
                }
                else if (balance > -tol)
                {
                    Set(dto, EnergyStatuses.Neutral, EnergyTones.Blue, "Dengeye yakın bir gün; kilo almak için hafif bir fazla gerekir.");
                }
                else
                {
                    Set(dto, EnergyStatuses.Attention, EnergyTones.Red, $"{Fmt(-balance)} kcal açık: kilo alma hedefin için enerji alımın yetersiz kaldı.");
                }

                break;
            case "maintain":
                if (Math.Abs(balance) <= _options.MaintainBandKcal)
                {
                    Set(dto, EnergyStatuses.Aligned, EnergyTones.Green, "Yaklaşık enerji dengesindesin: kilo koruma hedefinle uyumlu.");
                }
                else if (balance > 0)
                {
                    Set(dto, EnergyStatuses.Surplus, EnergyTones.Orange, $"{Fmt(balance)} kcal fazla: kilo koruma için dengenin üzerinde.");
                }
                else if (balance < -_options.MaxSustainableDeficitKcal || intakeTooLow)
                {
                    Set(dto, EnergyStatuses.Attention, EnergyTones.Red, $"{Fmt(-balance)} kcal açık: kilo koruma hedefi için fazla düşük alım.");
                }
                else
                {
                    Set(dto, EnergyStatuses.Neutral, EnergyTones.Blue, $"{Fmt(-balance)} kcal açık: kilo koruma için dengenin altında.");
                }

                break;
            default:
                Set(dto, balance >= tol ? EnergyStatuses.Surplus : EnergyStatuses.Neutral, balance >= tol ? EnergyTones.Orange : EnergyTones.Blue,
                    balance >= tol ? $"{Fmt(balance)} kcal kalori fazlası." : balance <= -tol ? $"{Fmt(-balance)} kcal kalori açığı." : "Yaklaşık enerji dengesi.");
                break;
        }
    }

    private List<EnergyInsightDto> BuildInsights(EnergyDayInput input, EnergyDayDto dto)
    {
        var insights = new List<EnergyInsightDto>();
        var quality = dto.DataQuality;

        if (!quality.ProfileComplete)
        {
            insights.Add(Insight("profile_incomplete", EnergyTones.Blue,
                "Bazal metabolizman için profilinde " + string.Join(", ", quality.MissingProfileFields.Select(FieldLabel)) + " eksik."));
        }

        if (quality.StepSyncStale)
        {
            insights.Add(Insight("step_sync_stale", EnergyTones.Blue,
                "Adım verin bir süredir senkronize edilmedi; harcanan kalori eksik görünebilir."));
        }
        else if (input.IsToday && !quality.HasSteps)
        {
            insights.Add(Insight("no_steps", EnergyTones.Blue, "Bugün için adım verisi yok; yürüyüş harcaması hesaba katılamadı."));
        }

        if (quality.HasSteps && !quality.StepsVerified)
        {
            insights.Add(Insight("steps_unverified", EnergyTones.Blue, "Bu günün adım verisi olağan dışı göründüğü için harcama tahmini daha belirsiz."));
        }

        if (quality.WorkoutsWithoutCalories > 0)
        {
            insights.Add(Insight("workout_without_calories", EnergyTones.Blue,
                "Süresi belirlenemeyen antrenman kaloriye katılmadı."));
        }

        insights.AddRange(ActivityInsights(dto));
        if (!quality.HasMeals || dto.Balance is null)
        {
            return insights;
        }

        // Hedefe gore gunluk sonuc (yalnizca tamamlanmis gunde kesin ifade)
        if (quality.IsComplete && dto.Status == EnergyStatuses.Aligned)
        {
            if (dto.GoalDirection == "lose" && input.PlannedBalance is < 0 && dto.Balance <= input.PlannedBalance)
            {
                insights.Add(Insight("deficit_reached", EnergyTones.Green, "Bu gün hedeflediğin kalori açığına ulaştın."));
            }
            else
            {
                insights.Add(Insight("balance_aligned", EnergyTones.Green, "Günlük kalori dengen hedefinle uyumlu."));
            }
        }

        // Protein: tamamlanmis gun ya da gunun buyuk kismi gecmisken
        if (input.ProteinTarget is > 0 && (quality.IsComplete || dto.ElapsedDayFraction >= 0.85))
        {
            var ratio = (decimal)dto.Nutrition.Protein / input.ProteinTarget.Value;
            if (ratio < _options.LowProteinRatio)
            {
                insights.Add(Insight("protein_low", EnergyTones.Orange,
                    $"Protein alımın hedefinin altında kaldı ({dto.Nutrition.Protein} / {input.ProteinTarget} g)."));
            }
            else if (ratio >= 1)
            {
                insights.Add(Insight("protein_reached", EnergyTones.Green, "Protein hedefine ulaştın."));
            }
        }

        return insights;
    }

    private IEnumerable<EnergyInsightDto> ActivityInsights(EnergyDayDto dto)
    {
        var expenditure = dto.Expenditure;
        if (expenditure.Walking >= 50)
        {
            yield return Insight("walking_contribution", EnergyTones.Blue,
                $"Yürüyüş aktiviten günlük enerji harcamana yaklaşık {Fmt(expenditure.Walking)} kcal katkı sağladı.");
        }

        if (expenditure.Workout > 0)
        {
            yield return Insight("workout_contribution", EnergyTones.Blue,
                $"Antrenman yaptığın için enerji ihtiyacın yaklaşık {Fmt(expenditure.Workout)} kcal arttı.");
        }

        if (dto.Activity.OverlappingSteps >= 100)
        {
            yield return Insight("steps_deduplicated", EnergyTones.Blue,
                $"Antrenman sırasında atılan yaklaşık {Fmt(dto.Activity.OverlappingSteps)} adım iki kez sayılmadı.");
        }
    }

    private EnergyNutritionDto BuildNutrition(EnergyDayInput input)
    {
        var meals = input.Meals;
        var result = new EnergyNutritionDto
        {
            Calories = Round(meals.Sum(m => m.Calories)),
            Protein = Round(meals.Sum(m => m.Protein)),
            Carbs = Round(meals.Sum(m => m.Carbs)),
            Fat = Round(meals.Sum(m => m.Fat)),
            ItemCount = meals.Count,
            ProteinTarget = input.ProteinTarget,
            CarbsTarget = input.CarbsTarget,
            FatTarget = input.FatTarget,
        };

        foreach (var type in Enum.GetValues<MealType>())
        {
            var items = meals.Where(m => m.MealType == type).ToList();
            result.Meals.Add(new EnergyMealDto
            {
                Type = type.ToString().ToLowerInvariant(),
                Label = MealLogWriter.MapMealType(type),
                Calories = Round(items.Sum(m => m.Calories)),
                Protein = Round(items.Sum(m => m.Protein)),
                Carbs = Round(items.Sum(m => m.Carbs)),
                Fat = Round(items.Sum(m => m.Fat)),
                ItemCount = items.Count,
            });
        }

        return result;
    }

    private static void Set(EnergyDayDto dto, string status, string tone, string headline)
    {
        dto.Status = status;
        dto.Tone = tone;
        dto.Headline = headline;
    }

    private static EnergyInsightDto Insight(string code, string tone, string text) => new() { Code = code, Tone = tone, Text = text };

    private static string FieldLabel(string field) => field switch
    {
        "weight" => "kilo",
        "height" => "boy",
        "birthDate" => "doğum tarihi",
        _ => field,
    };

    private static bool IsPlausible(decimal? value, decimal min, decimal max) => value is { } v && v >= min && v <= max;

    private static int Round(decimal value) => (int)Math.Round(value, 0, MidpointRounding.AwayFromZero);

    private static string Fmt(int value) => value.ToString("N0", System.Globalization.CultureInfo.GetCultureInfo("tr-TR"));

    private static DateTime Min(DateTime a, DateTime b) => a < b ? a : b;

    private static DateTime Max(DateTime a, DateTime b) => a > b ? a : b;
}
