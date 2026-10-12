using MerfitCustomerApi.Business.Common;
using MerfitCustomerApi.Business.Services.Personalization.Generators;
using MerfitCustomerApi.Business.Tests.TestHelpers;
using MerfitCustomerApi.Domain.Entities;
using MerfitCustomerApi.Domain.Entities.Enums;
using MerfitCustomerApi.Domain.Interfaces;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace MerfitCustomerApi.Business.Tests.Personalization;

/// <summary>
/// WorkoutPlanGenerator'in hedef uyum puani (FitScore) entegrasyonunu, uygunluk kurallarinin
/// (ekipman, Premium, deneyim/sure fallback'leri) puandan bagimsiz kaldigini ve mevcut haftalik
/// dagilim kurallarinin (tekrar onleme, kas grubu dengesi, deterministik secim) korundugunu dogrular.
/// </summary>
public class WorkoutPlanGeneratorScoringTests
{
    private const long UserId = 1;

    private sealed class Catalog
    {
        public List<UserProfile> Profiles { get; } = new();
        public List<UserGoal> Goals { get; } = new();
        public List<UserEquipment> UserEquipment { get; } = new();
        public List<Workout> Workouts { get; } = new();
        public List<WorkoutEquipment> WorkoutEquipment { get; } = new();
        public List<WorkoutExercise> WorkoutExercises { get; } = new();
        public List<WorkoutAttributeScore> AttributeScores { get; } = new();
        public List<Subscription> Subscriptions { get; } = new();

        public UserProfile Profile(
            FitnessGoal? goal = FitnessGoal.BuildMuscle,
            ExperienceLevel experience = ExperienceLevel.Beginner,
            int trainingDays = 3,
            int? durationMin = 45)
        {
            var profile = new UserProfile
            {
                Id = 1,
                UserId = UserId,
                Goal = goal,
                ExperienceLevel = experience,
                TrainingDaysPerWeek = trainingDays,
                WorkoutDurationMin = durationMin,
            };
            Profiles.Add(profile);
            return profile;
        }

        public Workout Workout(
            long id,
            DifficultyLevel difficulty = DifficultyLevel.Beginner,
            int durationMin = 30,
            long? muscleGroupId = null,
            bool isFeatured = false,
            bool isPremium = false,
            bool isActive = true)
        {
            var workout = new Workout
            {
                Id = id,
                Title = $"Workout {id}",
                Slug = $"workout-{id}",
                Difficulty = difficulty,
                DurationMin = durationMin,
                MuscleGroupId = muscleGroupId ?? id,
                IsFeatured = isFeatured,
                IsPremium = isPremium,
                IsActive = isActive,
            };
            Workouts.Add(workout);
            WorkoutExercises.Add(new WorkoutExercise { Id = id * 10, WorkoutId = id, ExerciseId = id * 100, Order = 1, Sets = 3 });
            return workout;
        }

        public void Scores(long workoutId, int hypertrophy, int strength, int cardio = 0, int endurance = 0, int mobility = 0, int coreStability = 0)
        {
            Score(workoutId, WorkoutAttributeType.Hypertrophy, hypertrophy);
            Score(workoutId, WorkoutAttributeType.Strength, strength);
            Score(workoutId, WorkoutAttributeType.Cardio, cardio);
            Score(workoutId, WorkoutAttributeType.Endurance, endurance);
            Score(workoutId, WorkoutAttributeType.Mobility, mobility);
            Score(workoutId, WorkoutAttributeType.CoreStability, coreStability);
        }

        public void Score(long workoutId, WorkoutAttributeType type, int score) =>
            AttributeScores.Add(new WorkoutAttributeScore { Id = AttributeScores.Count + 1, WorkoutId = workoutId, AttributeType = type, Score = score });

        public void RequireEquipment(long workoutId, long equipmentId) =>
            WorkoutEquipment.Add(new WorkoutEquipment { WorkoutId = workoutId, EquipmentId = equipmentId });

