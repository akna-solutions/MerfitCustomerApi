using MerfitCustomerApi.Business.Common;
using MerfitCustomerApi.Business.Services.Nutrition;
using MerfitCustomerApi.Business.Services.Personalization.Generators;
using MerfitCustomerApi.Business.Services.Personalization.Models;
using MerfitCustomerApi.Business.Tests.TestHelpers;
using MerfitCustomerApi.Domain.Entities;
using MerfitCustomerApi.Domain.Entities.Enums;
using MerfitCustomerApi.Domain.Exceptions;
using MerfitCustomerApi.Domain.Interfaces;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace MerfitCustomerApi.Business.Tests.Nutrition;

/// <summary>
/// Kural tabanli 7 gunluk beslenme plani: farkli profiller farkli menu alir, alerji/diyet/dislama
/// kisitlari asla ihlal edilmez, porsiyonlar birimle tutarlidir, gunluk toplamlar son porsiyonlardan
/// hesaplanir ve yetersiz katalog acik bir hatayla raporlanir.
/// </summary>
public class NutritionPlanGeneratorTests
{
    private static readonly NutritionPlanGenerationOptions GenerationOptions = new();

    private static UserProfile LosingWeightMan(long userId = 1) => new()
    {
        UserId = userId,
        Gender = Gender.Male,
        DateOfBirth = DateTime.UtcNow.Date.AddYears(-35),
        HeightCm = 180,
        WeightKg = 92,
        TargetWeightKg = 80,
        Goal = FitnessGoal.LoseWeight,
        ActivityLevel = ActivityLevel.LightlyActive,
        TrainingDaysPerWeek = 3,
    };

    private static UserProfile MuscleBuildingWoman(long userId = 2) => new()
    {
        UserId = userId,
        Gender = Gender.Female,
        DateOfBirth = DateTime.UtcNow.Date.AddYears(-24),
        HeightCm = 165,
        WeightKg = 56,
        TargetWeightKg = 60,
        Goal = FitnessGoal.BuildMuscle,
        ActivityLevel = ActivityLevel.ModeratelyActive,
        TrainingDaysPerWeek = 4,
    };

    private static NutritionPlanGenerator CreateGenerator(UserProfile profile, List<Food>? foods = null)
    {
        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(u => u.Repository<UserProfile>()).Returns(MockRepositoryFactory.Create(new List<UserProfile> { profile }).Object);
        unitOfWork.Setup(u => u.Repository<BodyMeasurement>()).Returns(MockRepositoryFactory.Create(new List<BodyMeasurement>()).Object);
        unitOfWork.Setup(u => u.Repository<Food>()).Returns(MockRepositoryFactory.Create(foods ?? NutritionTestCatalog.Create()).Object);

        return new NutritionPlanGenerator(
            unitOfWork.Object,
            new NutritionCalculator(Options.Create(new NutritionCalculationOptions())),
            Options.Create(GenerationOptions),
            NullLogger<NutritionPlanGenerator>.Instance);
    }

    private static Task<NutritionPlanGenerationResult> GenerateAsync(UserProfile profile, List<Food>? foods = null) =>
        CreateGenerator(profile, foods).GenerateAsync(profile.UserId);

    private static IEnumerable<NutritionPlanMealItem> AllItems(NutritionPlanGenerationResult result) =>
        result.Days.SelectMany(d => d.Meals).SelectMany(m => m.Items);

    [Fact]
    public async Task Plan_HasSevenDistinctDays_WithConsistentMealTypes()
    {
        var result = await GenerateAsync(LosingWeightMan());

        Assert.Equal(7, result.Days.Count);
        Assert.Equal(7, result.Days.Select(d => d.Day.DayOfWeek).Distinct().Count());
        foreach (var day in result.Days)
        {
            Assert.Equal(
                new[] { MealType.Breakfast, MealType.Lunch, MealType.Dinner, MealType.Snack },
                day.Meals.Select(m => m.Meal.MealType).ToArray());
            Assert.All(day.Meals, m => Assert.NotEmpty(m.Items));
        }

        Assert.True(result.Plan.IsActive);
        Assert.False(result.Plan.IsAiGenerated);
        Assert.Equal(1, result.Plan.UserId);
    }

