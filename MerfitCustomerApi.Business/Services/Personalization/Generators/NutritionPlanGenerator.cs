using System.Globalization;
using MerfitCustomerApi.Business.Common;
using MerfitCustomerApi.Business.Services.BodyComposition;
using MerfitCustomerApi.Business.Interfaces.Services;
using MerfitCustomerApi.Business.Services.Nutrition;
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
/// INutritionPlanGenerator'in varsayilan implementasyonu. Tamamen kurallara dayali ve deterministiktir
/// (ayni profil + ayni katalog = ayni plan); yapay zeka/ML veya Random KULLANMAZ.
///
/// Algoritma ozet:
/// 1) Hedefler: NutritionGoal'daki eski deger yerine profil INutritionCalculator.Evaluate ile yeniden
///    hesaplanir (NutritionGoal profilden turetilen bir degerdir; manuel duzenleme yolu yoktur). Gerekli
///    veri eksik/gecersizse varsayilan uydurulmaz, eksik alanlari soyleyen bir hata firlatilir.
/// 2) Katalog: yalnizca admin tarafindan siniflandirilmis (Category + MealSuitability) ve enerji degeri
///    makrolariyla tutarli besinler kullanilir. Alerjisi olan kullaniciya alerjen bilgisi dogrulanmamis
///    besin, vejetaryen/vegan kullaniciya diyet etiketi dogrulanmamis besin ASLA onerilmez; kullanicinin
///    disladigi besinler cikarilir. Isimden kategori/alerjen tahmini yapilmaz.
/// 3) Ogun kompozisyonu: her ogun kategori slotlarindan olusur (or. ogle: protein kaynagi + tahil/baklagil
///    + sebze + istege bagli yag). Slot icin aday besinler hedefe gore puanlanir (kilo vermede protein/lif
///    yuksek ve enerji yogunlugu dusuk, kas kazaniminda protein/karbonhidrat yogun vb.), ayni gun tekrar
///    edilmez, onceki gun ve hafta icindeki kullanima gore cezalandirilir; esitlikler kullanici+gun+besin
///    kimliginden turetilen deterministik bir katsayiyla bozulur.
/// 4) Porsiyon: her besin icin porsiyon biriminden (gram/ml bolunebilir, adet/dilim sayilabilir) gecerli
///    miktar adaylari uretilir; ogunun kalori/protein/karbonhidrat/yag hedefine agirlikli en yakin
///    kombinasyon secilir (her besin kendi miktarini alir). Sonra gun toplami icin ac gozlu bir
///    iyilestirme yapilir. Besin degerleri son miktardan (miktar / referans porsiyon) hesaplanir.
/// 5) Dogrulama: gunluk toplamlar tolerans disindaysa plan basarili sayilmaz gibi gosterilmez; sapmalar
///    plan aciklamasina ve uyarilara yazilir.
/// </summary>
public class NutritionPlanGenerator : INutritionPlanGenerator
{
    private const string PlanName = "Kişisel Beslenme Programı";
    private const int DescriptionMaxLength = 1000; // NutritionPlanConfiguration.Description.HasMaxLength(1000)
    private const int MealNotesMaxLength = 1000; // NutritionPlanMealConfiguration.Notes.HasMaxLength(1000)

    /// <summary>Haftanin gunleri, Pazartesi'den baslayarak (ardisik gun cesitliligi bu siraya gore olculur).</summary>
    private static readonly DayOfWeek[] WeekDays =
    {
        DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday,
        DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday,
    };

    private static readonly MealDefinition[] MealDefinitions =
    {
        new(MealType.Breakfast, "Kahvaltı", new TimeOnly(8, 0), MealSuitability.Breakfast, new[]
        {
            new MealSlot("protein kaynağı", true, false, true, FoodCategory.ProteinSource, FoodCategory.Dairy, FoodCategory.Legume),
            new MealSlot("tahıl/nişasta", true, false, false, FoodCategory.GrainStarch),
            new MealSlot("meyve/sebze", false, false, false, FoodCategory.Fruit, FoodCategory.Vegetable),
            new MealSlot("kuruyemiş/yağ", false, true, false, FoodCategory.NutsSeedsFats),
        }),
        new(MealType.Lunch, "Öğle Yemeği", new TimeOnly(13, 0), MealSuitability.Lunch, new[]
        {
            new MealSlot("protein kaynağı", true, false, true, FoodCategory.ProteinSource, FoodCategory.Legume, FoodCategory.Dairy),
            new MealSlot("tahıl/nişasta", true, false, false, FoodCategory.GrainStarch, FoodCategory.Legume),
            new MealSlot("sebze", false, false, false, FoodCategory.Vegetable),
            new MealSlot("kuruyemiş/yağ", false, true, false, FoodCategory.NutsSeedsFats),
        }),
        new(MealType.Dinner, "Akşam Yemeği", new TimeOnly(19, 0), MealSuitability.Dinner, new[]
        {
            new MealSlot("protein kaynağı", true, false, true, FoodCategory.ProteinSource, FoodCategory.Legume, FoodCategory.Dairy),
            new MealSlot("tahıl/nişasta", true, false, false, FoodCategory.GrainStarch, FoodCategory.Legume),
            new MealSlot("sebze", false, false, false, FoodCategory.Vegetable),
            new MealSlot("kuruyemiş/yağ", false, true, false, FoodCategory.NutsSeedsFats),
        }),
        new(MealType.Snack, "Ara Öğün", new TimeOnly(16, 0), MealSuitability.Snack, new[]
        {
            new MealSlot("ara öğün besini", true, false, false, FoodCategory.Dairy, FoodCategory.Fruit, FoodCategory.NutsSeedsFats, FoodCategory.ProteinSource),
            new MealSlot("meyve/kuruyemiş", false, true, false, FoodCategory.Fruit, FoodCategory.NutsSeedsFats),
        }),
    };