        public WorkoutPlanGenerator CreateGenerator(WorkoutPlanGenerationOptions? options = null)
        {
            var unitOfWork = new Mock<IUnitOfWork>();
            unitOfWork.Setup(u => u.Repository<UserProfile>()).Returns(MockRepositoryFactory.Create(Profiles).Object);
            unitOfWork.Setup(u => u.Repository<UserGoal>()).Returns(MockRepositoryFactory.Create(Goals).Object);
            unitOfWork.Setup(u => u.Repository<UserEquipment>()).Returns(MockRepositoryFactory.Create(UserEquipment).Object);
            unitOfWork.Setup(u => u.Repository<Workout>()).Returns(MockRepositoryFactory.Create(Workouts).Object);
            unitOfWork.Setup(u => u.Repository<WorkoutEquipment>()).Returns(MockRepositoryFactory.Create(WorkoutEquipment).Object);
            unitOfWork.Setup(u => u.Repository<WorkoutExercise>()).Returns(MockRepositoryFactory.Create(WorkoutExercises).Object);
            unitOfWork.Setup(u => u.Repository<WorkoutAttributeScore>()).Returns(MockRepositoryFactory.Create(AttributeScores).Object);
            unitOfWork.Setup(u => u.Repository<Subscription>()).Returns(MockRepositoryFactory.Create(Subscriptions).Object);

            return new WorkoutPlanGenerator(
                unitOfWork.Object,
                Options.Create(options ?? new WorkoutPlanGenerationOptions()),
                NullLogger<WorkoutPlanGenerator>.Instance);
        }

        public async Task<List<long>> GeneratePickedWorkoutIdsAsync(WorkoutPlanGenerationOptions? options = null)
        {
            var result = await CreateGenerator(options).GenerateAsync(UserId);
            return result.Days.Select(d => d.Day.WorkoutId).ToList();
        }
    }

    [Fact]
    public async Task BuildMuscleUser_PrioritizesHighHypertrophyWorkouts()
    {
        var catalog = new Catalog();
        catalog.Profile(goal: FitnessGoal.BuildMuscle, trainingDays: 2);
        catalog.Workout(1); catalog.Scores(1, hypertrophy: 20, strength: 20, cardio: 90, endurance: 90);
        catalog.Workout(2); catalog.Scores(2, hypertrophy: 95, strength: 60);
        catalog.Workout(3); catalog.Scores(3, hypertrophy: 85, strength: 50);

        var picks = await catalog.GeneratePickedWorkoutIdsAsync();

        Assert.Equal(new long[] { 2, 3 }, picks);
    }

    [Fact]
    public async Task PreferredTrainingDays_DecideTheScheduleAndDayCount()
    {
        var catalog = new Catalog();
        var profile = catalog.Profile(trainingDays: 3);
        profile.PreferredTrainingDays = "Tue,Sat";
        catalog.Workout(1); catalog.Scores(1, hypertrophy: 90, strength: 50);
        catalog.Workout(2); catalog.Scores(2, hypertrophy: 80, strength: 50);
        catalog.Workout(3); catalog.Scores(3, hypertrophy: 70, strength: 50);

        var result = await catalog.CreateGenerator().GenerateAsync(UserId);

        Assert.Equal(new[] { DayOfWeek.Tuesday, DayOfWeek.Saturday }, result.Days.Select(d => d.Day.DayOfWeek));
        Assert.EndsWith("2 Gün", result.Plan.Name);
    }

    [Fact]
    public async Task StrengthGoal_ProducesDifferentOrder_ThanBuildMuscle_OnSameCatalog()
    {
        static Catalog Build(FitnessGoal goal)
        {
            var catalog = new Catalog();
            catalog.Profile(goal: goal, trainingDays: 2);
            catalog.Workout(1); catalog.Scores(1, hypertrophy: 95, strength: 40);
            catalog.Workout(2); catalog.Scores(2, hypertrophy: 40, strength: 95);
            return catalog;
        }

        var muscle = await Build(FitnessGoal.BuildMuscle).GeneratePickedWorkoutIdsAsync();
        var strength = await Build(FitnessGoal.GetStronger).GeneratePickedWorkoutIdsAsync();

        Assert.Equal(1, muscle[0]);
        Assert.Equal(2, strength[0]);
    }

    [Fact]
    public async Task ActiveUserGoal_TakesPrecedenceOverProfileGoal()
    {
        var catalog = new Catalog();
        catalog.Profile(goal: FitnessGoal.BuildMuscle, trainingDays: 2);
        catalog.Goals.Add(new UserGoal { Id = 1, UserId = UserId, GoalType = FitnessGoal.GetStronger, IsActive = true });
        catalog.Workout(1); catalog.Scores(1, hypertrophy: 95, strength: 40);
        catalog.Workout(2); catalog.Scores(2, hypertrophy: 40, strength: 95);

        var result = await catalog.CreateGenerator().GenerateAsync(UserId);

        Assert.Equal(FitnessGoal.GetStronger, result.Plan.Goal);
        Assert.Equal(2, result.Days[0].Day.WorkoutId);
    }