    [Fact]
    public async Task Plan_DoesNotRepeatFoodWithinADay_AndVariesAcrossConsecutiveDays()
    {
        var result = await GenerateAsync(LosingWeightMan());

        foreach (var day in result.Days)
        {
            var ids = day.Meals.SelectMany(m => m.Items).Select(i => i.FoodId).ToList();
            Assert.Equal(ids.Count, ids.Distinct().Count());
        }

        for (var i = 1; i < result.Days.Count; i++)
        {
            foreach (var mealType in new[] { MealType.Breakfast, MealType.Lunch, MealType.Dinner })
            {
                var today = result.Days[i].Meals.Single(m => m.Meal.MealType == mealType).Items.Select(x => x.FoodId).ToHashSet();
                var yesterday = result.Days[i - 1].Meals.Single(m => m.Meal.MealType == mealType).Items.Select(x => x.FoodId).ToHashSet();
                Assert.False(today.SetEquals(yesterday), $"{result.Days[i].Day.DayOfWeek} {mealType} bir onceki gunle ayni.");
            }
        }

        // Hafta boyunca ana protein kaynaklari donusumlu kullanilir.
        var lunchProteins = result.Days.Select(d => d.Meals.Single(m => m.Meal.MealType == MealType.Lunch).Items[0].FoodId).Distinct().Count();
        Assert.True(lunchProteins >= 3, $"Ogle yemeginde yalnizca {lunchProteins} farkli protein kaynagi kullanildi.");
    }

    [Fact]
    public async Task DifferentProfiles_GetMeaningfullyDifferentMenus_NotJustDifferentCalories()
    {
        var lose = await GenerateAsync(LosingWeightMan());
        var muscle = await GenerateAsync(MuscleBuildingWoman());

        Assert.NotEqual(lose.Targets!.DailyCalories, muscle.Targets!.DailyCalories);

        // Ayni gun/ogun/besin uclusunun cogu farkli olmali.
        var loseSlots = lose.Days.SelectMany(d => d.Meals.SelectMany(m => m.Items.Select(i => (d.Day.DayOfWeek, m.Meal.MealType, i.FoodId)))).ToHashSet();
        var muscleSlots = muscle.Days.SelectMany(d => d.Meals.SelectMany(m => m.Items.Select(i => (d.Day.DayOfWeek, m.Meal.MealType, i.FoodId)))).ToHashSet();
        var overlap = (decimal)loseSlots.Intersect(muscleSlots).Count() / loseSlots.Count;
        Assert.True(overlap < 0.5m, $"Menulerin %{overlap * 100:0}'i ayni.");

        // Hedefe gore besin profili farkli: kilo verme menusunde kalorinin daha buyuk kismi proteinden gelir.
        static decimal ProteinEnergyShare(NutritionPlanGenerationResult r) =>
            AllItems(r).Sum(i => i.Protein * 4) / AllItems(r).Sum(i => i.Calories);
        Assert.True(ProteinEnergyShare(lose) > ProteinEnergyShare(muscle));
    }

    [Fact]
    public async Task VegetarianPreference_NeverIncludesNonVegetarianFoods()
    {
        var profile = LosingWeightMan();
        profile.DietaryPreference = DietaryPreference.Vegetarian;
        var catalog = NutritionTestCatalog.Create();

        var result = await GenerateAsync(profile, catalog);

        var allowed = catalog.Where(f => f.DietTags.HasValue && (f.DietTags.Value & (DietTag.Vegetarian | DietTag.Vegan)) != 0).Select(f => f.Id).ToHashSet();
        Assert.All(AllItems(result), item => Assert.Contains(item.FoodId, allowed));
        Assert.Contains("vejetaryen", result.Summary);
    }

    [Fact]
    public async Task VeganPreference_UsesOnlyVeganTaggedFoods()
    {
        var profile = MuscleBuildingWoman();
        profile.DietaryPreference = DietaryPreference.Vegan;
        var catalog = NutritionTestCatalog.Create();

        var result = await GenerateAsync(profile, catalog);

        var vegan = catalog.Where(f => f.DietTags.HasValue && f.DietTags.Value.HasFlag(DietTag.Vegan)).Select(f => f.Id).ToHashSet();
        Assert.All(AllItems(result), item => Assert.Contains(item.FoodId, vegan));
    }

