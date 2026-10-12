using MerfitCustomerApi.Business.Common;
using MerfitCustomerApi.Business.Interfaces.Services;
using MerfitCustomerApi.Business.Services.Nutrition;
using MerfitCustomerApi.Business.Services.Personalization;
using MerfitCustomerApi.Business.Services.Personalization.Generators;
using MerfitCustomerApi.Business.Services.Personalization.Models;
using MerfitCustomerApi.Business.Tests.TestHelpers;
using MerfitCustomerApi.Domain.Common;
using MerfitCustomerApi.Domain.Entities;
using MerfitCustomerApi.Domain.Entities.Enums;
using MerfitCustomerApi.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace MerfitCustomerApi.Business.Tests.Personalization;

/// <summary>
/// Beslenme plani yeniden uretimi: gecmis korunur ve tek aktif plan kalir, NutritionGoal planla ayni
/// hedeflere guncellenir, yalnizca-beslenme isleri antrenman programina dokunmaz ve ayni kullanicinin
/// isleri eszamanli islenmez.
/// </summary>
public class NutritionRegenerationProcessorTests
{
    private const long UserId = 7;

    private sealed class Store
    {
        public List<PersonalizationJob> Jobs { get; } = new();
        public List<UserProfile> Profiles { get; } = new();
        public List<Food> Foods { get; } = NutritionTestCatalog.Create();
        public List<NutritionGoal> Goals { get; } = new();
        public List<NutritionPlan> Plans { get; } = new();
        public List<NutritionPlanDay> Days { get; } = new();
        public List<NutritionPlanMeal> Meals { get; } = new();
        public List<NutritionPlanMealItem> Items { get; } = new();
        public List<WorkoutPlan> WorkoutPlans { get; } = new();
        public List<WorkoutPlanDay> WorkoutPlanDays { get; } = new();
        public List<WorkoutPlanExercise> WorkoutPlanExercises { get; } = new();
        public Mock<IUnitOfWork> UnitOfWork { get; } = new();

        public Store()
        {
            Setup(Jobs);
            Setup(Profiles);
            Setup(Foods);
            Setup(Goals);
            Setup(Plans);
            Setup(Days);
            Setup(Meals);
            Setup(Items);
            Setup(WorkoutPlans);
            Setup(WorkoutPlanDays);
            Setup(WorkoutPlanExercises);

            // Veritabani gibi: kaydedilen yeni satirlara kimlik atanir (ust-alt iliskileri dogrulamak icin).
            UnitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
                .Callback(() =>
                {
                    AssignIds(Plans);
                    AssignIds(Days);
                    AssignIds(Meals);
                    AssignIds(Items);
                    AssignIds(Goals);
                    AssignIds(WorkoutPlans);
                    AssignIds(WorkoutPlanDays);
                    AssignIds(Jobs);
                })
                .ReturnsAsync(1);
        }

        private void Setup<T>(List<T> items) where T : BaseEntity =>
            UnitOfWork.Setup(u => u.Repository<T>()).Returns(MockRepositoryFactory.Create(items).Object);

        private static void AssignIds<T>(List<T> items) where T : BaseEntity
        {
            foreach (var item in items.Where(i => i.Id == 0))
            {
                item.Id = items.Max(i => i.Id) + 1;
            }
        }
    }

    private static UserProfile Profile() => new()
    {
        Id = 1,
        UserId = UserId,
        Gender = Gender.Female,
        DateOfBirth = DateTime.UtcNow.Date.AddYears(-30),
        HeightCm = 168,
        WeightKg = 70,
        Goal = FitnessGoal.LoseWeight,
        ActivityLevel = ActivityLevel.LightlyActive,
        TrainingDaysPerWeek = 3,
    };

    private static PersonalizationJobProcessor CreateProcessor(Store store, Mock<IWorkoutPlanGenerator> workoutGenerator, INutritionPlanGenerator? nutritionGenerator = null) =>
        new(
            store.UnitOfWork.Object,
            workoutGenerator.Object,
            nutritionGenerator ?? new NutritionPlanGenerator(
                store.UnitOfWork.Object,
                new NutritionCalculator(Options.Create(new NutritionCalculationOptions())),
                Options.Create(new NutritionPlanGenerationOptions()),
                NullLogger<NutritionPlanGenerator>.Instance),
            Options.Create(new PersonalizationJobProcessingOptions()),
            NullLogger<PersonalizationJobProcessor>.Instance);