    [Fact]
    public async Task MissingGoal_FallsBackToMaintainWeightProfile()
    {
        var catalog = new Catalog();
        catalog.Profile(goal: null, trainingDays: 2);
        catalog.Workout(1); catalog.Scores(1, hypertrophy: 90, strength: 90);
        catalog.Workout(2); catalog.Scores(2, hypertrophy: 10, strength: 10, cardio: 95, endurance: 95);

        var result = await catalog.CreateGenerator().GenerateAsync(UserId);

        Assert.Equal(FitnessGoal.MaintainWeight, result.Plan.Goal);
        // MaintainWeight: Cardio 25 + Endurance 20 agirlikli -> W2 = 46.25, W1 = 27
        Assert.Equal(2, result.Days[0].Day.WorkoutId);
    }

    [Fact]
    public async Task WorkoutWithUnownedEquipment_IsNeverSelected_EvenWithHighestFitScore()
    {
        var catalog = new Catalog();
        catalog.Profile(goal: FitnessGoal.BuildMuscle, trainingDays: 3);
        catalog.Workout(1); catalog.Scores(1, hypertrophy: 100, strength: 100, cardio: 100, endurance: 100, mobility: 100, coreStability: 100);
        catalog.RequireEquipment(1, equipmentId: 77);
        catalog.Workout(2); catalog.Scores(2, hypertrophy: 10, strength: 10);
        catalog.Workout(3); catalog.Scores(3, hypertrophy: 5, strength: 5);

        var picks = await catalog.GeneratePickedWorkoutIdsAsync();

        Assert.DoesNotContain(1L, picks);
    }

    [Fact]
    public async Task PremiumWorkout_IsExcluded_ForUserWithoutActiveSubscription()
    {
        var catalog = new Catalog();
        catalog.Profile(goal: FitnessGoal.BuildMuscle, trainingDays: 2);
        catalog.Workout(1, isPremium: true); catalog.Scores(1, hypertrophy: 100, strength: 100);
        catalog.Workout(2); catalog.Scores(2, hypertrophy: 10, strength: 10);
        catalog.Workout(3); catalog.Scores(3, hypertrophy: 5, strength: 5);
        // Suresi dolmus abonelik Plus erisimi vermez.
        catalog.Subscriptions.Add(new Subscription { Id = 1, UserId = UserId, Status = SubscriptionStatus.Active, ExpiresAt = DateTime.UtcNow.AddDays(-1) });

        var picks = await catalog.GeneratePickedWorkoutIdsAsync();

        Assert.DoesNotContain(1L, picks);
    }

    [Fact]
    public async Task PremiumWorkout_IsExcluded_FromEquipmentFreeFallback_ForFreeUser()
    {
        var catalog = new Catalog();
        catalog.Profile(goal: FitnessGoal.BuildMuscle, trainingDays: 2);
        // Birincil/bitisik havuzlar ekipman nedeniyle bos; son care bodyweight fallback'i devreye girer.
        catalog.Workout(1); catalog.RequireEquipment(1, equipmentId: 77);
        catalog.Workout(2, difficulty: DifficultyLevel.Advanced, durationMin: 120, isPremium: true);
        catalog.Workout(3, difficulty: DifficultyLevel.Advanced, durationMin: 120);

        var picks = await catalog.GeneratePickedWorkoutIdsAsync();

        Assert.All(picks, id => Assert.Equal(3L, id));
    }

    [Fact]
    public async Task PremiumWorkout_IsLastResort_WhenFreePoolIsEmpty_ForFreeUser()
    {
        var catalog = new Catalog();
        catalog.Profile(goal: FitnessGoal.BuildMuscle, trainingDays: 2);
        // Ucretsiz antrenmanin ekipmani kullanicida yok ve ekipmansiz ucretsiz antrenman da yok:
        // normal zincir bos kalir, Premium (ekipmansiz) antrenman son care olarak kullanilir.
        catalog.Workout(1); catalog.RequireEquipment(1, equipmentId: 77);
        catalog.Workout(2, isPremium: true);

        var picks = await catalog.GeneratePickedWorkoutIdsAsync();

        Assert.NotEmpty(picks);
        Assert.All(picks, id => Assert.Equal(2L, id));
    }

    [Fact]
    public async Task PremiumWorkout_StaysExcluded_WhenFreePoolHasAnyCandidate_ForFreeUser()
    {
        var catalog = new Catalog();
        catalog.Profile(goal: FitnessGoal.BuildMuscle, trainingDays: 2);
        catalog.Workout(1, isPremium: true); catalog.Scores(1, hypertrophy: 100, strength: 100);
        catalog.Workout(2);

        var picks = await catalog.GeneratePickedWorkoutIdsAsync();

        Assert.DoesNotContain(1L, picks);
    }