    [Fact]
    public async Task Allergies_AreNeverIgnored_AndUnverifiedAllergenDataIsExcluded()
    {
        var profile = LosingWeightMan();
        profile.Allergies = FoodAllergen.Milk | FoodAllergen.Gluten;
        var catalog = NutritionTestCatalog.Create();
        // Alerjen bilgisi dogrulanmamis (null) bir protein kaynagi: alerjisi olan kullaniciya onerilmemeli.
        catalog.Add(new Food
        {
            Id = 200, Name = "Alerjeni bilinmeyen kofte", ServingSize = 100, ServingUnit = "g", Calories = 200, Protein = 20, Carbs = 5, Fat = 11,
            Category = FoodCategory.ProteinSource, MealSuitability = MealSuitability.Lunch | MealSuitability.Dinner, Allergens = null, DietTags = DietTag.None,
        });

        var result = await GenerateAsync(profile, catalog);

        var foodsById = catalog.ToDictionary(f => f.Id);
        Assert.All(AllItems(result), item =>
        {
            var food = foodsById[item.FoodId];
            Assert.NotNull(food.Allergens);
            Assert.Equal(FoodAllergen.None, food.Allergens!.Value & (FoodAllergen.Milk | FoodAllergen.Gluten));
        });
        Assert.DoesNotContain(AllItems(result), i => i.FoodId == 200);
    }

    [Fact]
    public async Task ExcludedFoods_AreNeverIncluded()
    {
        var baseline = await GenerateAsync(LosingWeightMan());
        var mostUsed = AllItems(baseline).GroupBy(i => i.FoodId).OrderByDescending(g => g.Count()).Take(3).Select(g => g.Key).ToList();

        var profile = LosingWeightMan();
        profile.ExcludedFoodIds = FoodPreferenceFormat.FormatExcludedFoodIds(mostUsed);
        var result = await GenerateAsync(profile);

        Assert.DoesNotContain(AllItems(result), i => mostUsed.Contains(i.FoodId));
    }

    [Fact]
    public async Task UnclassifiedAndInconsistentFoods_AreNeverUsed()
    {
        var result = await GenerateAsync(MuscleBuildingWoman());

        Assert.DoesNotContain(AllItems(result), i => i.FoodId is NutritionTestCatalog.UnclassifiedFoodId or 91 or 92 or NutritionTestCatalog.InconsistentEnergyFoodId);
    }

    [Fact]
    public async Task ItemNutrients_ComeFromFinalQuantity_AndDailyTotalsFromItems()
    {
        var catalog = NutritionTestCatalog.Create();
        var foodsById = catalog.ToDictionary(f => f.Id);
        var result = await GenerateAsync(LosingWeightMan(), catalog);

        foreach (var item in AllItems(result))
        {
            var food = foodsById[item.FoodId];
            var ratio = item.Quantity / food.ServingSize;
            Assert.Equal(Math.Round(food.Calories * ratio, 0), item.Calories);
            Assert.Equal(Math.Round(food.Protein * ratio, 1), item.Protein);
            Assert.Equal(Math.Round(food.Carbs * ratio, 1), item.Carbs);
            Assert.Equal(Math.Round(food.Fat * ratio, 1), item.Fat);
        }

        // Porsiyonlar besine gore farkli katsayilarla secilir (tek bir ogun carpani yoktur).
        var lunch = result.Days[0].Meals.Single(m => m.Meal.MealType == MealType.Lunch).Items;
        var ratios = lunch.Select(i => i.Quantity / foodsById[i.FoodId].ServingSize).Distinct().Count();
        Assert.True(ratios > 1);

        // Gunluk toplam, son porsiyonlarin toplamidir ve hedefe yakindir.
        var targets = result.Targets!;
        foreach (var day in result.Days)
        {
            var dayCalories = day.Meals.SelectMany(m => m.Items).Sum(i => i.Calories);
            Assert.InRange(dayCalories, targets.DailyCalories * 0.85m, targets.DailyCalories * 1.15m);
        }
    }

    [Fact]
    public async Task Portions_RespectServingUnit_AndArePositiveAndPlausible()
    {
        var catalog = NutritionTestCatalog.Create();
        var foodsById = catalog.ToDictionary(f => f.Id);
        var result = await GenerateAsync(MuscleBuildingWoman(), catalog);

        foreach (var item in AllItems(result))
        {
            var food = foodsById[item.FoodId];
            Assert.Equal(food.ServingUnit, item.ServingUnit);
            Assert.True(item.Quantity > 0);
            Assert.True(item.Calories > 0);

            var ratio = item.Quantity / food.ServingSize;
            Assert.InRange(ratio, GenerationOptions.MinPortionMultiplier, GenerationOptions.MaxPortionMultiplier);

            if (food.ServingUnit is "adet" or "dilim")
            {
                Assert.Equal(0m, item.Quantity % GenerationOptions.DiscreteQuantityStep);
            }
            else if (food.ServingSize * GenerationOptions.MinPortionMultiplier >= GenerationOptions.ContinuousQuantityRounding)
            {
                Assert.Equal(0m, item.Quantity % GenerationOptions.ContinuousQuantityRounding);
            }
        }
    }