    private static Mock<IWorkoutPlanGenerator> WorkoutGenerator()
    {
        var generator = new Mock<IWorkoutPlanGenerator>();
        generator.Setup(g => g.GenerateAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((long userId, CancellationToken _) => new WorkoutPlanGenerationResult
            {
                Plan = new WorkoutPlan { UserId = userId, IsActive = true },
                Days = new List<WorkoutPlanDayResult>(),
                Summary = "test",
            });
        return generator;
    }

    [Fact]
    public async Task Regeneration_KeepsHistory_LeavesSingleActivePlan_AndSyncsNutritionGoal()
    {
        var store = new Store();
        store.Profiles.Add(Profile());
        store.Goals.Add(new NutritionGoal { Id = 1, UserId = UserId, DailyCalories = 2500, ProteinTarget = 100, CarbsTarget = 300, FatTarget = 80, WaterTargetMl = 2000 });
        store.Plans.Add(new NutritionPlan { Id = 40, UserId = UserId, IsActive = false, EndDate = DateTime.UtcNow.AddDays(-30) });
        store.Plans.Add(new NutritionPlan { Id = 50, UserId = UserId, IsActive = true });
        store.Plans.Add(new NutritionPlan { Id = 60, UserId = 99, IsActive = true });
        store.Jobs.Add(new PersonalizationJob { Id = 1, UserId = UserId, Status = PersonalizationJobStatus.Pending, NutritionOnly = true, CreatedAt = DateTime.UtcNow });
        var workoutGenerator = WorkoutGenerator();

        var processed = await CreateProcessor(store, workoutGenerator).ProcessNextAsync();

        Assert.True(processed);
        Assert.Equal(PersonalizationJobStatus.Completed, store.Jobs[0].Status);
        Assert.Null(store.Jobs[0].ErrorMessage);

        // Gecmis planlar silinmez; kullanicinin yalnizca yeni plani aktiftir; baska kullaniciya dokunulmaz.
        Assert.Equal(4, store.Plans.Count);
        var active = Assert.Single(store.Plans, p => p.UserId == UserId && p.IsActive);
        Assert.NotEqual(50, active.Id);
        Assert.NotNull(store.Plans.Single(p => p.Id == 50).EndDate);
        Assert.True(store.Plans.Single(p => p.Id == 60).IsActive);

        // Plan grafiginin ust-alt iliskileri dogru kurulur.
        var days = store.Days.Where(d => d.NutritionPlanId == active.Id).ToList();
        Assert.Equal(7, days.Count);
        Assert.All(store.Meals, m => Assert.Contains(m.NutritionPlanDayId, days.Select(d => d.Id)));
        Assert.Equal(28, store.Meals.Count);
        Assert.All(store.Items, i => Assert.Contains(i.NutritionPlanMealId, store.Meals.Select(m => m.Id)));

        // NutritionGoal, planin dayandigi profilden yeniden hesaplanan hedeflere esitlenir.
        var expected = new NutritionCalculator(Options.Create(new NutritionCalculationOptions())).Evaluate(store.Profiles[0]).Result!;
        var goal = Assert.Single(store.Goals);
        Assert.Equal(expected.DailyCalories, goal.DailyCalories);
        Assert.Equal(expected.ProteinTarget, goal.ProteinTarget);
        Assert.NotEqual(2500m, goal.DailyCalories);

        // Yalnizca-beslenme isi antrenman programina dokunmaz; ogun kayitlari (Meal) hic okunmaz/yazilmaz.
        workoutGenerator.Verify(g => g.GenerateAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.Empty(store.WorkoutPlans);
        store.UnitOfWork.Verify(u => u.Repository<Meal>(), Times.Never);
        store.UnitOfWork.Verify(u => u.CommitTransactionAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task FullJob_RegeneratesWorkoutToo()
    {
        var store = new Store();
        store.Profiles.Add(Profile());
        store.Jobs.Add(new PersonalizationJob { Id = 1, UserId = UserId, Status = PersonalizationJobStatus.Pending, CreatedAt = DateTime.UtcNow });
        var workoutGenerator = WorkoutGenerator();

        await CreateProcessor(store, workoutGenerator).ProcessNextAsync();

        workoutGenerator.Verify(g => g.GenerateAsync(UserId, It.IsAny<CancellationToken>()), Times.Once);
        Assert.Single(store.WorkoutPlans, p => p.IsActive);
        Assert.Single(store.Plans, p => p.IsActive);
        Assert.Single(store.Goals);
    }

    [Fact]
    public async Task IncompleteProfile_KeepsExistingPlanAndGoal_AndExplainsWhy()
    {
        var store = new Store();
        var profile = Profile();
        profile.HeightCm = null;
        store.Profiles.Add(profile);
        store.Goals.Add(new NutritionGoal { Id = 1, UserId = UserId, DailyCalories = 2500 });
        store.Plans.Add(new NutritionPlan { Id = 50, UserId = UserId, IsActive = true });
        store.Jobs.Add(new PersonalizationJob { Id = 1, UserId = UserId, Status = PersonalizationJobStatus.Pending, NutritionOnly = true, CreatedAt = DateTime.UtcNow });

        await CreateProcessor(store, WorkoutGenerator()).ProcessNextAsync();

        Assert.Equal(PersonalizationJobStatus.Completed, store.Jobs[0].Status);
        Assert.Contains("Boy", store.Jobs[0].ErrorMessage);
        Assert.True(Assert.Single(store.Plans).IsActive);
        Assert.Equal(2500m, Assert.Single(store.Goals).DailyCalories);
    }

    [Fact]
    public async Task JobOfUserWithAnotherProcessingJob_IsNotClaimed()
    {
        var store = new Store();
        store.Profiles.Add(Profile());
        store.Jobs.Add(new PersonalizationJob { Id = 1, UserId = UserId, Status = PersonalizationJobStatus.Processing, StartedAt = DateTime.UtcNow, AttemptCount = 1 });
        store.Jobs.Add(new PersonalizationJob { Id = 2, UserId = UserId, Status = PersonalizationJobStatus.Pending, CreatedAt = DateTime.UtcNow });
        var nutritionGenerator = new Mock<INutritionPlanGenerator>();

        var processed = await CreateProcessor(store, WorkoutGenerator(), nutritionGenerator.Object).ProcessNextAsync();

        Assert.False(processed);
        Assert.Equal(PersonalizationJobStatus.Pending, store.Jobs[1].Status);
        nutritionGenerator.Verify(g => g.GenerateAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task StaleProcessingJob_DoesNotBlockUserForever()
    {
        var store = new Store();
        store.Profiles.Add(Profile());
        store.Jobs.Add(new PersonalizationJob { Id = 1, UserId = UserId, Status = PersonalizationJobStatus.Processing, StartedAt = DateTime.UtcNow.AddHours(-2), AttemptCount = 1 });
        store.Jobs.Add(new PersonalizationJob { Id = 2, UserId = UserId, Status = PersonalizationJobStatus.Pending, NutritionOnly = true, CreatedAt = DateTime.UtcNow });

        var processed = await CreateProcessor(store, WorkoutGenerator()).ProcessNextAsync();

        Assert.True(processed);
        Assert.Equal(PersonalizationJobStatus.Completed, store.Jobs[1].Status);
    }

    [Fact]
    public async Task ConcurrentClaimOfSameUsersJobs_OnlyEarliestProceeds()
    {
        var store = new Store();
        store.Profiles.Add(Profile());
        var earlierJob = new PersonalizationJob { Id = 1, UserId = UserId, Status = PersonalizationJobStatus.Pending, CreatedAt = DateTime.UtcNow };
        var laterJob = new PersonalizationJob { Id = 2, UserId = UserId, Status = PersonalizationJobStatus.Pending, CreatedAt = DateTime.UtcNow.AddSeconds(-5) };
        store.Jobs.Add(earlierJob);
        store.Jobs.Add(laterJob);

        // Yaris: bu worker 2 numarali isi claim ederken baska bir worker ayni anda 1 numarali isi claim eder.
        store.UnitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Callback(() =>
            {
                if (laterJob.Status == PersonalizationJobStatus.Processing && earlierJob.Status == PersonalizationJobStatus.Pending)
                {
                    earlierJob.Status = PersonalizationJobStatus.Processing;
                    earlierJob.StartedAt = DateTime.UtcNow;
                    earlierJob.AttemptCount = 1;
                }
            })
            .ReturnsAsync(1);
        var nutritionGenerator = new Mock<INutritionPlanGenerator>();

        var processed = await CreateProcessor(store, WorkoutGenerator(), nutritionGenerator.Object).ProcessNextAsync();

        // 2 numarali is geri birakilir (deneme hakki iade edilir); 1 numarali isi diger worker isler.
        Assert.False(processed);
        Assert.Equal(PersonalizationJobStatus.Pending, laterJob.Status);
        Assert.Equal(0, laterJob.AttemptCount);
        Assert.Null(laterJob.StartedAt);
        nutritionGenerator.Verify(g => g.GenerateAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task UniqueActivePlanViolation_RollsBack_AndRetriesJob()
    {
        var store = new Store();
        store.Profiles.Add(Profile());
        store.Jobs.Add(new PersonalizationJob { Id = 1, UserId = UserId, Status = PersonalizationJobStatus.Pending, NutritionOnly = true, CreatedAt = DateTime.UtcNow });
        // Baska bir surec ayni anda aktif plan eklediginde veritabanindaki UX_NutritionPlan_UserId_Active index'i reddeder.
        store.UnitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Returns(() => store.Plans.Any(p => p.Id == 0 && p.IsActive)
                ? Task.FromException<int>(new DbUpdateException("duplicate key value violates unique constraint \"UX_NutritionPlan_UserId_Active\""))
                : Task.FromResult(1));
        // Rollback + ClearTracking: kaydedilmemis (eklenmeye calisilan) satirlar gecerliligini yitirir.
        store.UnitOfWork.Setup(u => u.ClearTracking()).Callback(() => store.Plans.RemoveAll(p => p.Id == 0));

        var processed = await CreateProcessor(store, WorkoutGenerator()).ProcessNextAsync();

        Assert.True(processed);
        store.UnitOfWork.Verify(u => u.RollbackTransactionAsync(It.IsAny<CancellationToken>()), Times.Once);
        store.UnitOfWork.Verify(u => u.CommitTransactionAsync(It.IsAny<CancellationToken>()), Times.Never);
        Assert.Equal(PersonalizationJobStatus.Pending, store.Jobs[0].Status);
        Assert.NotNull(store.Jobs[0].ErrorMessage);
    }

    [Fact]
    public async Task GeneratedPlan_IsServedThroughUnchangedNutritionPlanDto()
    {
        var store = new Store();
        store.Profiles.Add(Profile());
        store.Jobs.Add(new PersonalizationJob { Id = 1, UserId = UserId, Status = PersonalizationJobStatus.Pending, NutritionOnly = true, CreatedAt = DateTime.UtcNow });
        await CreateProcessor(store, WorkoutGenerator()).ProcessNextAsync();

        var dto = await new MyNutritionPlanService(store.UnitOfWork.Object).GetActivePlanAsync(UserId);

        Assert.NotNull(dto);
        Assert.Equal(7, dto!.Days.Count);
        Assert.All(dto.Days, day =>
        {
            Assert.Equal(4, day.Meals.Count);
            Assert.All(day.Meals, meal =>
            {
                Assert.NotEmpty(meal.Items);
                Assert.Equal(meal.Items.Sum(i => i.Calories), meal.TotalCalories);
                Assert.All(meal.Items, item =>
                {
                    Assert.False(string.IsNullOrEmpty(item.FoodName));
                    Assert.True(item.Quantity > 0);
                    Assert.False(string.IsNullOrEmpty(item.ServingUnit));
                });
            });
        });

        // Mobil uygulamanin okudugu JSON sozlesmesi degismedi.
        var json = System.Text.Json.JsonSerializer.Serialize(dto, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
        foreach (var key in new[] { "\"id\"", "\"name\"", "\"startDate\"", "\"isActive\"", "\"summary\"", "\"days\"", "\"dayOfWeek\"", "\"meals\"",
                     "\"mealType\"", "\"plannedTime\"", "\"notes\"", "\"items\"", "\"totalCalories\"", "\"totalProtein\"",
                     "\"foodId\"", "\"foodName\"", "\"quantity\"", "\"servingUnit\"", "\"calories\"", "\"protein\"", "\"carbs\"", "\"fat\"" })
        {
            Assert.Contains(key, json);
        }
    }
}