    [Fact]
    public async Task PremiumWorkout_IsAvailable_ForActivePlusSubscriber()
    {
        var catalog = new Catalog();
        catalog.Profile(goal: FitnessGoal.BuildMuscle, trainingDays: 2);
        catalog.Workout(1, isPremium: true); catalog.Scores(1, hypertrophy: 100, strength: 100);
        catalog.Workout(2); catalog.Scores(2, hypertrophy: 10, strength: 10);
        catalog.Subscriptions.Add(new Subscription { Id = 1, UserId = UserId, Status = SubscriptionStatus.Active, ExpiresAt = DateTime.UtcNow.AddDays(10) });

        var picks = await catalog.GeneratePickedWorkoutIdsAsync();

        Assert.Equal(1L, picks[0]);
    }

    [Fact]
    public async Task FallbackDifficultyWorkout_DoesNotReceiveFullExperienceBonus()
    {
        var catalog = new Catalog();
        catalog.Profile(goal: FitnessGoal.BuildMuscle, experience: ExperienceLevel.Beginner, trainingDays: 2, durationMin: 45);
        // Sure toleransini (45+15) asan iki bodyweight antrenman: yalnizca son care fallback'inde aday olurlar.
        // Advanced + featured olan, eski kodda tam deneyim puaniyla (30+10) Beginner'in (30) onune geciyordu.
        catalog.Workout(1, difficulty: DifficultyLevel.Advanced, durationMin: 120, isFeatured: true);
        catalog.Workout(2, difficulty: DifficultyLevel.Beginner, durationMin: 120);

        var picks = await catalog.GeneratePickedWorkoutIdsAsync();

        Assert.Equal(2L, picks[0]);
    }

    [Fact]
    public async Task AdjacentDifficultyFallback_StillProducesPlan_WhenPrimaryPoolIsEmpty()
    {
        var catalog = new Catalog();
        catalog.Profile(goal: FitnessGoal.BuildMuscle, experience: ExperienceLevel.Beginner, trainingDays: 2);
        catalog.Workout(1, difficulty: DifficultyLevel.Intermediate); catalog.Scores(1, hypertrophy: 90, strength: 50);
        catalog.Workout(2, difficulty: DifficultyLevel.Advanced); catalog.Scores(2, hypertrophy: 100, strength: 100);

        var picks = await catalog.GeneratePickedWorkoutIdsAsync();

        // Advanced (Beginner'a uzak) bu asamada aday degildir; yalnizca bitisik Intermediate kullanilir.
        Assert.All(picks, id => Assert.Equal(1L, id));
    }

    [Fact]
    public async Task DurationTolerance_IsPreserved()
    {
        var catalog = new Catalog();
        catalog.Profile(goal: FitnessGoal.BuildMuscle, trainingDays: 2, durationMin: 30);
        catalog.Workout(1, durationMin: 40); // tolerans (30+15) icinde
        catalog.Workout(2, durationMin: 50); // tolerans disinda
        catalog.Scores(2, hypertrophy: 100, strength: 100, cardio: 100, endurance: 100, mobility: 100, coreStability: 100);

        var picks = await catalog.GeneratePickedWorkoutIdsAsync();

        Assert.All(picks, id => Assert.Equal(1L, id));
    }

    [Fact]
    public async Task RepeatPrevention_AndMuscleGroupBalance_StillApply()
    {
        var catalog = new Catalog();
        catalog.Profile(goal: FitnessGoal.BuildMuscle, trainingDays: 3);
        catalog.Workout(1, muscleGroupId: 10); catalog.Scores(1, hypertrophy: 100, strength: 100);
        catalog.Workout(2, muscleGroupId: 10); catalog.Scores(2, hypertrophy: 95, strength: 95);
        catalog.Workout(3, muscleGroupId: 20); catalog.Scores(3, hypertrophy: 70, strength: 70);

        var picks = await catalog.GeneratePickedWorkoutIdsAsync();

        // 1 (en yuksek) -> 3 (2 ayni kas grubunda oldugu icin cezali: 93-15 < 82) -> 2 (tekrar yok)
        Assert.Equal(new long[] { 1, 3, 2 }, picks);
        Assert.Equal(picks.Count, picks.Distinct().Count());
    }

    [Fact]
    public async Task FewerCandidatesThanTrainingDays_RepeatsWorkoutsAndFillsEveryDay()
    {
        var catalog = new Catalog();
        catalog.Profile(goal: FitnessGoal.BuildMuscle, trainingDays: 4);
        catalog.Workout(1); catalog.Scores(1, hypertrophy: 90, strength: 50);
        catalog.Workout(2); catalog.Scores(2, hypertrophy: 60, strength: 50);

        var result = await catalog.CreateGenerator().GenerateAsync(UserId);

        Assert.Equal(4, result.Days.Count);
        Assert.Equal(new long[] { 1, 2, 1, 2 }, result.Days.Select(d => d.Day.WorkoutId));
    }