    private static readonly Dictionary<DayOfWeek, string> DayNames = new()
    {
        [DayOfWeek.Monday] = "Pazartesi",
        [DayOfWeek.Tuesday] = "Salı",
        [DayOfWeek.Wednesday] = "Çarşamba",
        [DayOfWeek.Thursday] = "Perşembe",
        [DayOfWeek.Friday] = "Cuma",
        [DayOfWeek.Saturday] = "Cumartesi",
        [DayOfWeek.Sunday] = "Pazar",
    };

    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");

    private readonly IUnitOfWork _unitOfWork;
    private readonly INutritionCalculator _nutritionCalculator;
    private readonly NutritionPlanGenerationOptions _options;
    private readonly ILogger<NutritionPlanGenerator> _logger;
    private readonly BodyCompositionOptions _bodyCompositionOptions;

    public NutritionPlanGenerator(
        IUnitOfWork unitOfWork,
        INutritionCalculator nutritionCalculator,
        IOptions<NutritionPlanGenerationOptions> options,
        ILogger<NutritionPlanGenerator> logger,
        IOptions<BodyCompositionOptions>? bodyCompositionOptions = null)
    {
        _bodyCompositionOptions = bodyCompositionOptions?.Value ?? new BodyCompositionOptions();
        _unitOfWork = unitOfWork;
        _nutritionCalculator = nutritionCalculator;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<NutritionPlanGenerationResult> GenerateAsync(long userId, CancellationToken cancellationToken = default)
    {
        var profile = await _unitOfWork.Repository<UserProfile>()
            .FirstOrDefaultAsync(p => p.UserId == userId, cancellationToken);
        if (profile is null)
        {
            throw new NotFoundException(nameof(UserProfile), userId);
        }

        // Hedef ve vucut kompozisyonu birlikte degerlendirilir: yag orani yuksekse kalori fazlasi, Yag Kaybi'nda
        // dusukse kalori acigi uygulanmaz (bkz. NutritionCalculator). Degerlendirme her uretimde sunucuda yapilir.
        var measurements = await _unitOfWork.Repository<BodyMeasurement>()
            .GetQueryable()
            .Where(m => m.UserId == userId)
            .ToListAsync(cancellationToken);
        var bodyFat = BodyFatAssessor.Assess(profile, measurements, DateTime.UtcNow, _bodyCompositionOptions);
        var evaluation = _nutritionCalculator.Evaluate(profile, bodyFat);
        if (!evaluation.IsComplete)
        {
            throw new PersonalizationGenerationException(
                "Kişisel beslenme programı için profil bilgileriniz eksik veya geçersiz: "
                + string.Join(" ", evaluation.Issues)
                + " Profilinizi güncellediğinizde program otomatik olarak yeniden oluşturulur.");
        }

        var targets = evaluation.Result!;
        var catalog = await LoadCatalogAsync(profile, cancellationToken);
        EnsureCatalogCoversAllMeals(catalog);

        _logger.LogInformation(
            "Nutrition plan catalog for user {UserId}: {Usable} usable of {Total} foods",
            userId, catalog.Foods.Count, catalog.TotalCount);

        var dailyTarget = new MacroTotals(targets.DailyCalories, targets.ProteinTarget, targets.CarbsTarget, targets.FatTarget);
        var weights = ResolveScoringWeights(FitnessGoals.Normalize(profile.Goal));
        foreach (var candidate in catalog.Foods)
        {
            candidate.GoalScore = ScoreForGoal(candidate, weights);
        }

        var warnings = new List<string>();
        var weeklyUsage = new Dictionary<long, int>();
        var previousDayByMeal = new Dictionary<MealType, HashSet<long>>();
        var previousDayAll = new HashSet<long>();
        var days = new List<NutritionPlanDayResult>();
        var daysWithinTolerance = 0;

        for (var dayIndex = 0; dayIndex < WeekDays.Length; dayIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var dayOfWeek = WeekDays[dayIndex];
            var usedToday = new HashSet<long>();
            var plannedMeals = new List<PlannedMeal>();

            foreach (var definition in MealDefinitions)
            {
                var mealTarget = dailyTarget.Scale(MealPercentage(definition.Type));
                var context = new SelectionContext(userId, dayIndex, definition, usedToday, previousDayByMeal, previousDayAll, weeklyUsage);
                var (selection, repeatedToday) = SelectFoods(catalog, context);
                if (repeatedToday)
                {
                    warnings.Add($"{DayNames[dayOfWeek]} {definition.Name.ToLower(Turkish)}: uygun besin az olduğu için aynı gün içinde bir besin tekrarlandı.");
                }

                var portions = SolveMealPortions(selection, mealTarget);
                plannedMeals.Add(new PlannedMeal(definition, mealTarget, portions));

                foreach (var portion in portions)
                {
                    usedToday.Add(portion.Food.Food.Id);
                }
            }

            RefineDayPortions(plannedMeals, dailyTarget);

            var dayTotals = MacroTotals.Zero;
            var mealResults = new List<NutritionPlanMealResult>();
            var todayByMeal = new Dictionary<MealType, HashSet<long>>();
            foreach (var planned in plannedMeals)
            {
                var items = planned.Portions.Select(BuildItem).ToList();
                var mealTotals = items.Aggregate(MacroTotals.Zero, (sum, item) => sum + new MacroTotals(item.Calories, item.Protein, item.Carbs, item.Fat));
                dayTotals += mealTotals;

                mealResults.Add(new NutritionPlanMealResult
                {
                    Meal = new NutritionPlanMeal
                    {
                        MealType = planned.Definition.Type,
                        Name = planned.Definition.Name,
                        PlannedTime = planned.Definition.PlannedTime,
                        Notes = Truncate(BuildMealNote(planned.Target), MealNotesMaxLength),
                        CreatedAt = DateTime.UtcNow,
                    },
                    Items = items,
                });

                var ids = planned.Portions.Select(p => p.Food.Food.Id).ToHashSet();
                todayByMeal[planned.Definition.Type] = ids;
                foreach (var id in ids)
                {
                    weeklyUsage[id] = weeklyUsage.GetValueOrDefault(id) + 1;
                }
            }

            var deviations = DescribeDeviations(dayTotals, dailyTarget);
            if (deviations.Count == 0)
            {
                daysWithinTolerance++;
            }
            else
            {
                warnings.Add($"{DayNames[dayOfWeek]}: {string.Join(", ", deviations)}.");
            }

            days.Add(new NutritionPlanDayResult
            {
                Day = new NutritionPlanDay { DayOfWeek = dayOfWeek, CreatedAt = DateTime.UtcNow },
                Meals = mealResults,
            });

            previousDayByMeal = todayByMeal;
            previousDayAll = todayByMeal.Values.SelectMany(ids => ids).ToHashSet();
        }

        if (warnings.Count > 0)
        {
            _logger.LogInformation(
                "Nutrition plan for user {UserId} generated with {WarningCount} warnings; {DaysWithin}/7 days within tolerance",
                userId, warnings.Count, daysWithinTolerance);
        }

        var summary = Truncate(BuildSummary(profile, targets, catalog, daysWithinTolerance, warnings), DescriptionMaxLength);

        var plan = new NutritionPlan
        {
            UserId = userId,
            Name = PlanName,
            // WorkoutPlanGenerator'daki ayni yaklasim: kalici Reason/Summary metni icin yeni bir
            // kolon eklemek yerine mevcut Description alani yeniden kullanilir.
            Description = summary,
            StartDate = DateTime.UtcNow.Date,
            EndDate = null,
            IsActive = true,
            IsAiGenerated = false,
            CreatedAt = DateTime.UtcNow,
        };

        return new NutritionPlanGenerationResult
        {
            Plan = plan,
            Days = days,
            Summary = summary,
            Targets = targets,
            Warnings = warnings,
        };
    }

    // ---------------------------------------------------------------------------------------------
    // Katalog
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Siniflandirilmis besinleri yukler ve kullanicinin alerji, diyet ve dislama tercihlerine gore
    /// filtreler. Elenen besin sayilari, yetersiz katalog mesajinda kullanilmak uzere tutulur.
    /// </summary>
    private async Task<CatalogSelection> LoadCatalogAsync(UserProfile profile, CancellationToken cancellationToken)
    {
        var repo = _unitOfWork.Repository<Food>();
        var totalCount = await repo.GetQueryable().CountAsync(cancellationToken);

        var classified = await repo.GetQueryable()
            .Where(f => f.Category != null
                && f.Category != FoodCategory.Other
                && f.MealSuitability != null
                && f.MealSuitability != MealSuitability.None)
            .OrderBy(f => f.Id)
            .ToListAsync(cancellationToken);

        var allergies = profile.Allergies ?? FoodAllergen.None;
        var excludedIds = FoodPreferenceFormat.ParseExcludedFoodIds(profile.ExcludedFoodIds);
        var preference = profile.DietaryPreference ?? DietaryPreference.None;

        var selection = new CatalogSelection
        {
            TotalCount = totalCount,
            UnclassifiedCount = totalCount - classified.Count,
            Allergies = allergies,
            Preference = preference,
        };

        foreach (var food in classified)
        {
            if (excludedIds.Contains(food.Id))
            {
                selection.ExcludedByUserCount++;
                continue;
            }

            if (allergies != FoodAllergen.None && (food.Allergens is null || (food.Allergens.Value & allergies) != 0))
            {
                // Alerjen bilgisi dogrulanmamis besin, alerjisi olan kullaniciya guvenli kabul edilmez.
                selection.ExcludedByAllergyCount++;
                continue;
            }

            if (!MatchesDiet(food.DietTags, preference))
            {
                selection.ExcludedByDietCount++;
                continue;
            }

            var ratios = BuildPortionRatios(food, out var isContinuous);
            if (ratios.Count == 0 || !HasConsistentEnergy(food))
            {
                selection.InvalidDataCount++;
                continue;
            }

            selection.Foods.Add(new FoodCandidate(food, ratios, isContinuous));
        }

        return selection;
    }

    private static bool MatchesDiet(DietTag? tags, DietaryPreference preference) => preference switch
    {
        DietaryPreference.Vegan => tags.HasValue && tags.Value.HasFlag(DietTag.Vegan),
        // Vegan besin vejetaryen diyete de uygundur.
        DietaryPreference.Vegetarian => tags.HasValue && (tags.Value & (DietTag.Vegetarian | DietTag.Vegan)) != 0,
        _ => true,
    };

    /// <summary>Besinin bildirdigi kalori ile makrolardan (4/4/9) hesaplanan enerji makul olcude uyusuyor mu.</summary>
    private bool HasConsistentEnergy(Food food)
    {
        if (food.Calories <= 0 || food.Protein < 0 || food.Carbs < 0 || food.Fat < 0)
        {
            return false;
        }

        var macroEnergy = MacroEnergy(food);
        var allowed = Math.Max(_options.EnergyConsistencyMinimumKcal, food.Calories * _options.EnergyConsistencyTolerance);
        return Math.Abs(macroEnergy - food.Calories) <= allowed;
    }

    /// <summary>
    /// Bir besin icin gecerli porsiyon katsayilarini (miktar / referans porsiyon) uretir. Gram/ml gibi
    /// bolunebilir birimlerde miktar ContinuousQuantityRounding'e yuvarlanir; adet/dilim gibi sayilabilir
    /// birimlerde miktar DiscreteQuantityStep katlarindadir. Sifir/negatif miktar uretilmez.
    /// </summary>
    private List<decimal> BuildPortionRatios(Food food, out bool isContinuous)
    {
        var unit = food.ServingUnit?.Trim() ?? string.Empty;
        isContinuous = _options.ContinuousServingUnits.Any(u => string.Equals(u, unit, StringComparison.OrdinalIgnoreCase));

        var ratios = new SortedSet<decimal>();
        if (food.ServingSize <= 0 || string.IsNullOrEmpty(unit))
        {
            return ratios.ToList();
        }

        var minQuantity = food.ServingSize * _options.MinPortionMultiplier;
        var maxQuantity = food.ServingSize * _options.MaxPortionMultiplier;

        if (isContinuous)
        {
            // Referans porsiyon cok kucukse (orn. 10 g yag) 5 g'lik yuvarlama anlamsizlasir; 1 birim kullanilir.
            var rounding = minQuantity >= _options.ContinuousQuantityRounding ? _options.ContinuousQuantityRounding : 1m;
            for (var multiplier = _options.MinPortionMultiplier; multiplier <= _options.MaxPortionMultiplier; multiplier += _options.PortionMultiplierStep)
            {
                var quantity = Math.Round(food.ServingSize * multiplier / rounding, MidpointRounding.AwayFromZero) * rounding;
                if (quantity > 0 && quantity >= minQuantity && quantity <= maxQuantity)
                {
                    ratios.Add(quantity / food.ServingSize);
                }
            }
        }
        else
        {
            var step = _options.DiscreteQuantityStep;
            var firstStep = Math.Ceiling(minQuantity / step);
            for (var quantity = firstStep * step; quantity <= maxQuantity; quantity += step)
            {
                if (quantity > 0)
                {
                    ratios.Add(quantity / food.ServingSize);
                }
            }
        }

        return ratios.ToList();
    }

    /// <summary>
    /// Her ogunun zorunlu slotlari icin en az bir aday besin oldugunu dogrular. Olmayan varsa, kisitlarin
    /// mi yoksa siniflandirilmamis katalogun mu sebep oldugunu soyleyen acik bir hata firlatir.
    /// </summary>
    private static void EnsureCatalogCoversAllMeals(CatalogSelection catalog)
    {
        var missing = new List<string>();
        foreach (var definition in MealDefinitions)
        {
            foreach (var slot in definition.Slots.Where(s => s.Required))
            {
                if (!catalog.Foods.Any(f => FitsSlot(f, definition, slot)))
                {
                    missing.Add($"{definition.Name.ToLower(Turkish)} için {slot.Label}");
                }
            }
        }

        if (missing.Count == 0)
        {
            return;
        }

        var reasons = new List<string>();
        if (catalog.UnclassifiedCount > 0)
        {
            reasons.Add($"{catalog.UnclassifiedCount} besin henüz kategori/öğün bilgisiyle sınıflandırılmamış");
        }

        if (catalog.ExcludedByAllergyCount > 0)
        {
            reasons.Add($"{catalog.ExcludedByAllergyCount} besin alerjileriniz nedeniyle (veya alerjen bilgisi doğrulanmadığı için) elendi");
        }

        if (catalog.ExcludedByDietCount > 0)
        {
            reasons.Add($"{catalog.ExcludedByDietCount} besin beslenme tercihiniz nedeniyle elendi");
        }

        if (catalog.ExcludedByUserCount > 0)
        {
            reasons.Add($"{catalog.ExcludedByUserCount} besini hariç tuttunuz");
        }

        if (catalog.InvalidDataCount > 0)
        {
            reasons.Add($"{catalog.InvalidDataCount} besinin porsiyon/besin değeri verisi tutarsız");
        }

        var restrictionsApplied = catalog.ExcludedByAllergyCount + catalog.ExcludedByDietCount + catalog.ExcludedByUserCount > 0;
        var action = restrictionsApplied
            ? "Alerji, beslenme tercihi veya hariç tutulan besin ayarlarınızı gözden geçirebilir ya da destek ekibine katalogda uygun besin eklenmesini bildirebilirsiniz."
            : "Besin kataloğunun yönetim panelinden sınıflandırılması gerekiyor; lütfen destek ekibine bildirin.";

        throw new PersonalizationGenerationException(
            $"Beslenme programı oluşturulamadı: uygun besin bulunamadı ({string.Join("; ", missing)}). "
            + $"Katalogdaki {catalog.TotalCount} besinden "
            + (reasons.Count > 0 ? string.Join(", ", reasons) : "hiçbiri bu öğünlere uygun değil")
            + ". " + action);
    }

    private static bool FitsSlot(FoodCandidate candidate, MealDefinition definition, MealSlot slot) =>
        candidate.Food.MealSuitability.HasValue
        && candidate.Food.MealSuitability.Value.HasFlag(definition.Suitability)
        && candidate.Food.Category.HasValue
        && slot.Categories.Contains(candidate.Food.Category.Value);

    // ---------------------------------------------------------------------------------------------
    // Secim ve cesitlilik
    // ---------------------------------------------------------------------------------------------

    private FoodScoringWeights ResolveScoringWeights(FitnessGoal? goal)
    {
        if (goal.HasValue && _options.GoalScoringWeights.TryGetValue(goal.Value, out var weights))
        {
            return weights;
        }

        return _options.GoalScoringWeights.TryGetValue(FitnessGoals.Default, out var fallback)
            ? fallback
            : new FoodScoringWeights();
    }

    /// <summary>Besinin hedefe uygunluk puani (0-1'e normalize ozellikler x hedef agirliklari).</summary>
    private decimal ScoreForGoal(FoodCandidate candidate, FoodScoringWeights weights)
    {
        var food = candidate.Food;
        var macroEnergy = MacroEnergy(food);
        if (macroEnergy <= 0)
        {
            return 0m;
        }

        var proteinShare = food.Protein * 4m / macroEnergy;
        candidate.ProteinShare = proteinShare;
        var carbsShare = food.Carbs * 4m / macroEnergy;
        var fatShare = food.Fat * 9m / macroEnergy;
        var fiber = food.Fiber.HasValue
            ? Math.Min(1m, food.Fiber.Value * 100m / food.Calories / _options.FiberPer100KcalForFullScore)
            : 0m;
        var sugarShare = food.Sugar.HasValue ? Math.Min(1m, food.Sugar.Value * 4m / food.Calories) : 0m;
        var energyDensity = candidate.IsContinuous
            ? Math.Min(1m, food.Calories / food.ServingSize / _options.EnergyDensityKcalPerGramForFullScore)
            : 0m;

        return (weights.ProteinShare * proteinShare)
            + (weights.CarbsShare * carbsShare)
            + (weights.FatShare * fatShare)
            + (weights.Fiber * fiber)
            + (weights.SugarShare * sugarShare)
            + (weights.EnergyDensity * energyDensity);
    }

    /// <summary>
    /// Ogunun slotlarini sirayla doldurur. Ayni gun daha once kullanilan besin, baska aday varsa secilmez.
    /// Ikinci deger, ayni gun tekrarindan kacinilamadiysa true'dur.
    /// </summary>
    private (List<SelectedFood> Selection, bool RepeatedToday) SelectFoods(CatalogSelection catalog, SelectionContext context)
    {
        var selection = new List<SelectedFood>();
        var repeatedToday = false;

        foreach (var slot in context.Definition.Slots)
        {
            if (selection.Count >= _options.MaxItemsPerMeal)
            {
                break;
            }

            var candidates = catalog.Foods
                .Where(f => FitsSlot(f, context.Definition, slot) && selection.All(s => s.Candidate.Food.Id != f.Food.Id))
                .ToList();
            if (candidates.Count == 0)
            {
                continue;
            }

            var fresh = candidates.Where(f => !context.UsedToday.Contains(f.Food.Id)).ToList();
            if (fresh.Count == 0)
            {
                if (!slot.Required)
                {
                    continue;
                }

                repeatedToday = true;
                fresh = candidates;
            }

            // Ayni ogunde ayni kategoriden ikinci besin (orn. mercimek + fasulye) yalnizca baska secenek yoksa.
            var usedCategories = selection.Select(s => s.Candidate.Food.Category).ToHashSet();
            var differentCategory = fresh.Where(f => !usedCategories.Contains(f.Food.Category)).ToList();
            if (differentCategory.Count > 0)
            {
                fresh = differentCategory;
            }

            var best = fresh
                .Select(f => (Candidate: f, Score: SelectionScore(f, slot, context)))
                .OrderByDescending(x => x.Score)
                .ThenBy(x => x.Candidate.Food.Id)
                .First();

            selection.Add(new SelectedFood(best.Candidate, slot.Droppable));
        }

        return (selection, repeatedToday);
    }

    private decimal SelectionScore(FoodCandidate candidate, MealSlot slot, SelectionContext context)
    {
        var id = candidate.Food.Id;
        var score = candidate.GoalScore;
        if (slot.ProteinFocus)
        {
            score += _options.ProteinSlotFocusWeight * candidate.ProteinShare;
        }

        if (context.PreviousDayByMeal.TryGetValue(context.Definition.Type, out var sameMealYesterday) && sameMealYesterday.Contains(id))
        {
            score -= _options.PreviousDaySameMealPenalty;
        }

        if (context.PreviousDayAll.Contains(id))
        {
            score -= _options.PreviousDayAnyMealPenalty;
        }

        score -= _options.WeeklyUsagePenalty * context.WeeklyUsage.GetValueOrDefault(id);
        score += _options.VarietyJitterWeight * DeterministicJitter(context.UserId, context.DayIndex, (int)context.Definition.Type, id);

        return score;
    }

    /// <summary>
    /// Kullanici, gun, ogun ve besin kimliginden [0, 1) araliginda tekrarlanabilir bir deger uretir
    /// (SplitMix64). string.GetHashCode surecten surece degistigi icin kullanilmaz.
    /// </summary>
    private static decimal DeterministicJitter(long userId, int dayIndex, int mealType, long foodId)
    {
        unchecked
        {
            var x = (ulong)userId * 0x9E3779B97F4A7C15UL;
            x ^= (ulong)dayIndex * 0xBF58476D1CE4E5B9UL;
            x ^= (ulong)mealType * 0x94D049BB133111EBUL;
            x ^= (ulong)foodId * 0xD6E8FEB86659FD93UL;
            x = (x ^ (x >> 30)) * 0xBF58476D1CE4E5B9UL;
            x = (x ^ (x >> 27)) * 0x94D049BB133111EBUL;
            x ^= x >> 31;
            return (decimal)(x >> 11) / (1UL << 53);
        }
    }

    // ---------------------------------------------------------------------------------------------
    // Porsiyon cozumu
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Ogundeki besinlerin porsiyonlarini, ogun hedefine (kalori/protein/karbonhidrat/yag) agirlikli en
    /// yakin kombinasyon olacak sekilde secer. Cikarilabilir slottaki besin (yag/kuruyemis) hedefe katki
    /// saglamiyorsa cikarilir; digerleri her zaman en az minimum porsiyonla kalir.
    /// </summary>
    private List<Portion> SolveMealPortions(List<SelectedFood> selection, MacroTotals target)
    {
        var options = selection
            .Select(s => s.Droppable ? new[] { 0m }.Concat(s.Candidate.Ratios).ToList() : s.Candidate.Ratios)
            .ToList();

        var bestRatios = new decimal[selection.Count];
        var current = new decimal[selection.Count];
        var bestError = decimal.MaxValue;

        void Search(int index, MacroTotals sum)
        {
            if (index == selection.Count)
            {
                var error = WeightedError(sum, target);
                if (error < bestError)
                {
                    bestError = error;
                    Array.Copy(current, bestRatios, current.Length);
                }

                return;
            }

            foreach (var ratio in options[index])
            {
                current[index] = ratio;
                Search(index + 1, sum + Nutrients(selection[index].Candidate.Food, ratio));
            }
        }

        Search(0, MacroTotals.Zero);

        return selection
            .Select((s, i) => new Portion(s.Candidate, bestRatios[i]))
            .Where(p => p.Ratio > 0)
            .ToList();
    }

    /// <summary>
    /// Gun toplamini hedefe yaklastirmak icin ac gozlu iyilestirme: her turda, gun hatasini en cok
    /// azaltan tek porsiyon degisikligi uygulanir. Ogun kalorisi kendi hedefinden MealCalorieFlexPercentage
    /// kadar uzaklasamaz (ogun dagilimi korunur).
    /// </summary>
    private void RefineDayPortions(List<PlannedMeal> meals, MacroTotals dailyTarget)
    {
        for (var pass = 0; pass < _options.MaxDayRefinementPasses; pass++)
        {
            var dayTotals = meals.Aggregate(MacroTotals.Zero, (sum, meal) => sum + meal.Totals());
            var currentError = WeightedError(dayTotals, dailyTarget);

            (PlannedMeal Meal, int Index, decimal Ratio, decimal Error)? best = null;
            foreach (var meal in meals)
            {
                var mealTotals = meal.Totals();
                for (var i = 0; i < meal.Portions.Count; i++)
                {
                    var portion = meal.Portions[i];
                    var withoutItem = mealTotals - Nutrients(portion.Food.Food, portion.Ratio);
                    foreach (var ratio in portion.Food.Ratios)
                    {
                        if (ratio == portion.Ratio)
                        {
                            continue;
                        }

                        var newMeal = withoutItem + Nutrients(portion.Food.Food, ratio);
                        var flex = Math.Abs(newMeal.Calories - meal.Target.Calories) / meal.Target.Calories;
                        if (flex > _options.MealCalorieFlexPercentage)
                        {
                            continue;
                        }

                        var newDay = dayTotals - mealTotals + newMeal;
                        var error = WeightedError(newDay, dailyTarget);
                        if (error < currentError && (best is null || error < best.Value.Error))
                        {
                            best = (meal, i, ratio, error);
                        }
                    }
                }
            }

            if (best is null)
            {
                return;
            }

            var (bestMeal, index, bestRatio, _) = best.Value;
            bestMeal.Portions[index] = bestMeal.Portions[index] with { Ratio = bestRatio };
        }
    }

    private decimal WeightedError(MacroTotals actual, MacroTotals target)
    {
        return (_options.CalorieErrorWeight * SquaredRelative(actual.Calories, target.Calories))
            + (_options.ProteinErrorWeight * SquaredRelative(actual.Protein, target.Protein))
            + (_options.CarbsErrorWeight * SquaredRelative(actual.Carbs, target.Carbs))
            + (_options.FatErrorWeight * SquaredRelative(actual.Fat, target.Fat));
    }

    private static decimal SquaredRelative(decimal actual, decimal target)
    {
        if (target <= 0)
        {
            return 0m;
        }

        var relative = (actual - target) / target;
        return relative * relative;
    }

    /// <summary>Besinin verilen porsiyon katsayisindaki (miktar / referans porsiyon) besin degerleri.</summary>
    private static MacroTotals Nutrients(Food food, decimal ratio) =>
        new(food.Calories * ratio, food.Protein * ratio, food.Carbs * ratio, food.Fat * ratio);

    private static decimal MacroEnergy(Food food) => (food.Protein * 4m) + (food.Carbs * 4m) + (food.Fat * 9m);

    /// <summary>Son porsiyondan besin degeri snapshot'i olusturur (Food sonradan degisse de plan sabit kalir).</summary>
    private static NutritionPlanMealItem BuildItem(Portion portion)
    {
        var food = portion.Food.Food;
        // 150.00 yerine 150 gibi gorunmesi icin olcek normalize edilir (deger degismez).
        var quantity = Math.Round(food.ServingSize * portion.Ratio, 2) / 1.0000000000000000000000000000m;
        var ratio = quantity / food.ServingSize;

        return new NutritionPlanMealItem
        {
            FoodId = food.Id,
            Quantity = quantity,
            ServingUnit = food.ServingUnit.Trim(),
            Calories = Math.Round(food.Calories * ratio, 0),
            Protein = Math.Round(food.Protein * ratio, 1),
            Carbs = Math.Round(food.Carbs * ratio, 1),
            Fat = Math.Round(food.Fat * ratio, 1),
            CreatedAt = DateTime.UtcNow,
        };
    }

    // ---------------------------------------------------------------------------------------------
    // Dogrulama ve metinler
    // ---------------------------------------------------------------------------------------------

    private decimal MealPercentage(MealType type) => type switch
    {
        MealType.Breakfast => _options.BreakfastCaloriePercentage,
        MealType.Lunch => _options.LunchCaloriePercentage,
        MealType.Dinner => _options.DinnerCaloriePercentage,
        MealType.Snack => _options.SnackCaloriePercentage,
        _ => 0m,
    };

    /// <summary>Gun toplaminin tolerans disinda kalan besin ogelerini insan okunur bicimde listeler.</summary>
    private List<string> DescribeDeviations(MacroTotals actual, MacroTotals target)
    {
        var deviations = new List<string>();
        AddDeviation(deviations, "kalori", actual.Calories, target.Calories, _options.CalorieTolerancePercentage);
        AddDeviation(deviations, "protein", actual.Protein, target.Protein, _options.ProteinTolerancePercentage);
        AddDeviation(deviations, "karbonhidrat", actual.Carbs, target.Carbs, _options.CarbsTolerancePercentage);
        AddDeviation(deviations, "yağ", actual.Fat, target.Fat, _options.FatTolerancePercentage);
        return deviations;
    }

    private static void AddDeviation(List<string> deviations, string label, decimal actual, decimal target, decimal tolerance)
    {
        if (target <= 0)
        {
            return;
        }

        var relative = (actual - target) / target;
        if (Math.Abs(relative) <= tolerance)
        {
            return;
        }

        var direction = relative > 0 ? "üstünde" : "altında";
        deviations.Add($"{label} hedefin %{Math.Abs(relative) * 100m:0} {direction}");
    }

    private static string BuildMealNote(MacroTotals target) =>
        $"Öğün hedefi: yaklaşık {target.Calories:0} kcal, {target.Protein:0} g protein, {target.Carbs:0} g karbonhidrat, {target.Fat:0} g yağ.";

    private static string BuildSummary(
        UserProfile profile,
        NutritionCalculationResult targets,
        CatalogSelection catalog,
        int daysWithinTolerance,
        List<string> warnings)
    {
        var parts = new List<string>
        {
            $"Günlük {targets.DailyCalories:0} kcal; {targets.ProteinTarget:0} g protein, {targets.CarbsTarget:0} g karbonhidrat, {targets.FatTarget:0} g yağ.",
            $"Hesap: bazal metabolizma {targets.Bmr:0} kcal × aktivite {targets.ActivityMultiplier:0.###}"
                + (targets.GoalCalorieAdjustment == 0 ? "." : $", hedef düzeltmesi {targets.GoalCalorieAdjustment:+0;-0} kcal."),
            GoalFocus(profile.Goal),
        };

        parts.AddRange(targets.Notes);

        var restrictions = new List<string>();
        if (catalog.Preference == DietaryPreference.Vegetarian)
        {
            restrictions.Add("vejetaryen");
        }
        else if (catalog.Preference == DietaryPreference.Vegan)
        {
            restrictions.Add("vegan");
        }

        if (catalog.Allergies != FoodAllergen.None)
        {
            restrictions.Add("alerjileriniz");
        }

        if (catalog.ExcludedByUserCount > 0)
        {
            restrictions.Add($"hariç tuttuğunuz {catalog.ExcludedByUserCount} besin");
        }

        if (restrictions.Count > 0)
        {
            parts.Add($"Dikkate alınan tercihler: {string.Join(", ", restrictions)}.");
        }

        parts.Add(warnings.Count == 0
            ? "Tüm günler hedef toleransları içinde."
            : $"7 günün {daysWithinTolerance} tanesi hedef toleransları içinde; kalan sapmalar: {string.Join(" ", warnings)}");

        return string.Join(" ", parts);
    }

    private static string GoalFocus(FitnessGoal? goal) => goal is null ? string.Empty : FitnessGoals.Normalize(goal.Value) switch
    {
        FitnessGoal.LoseWeight => "Yağ kaybı hedefin için hipertrofi antrenmanını destekleyecek kadar proteinli, lifi yüksek ve enerji yoğunluğu düşük besinler önceliklendirildi.",
        FitnessGoal.GetStronger => "Güçlenme hedefin için kuvvet antrenmanını ve toparlanmayı destekleyen protein ağırlıklı, yeterli karbonhidratlı besinler önceliklendirildi.",
        _ => "Kas geliştirme hedefin için protein ve karbonhidratı yüksek besinler önceliklendirildi.",
    };

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..(maxLength - 1)] + "…";