    [Fact]
    public async Task DailyTotals_AreValidated_AndDeviationsReported()
    {
        // Tolerans imkansiz derecede dar: sapmalar basari gibi gosterilmemeli, raporlanmali.
        var strictOptions = new NutritionPlanGenerationOptions { CalorieTolerancePercentage = 0.0001m, ProteinTolerancePercentage = 0.0001m };
        var profile = LosingWeightMan();
        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(u => u.Repository<UserProfile>()).Returns(MockRepositoryFactory.Create(new List<UserProfile> { profile }).Object);
        unitOfWork.Setup(u => u.Repository<BodyMeasurement>()).Returns(MockRepositoryFactory.Create(new List<BodyMeasurement>()).Object);
        unitOfWork.Setup(u => u.Repository<Food>()).Returns(MockRepositoryFactory.Create(NutritionTestCatalog.Create()).Object);
        var generator = new NutritionPlanGenerator(
            unitOfWork.Object,
            new NutritionCalculator(Options.Create(new NutritionCalculationOptions())),
            Options.Create(strictOptions),
            NullLogger<NutritionPlanGenerator>.Instance);

        var result = await generator.GenerateAsync(profile.UserId);

        Assert.NotEmpty(result.Warnings);
        Assert.Contains("hedefin", result.Warnings[0]);
        Assert.Contains("toleransları içinde; kalan sapmalar", result.Summary);
    }

    [Fact]
    public async Task Generation_IsDeterministic()
    {
        var first = await GenerateAsync(LosingWeightMan());
        var second = await GenerateAsync(LosingWeightMan());

        Assert.Equal(
            AllItems(first).Select(i => (i.FoodId, i.Quantity)).ToList(),
            AllItems(second).Select(i => (i.FoodId, i.Quantity)).ToList());
        Assert.Equal(first.Summary, second.Summary);
    }

    [Fact]
    public async Task MissingProfileData_FailsExplicitly_WithoutDefaults()
    {
        var profile = LosingWeightMan();
        profile.WeightKg = null;
        profile.DateOfBirth = null;

        var ex = await Assert.ThrowsAsync<PersonalizationGenerationException>(() => GenerateAsync(profile));

        Assert.Contains("Kilo", ex.Message);
        Assert.Contains("Dogum tarihi", ex.Message);
    }

    [Fact]
    public async Task InsufficientCatalogForRestrictions_IsExplicitAndActionable()
    {
        var profile = LosingWeightMan();
        profile.DietaryPreference = DietaryPreference.Vegan;
        // Tofu disinda vegan kahvalti protein kaynagi yok; tofu da soya alerjisi nedeniyle elenir.
        profile.Allergies = FoodAllergen.Soy;

        var ex = await Assert.ThrowsAsync<PersonalizationGenerationException>(() => GenerateAsync(profile));

        Assert.Contains("kahvaltı için protein kaynağı", ex.Message);
        Assert.Contains("beslenme tercihiniz", ex.Message);
        Assert.Contains("alerjileriniz", ex.Message);
    }

    [Fact]
    public async Task UnclassifiedCatalog_IsExplicit()
    {
        var unclassified = NutritionTestCatalog.Create().Select(f =>
        {
            f.Category = null;
            f.MealSuitability = null;
            return f;
        }).ToList();

        var ex = await Assert.ThrowsAsync<PersonalizationGenerationException>(() => GenerateAsync(LosingWeightMan(), unclassified));

        Assert.Contains("sınıflandırılmamış", ex.Message);
        Assert.Contains("yönetim panelinden", ex.Message);
    }

    [Fact]
    public async Task Result_CarriesTargets_ForNutritionGoalSync()
    {
        var profile = LosingWeightMan();
        var expected = new NutritionCalculator(Options.Create(new NutritionCalculationOptions())).Evaluate(profile).Result!;

        var result = await GenerateAsync(profile);

        Assert.Equal(expected.DailyCalories, result.Targets!.DailyCalories);
        Assert.Equal(expected.ProteinTarget, result.Targets.ProteinTarget);
        Assert.Contains($"{expected.DailyCalories:0} kcal", result.Summary);
        Assert.True(result.Summary.Length <= 1000);
    }
}