    [Fact]
    public async Task EqualScores_AreResolvedDeterministically_ByWorkoutId()
    {
        static Catalog Build()
        {
            var catalog = new Catalog();
            catalog.Profile(goal: FitnessGoal.BuildMuscle, trainingDays: 3);
            foreach (var id in new long[] { 5, 3, 4 })
            {
                catalog.Workout(id);
                catalog.Scores(id, hypertrophy: 80, strength: 80);
            }

            return catalog;
        }

        var first = await Build().GeneratePickedWorkoutIdsAsync();
        var second = await Build().GeneratePickedWorkoutIdsAsync();

        Assert.Equal(new long[] { 3, 4, 5 }, first);
        Assert.Equal(first, second);
    }

    [Fact]
    public async Task PlanStructure_FollowsTrainingDaysPattern()
    {
        var catalog = new Catalog();
        catalog.Profile(goal: FitnessGoal.BuildMuscle, trainingDays: 4);
        for (var id = 1; id <= 5; id++)
        {
            catalog.Workout(id);
        }

        var result = await catalog.CreateGenerator().GenerateAsync(UserId);

        Assert.Equal(
            new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Thursday, DayOfWeek.Saturday },
            result.Days.Select(d => d.Day.DayOfWeek));
        Assert.All(result.Days, d => Assert.Single(d.Exercises));
        Assert.True(result.Plan.IsActive);
        Assert.Equal(UserId, result.Plan.UserId);
    }

    [Fact]
    public async Task CatalogWithoutAnyScores_KeepsLegacyOrdering()
    {
        var catalog = new Catalog();
        catalog.Profile(goal: FitnessGoal.BuildMuscle, trainingDays: 3, durationMin: 30);
        catalog.Workout(1, durationMin: 40);                  // 30 + kismi sure (20 * 5/15 -> 7) = 37
        catalog.Workout(2, durationMin: 30, isFeatured: true); // 30 + 20 + 10 = 60
        catalog.Workout(3, durationMin: 25);                  // 30 + 20 = 50

        var picks = await catalog.GeneratePickedWorkoutIdsAsync();

        Assert.Equal(new long[] { 2, 3, 1 }, picks);
    }

    [Fact]
    public async Task ScoredWorkouts_RankAboveUnscored_OnlyThroughGoalFitBonus()
    {
        var catalog = new Catalog();
        catalog.Profile(goal: FitnessGoal.BuildMuscle, trainingDays: 2);
        catalog.Workout(1);                                    // puansiz: 50
        catalog.Workout(2); catalog.Scores(2, hypertrophy: 0, strength: 0); // FitScore 0 -> 50
        catalog.Workout(3); catalog.Scores(3, hypertrophy: 60, strength: 60); // FitScore 42 -> 50 + 16.8

        var picks = await catalog.GeneratePickedWorkoutIdsAsync();

        // Puansiz antrenman cezalandirilmaz: FitScore'u 0 olanla esit kalir ve Id ile cozulur.
        Assert.Equal(new long[] { 3, 1 }, picks);
    }

    [Fact]
    public async Task PartiallyScoredWorkout_UsesNormalizedWeights()
    {
        var catalog = new Catalog();
        catalog.Profile(goal: FitnessGoal.BuildMuscle, trainingDays: 2);
        // Yalnizca Hypertrophy=80 -> normalize FitScore 80; tam puanli W2 -> 50*0.5+... = 59
        catalog.Workout(1); catalog.Score(1, WorkoutAttributeType.Hypertrophy, 80);
        catalog.Workout(2); catalog.Scores(2, hypertrophy: 70, strength: 70, cardio: 30, endurance: 30, mobility: 30, coreStability: 30);

        var picks = await catalog.GeneratePickedWorkoutIdsAsync();

        Assert.Equal(1L, picks[0]);
    }

    [Fact]
    public async Task MissingGoalWeights_FailsExplicitly_InsteadOfScoringSilently()
    {
        var catalog = new Catalog();
        catalog.Profile(goal: FitnessGoal.LoseWeight, trainingDays: 2);
        catalog.Workout(1);
        var options = new WorkoutPlanGenerationOptions();
        options.AttributeWeightsByGoal.Remove(FitnessGoal.LoseWeight);

        await Assert.ThrowsAsync<InvalidOperationException>(() => catalog.CreateGenerator(options).GenerateAsync(UserId));
    }
}