    // ---------------------------------------------------------------------------------------------
    // Ic modeller
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Ogun slotu. Required: katalogda bu slot icin aday yoksa plan uretilemez. Droppable: porsiyon cozumu,
    /// besin hedefe katki saglamiyorsa onu ogunden cikarabilir (orn. yag/kuruyemis); digerleri en az
    /// minimum porsiyonla kalir. ProteinFocus: slotun amaci protein saglamaktir; adaylarin protein enerji payi
    /// hedeften bagimsiz olarak ek puan alir (lifli baklagil, kilo verme hedefinde bile tavugun onune gecmesin).
    /// </summary>
    private sealed record MealSlot(string Label, bool Required, bool Droppable, bool ProteinFocus, params FoodCategory[] Categories);

    private sealed record MealDefinition(MealType Type, string Name, TimeOnly PlannedTime, MealSuitability Suitability, MealSlot[] Slots);

    private sealed class FoodCandidate
    {
        public FoodCandidate(Food food, List<decimal> ratios, bool isContinuous)
        {
            Food = food;
            Ratios = ratios;
            IsContinuous = isContinuous;
        }

        public Food Food { get; }

        /// <summary>Gecerli porsiyon katsayilari (miktar / referans porsiyon), artan sirali.</summary>
        public List<decimal> Ratios { get; }

        public bool IsContinuous { get; }

        public decimal GoalScore { get; set; }

        /// <summary>Enerjinin proteinden gelen payi (4P / makro enerjisi).</summary>
        public decimal ProteinShare { get; set; }
    }

    private sealed class CatalogSelection
    {
        public List<FoodCandidate> Foods { get; } = new();
        public int TotalCount { get; init; }
        public int UnclassifiedCount { get; init; }
        public int ExcludedByAllergyCount { get; set; }
        public int ExcludedByDietCount { get; set; }
        public int ExcludedByUserCount { get; set; }
        public int InvalidDataCount { get; set; }
        public FoodAllergen Allergies { get; init; }
        public DietaryPreference Preference { get; init; }
    }

    private sealed record SelectionContext(
        long UserId,
        int DayIndex,
        MealDefinition Definition,
        HashSet<long> UsedToday,
        Dictionary<MealType, HashSet<long>> PreviousDayByMeal,
        HashSet<long> PreviousDayAll,
        Dictionary<long, int> WeeklyUsage);

    private sealed record SelectedFood(FoodCandidate Candidate, bool Droppable);

    private sealed record Portion(FoodCandidate Food, decimal Ratio);

    private sealed record PlannedMeal(MealDefinition Definition, MacroTotals Target, List<Portion> Portions)
    {
        public MacroTotals Totals() => Portions.Aggregate(MacroTotals.Zero, (sum, p) => sum + Nutrients(p.Food.Food, p.Ratio));
    }

    private readonly record struct MacroTotals(decimal Calories, decimal Protein, decimal Carbs, decimal Fat)
    {
        public static MacroTotals Zero => new(0m, 0m, 0m, 0m);

        public MacroTotals Scale(decimal factor) => new(Calories * factor, Protein * factor, Carbs * factor, Fat * factor);

        public static MacroTotals operator +(MacroTotals a, MacroTotals b) =>
            new(a.Calories + b.Calories, a.Protein + b.Protein, a.Carbs + b.Carbs, a.Fat + b.Fat);

        public static MacroTotals operator -(MacroTotals a, MacroTotals b) =>
            new(a.Calories - b.Calories, a.Protein - b.Protein, a.Carbs - b.Carbs, a.Fat - b.Fat);
    }
}
