using MerfitCustomerApi.Business.Common;
using MerfitCustomerApi.Business.Services.Personalization.Models;
using MerfitCustomerApi.Domain.Entities;
using MerfitCustomerApi.Domain.Entities.Enums;
using Xunit;

namespace MerfitCustomerApi.Business.Tests.WorkoutLab;

/// <summary>
/// Kullanici akisi (GenerateAsync(userId)) ile simulasyon akisinin (GenerateAsync(input)) ayni algoritmayi
/// calistirdigini dogrular: ayni tercihler ayni programi uretir.
/// </summary>
public class WorkoutPlanGeneratorInputTests
{
    private const long UserId = 42;

    public static IEnumerable<object?[]> Profiles() => new[]
    {
        new object?[] { FitnessGoal.BuildMuscle, ExperienceLevel.Beginner, 3, null, 45, Array.Empty<long>(), false },
        new object?[] { FitnessGoal.GetStronger, ExperienceLevel.Intermediate, 5, null, 60, new[] { 1L, 2L, 3L, 4L, 5L }, false },
        new object?[] { FitnessGoal.LoseWeight, ExperienceLevel.Advanced, 4, null, 90, new[] { 1L, 2L, 3L, 4L, 5L }, true },
        new object?[] { FitnessGoal.ImproveFitness, ExperienceLevel.Beginner, 2, "Mon,Thu,Sat", 30, new[] { 1L }, false },
        new object?[] { null, null, null, null, null, Array.Empty<long>(), false },
    };

    [Theory]
    [MemberData(nameof(Profiles))]
    public async Task UserFlowAndSimulation_ProduceIdenticalPlans(
        FitnessGoal? goal, ExperienceLevel? experience, int? days, string? preferredDays, int? duration, long[] equipment, bool plus)
    {
        var catalog = new WorkoutLabTestCatalog().WithDefaultCatalog();
        catalog.Uow.Set<UserProfile>().Add(new UserProfile
        {
            Id = 1, UserId = UserId, Goal = goal, ExperienceLevel = experience, TrainingDaysPerWeek = days, HasEquipment = equipment.Length > 0,
            PreferredTrainingDays = preferredDays, WorkoutDurationMin = duration,
        });
        foreach (var id in equipment)
        {
            catalog.Uow.Set<UserEquipment>().Add(new UserEquipment { Id = id, UserId = UserId, EquipmentId = id });
        }

        if (plus)
        {
            catalog.Uow.Set<Subscription>().Add(new Subscription { Id = 1, UserId = UserId, Status = SubscriptionStatus.Active, ExpiresAt = DateTime.UtcNow.AddDays(10) });
        }

        var generator = catalog.CreateGenerator();
        var fromUser = await generator.GenerateAsync(UserId);
        var fromInput = await generator.GenerateAsync(new WorkoutPlanGenerationInput
        {
            UserId = UserId,
            Goal = goal,
            ExperienceLevel = experience,
            PreferredTrainingDays = TrainingDayCodes.ParseStored(preferredDays),
            WorkoutDurationMin = duration,
            // Ekipmanli kullanici katalogdaki tum ekipmanlara sahip sayilir (varsayilan katalog: 1-5).
            OwnedEquipmentIds = equipment.Length > 0 ? new long[] { 1, 2, 3, 4, 5 } : Array.Empty<long>(),
            HasActivePlusSubscription = plus,
        });

        Assert.Equal(fromUser.Plan.Name, fromInput.Plan.Name);
        Assert.Equal(fromUser.Plan.Goal, fromInput.Plan.Goal);
        Assert.Equal(fromUser.Summary, fromInput.Summary);
        Assert.Equal(
            fromUser.Days.Select(d => (d.Day.DayOfWeek, d.Day.WorkoutId, d.Day.CardioWorkoutId)),
            fromInput.Days.Select(d => (d.Day.DayOfWeek, d.Day.WorkoutId, d.Day.CardioWorkoutId)));
        Assert.Equal(
            fromUser.Days.SelectMany(d => d.Exercises.Select(e => (e.ExerciseId, e.Sets, e.Reps, e.RepsMax, e.RestSeconds, e.DurationSeconds))),
            fromInput.Days.SelectMany(d => d.Exercises.Select(e => (e.ExerciseId, e.Sets, e.Reps, e.RepsMax, e.RestSeconds, e.DurationSeconds))));
        Assert.Equal(fromUser.Diagnostics!.PoolStage, fromInput.Diagnostics!.PoolStage);
        // Eski hedef (ImproveFitness) simulasyonda da ayni normalizasyonla Kas Gelistirme olur.
        Assert.True(FitnessGoals.IsPrimary(fromInput.Plan.Goal!.Value));
    }

    [Fact]
    public async Task Generator_DoesNotReadUserTables_WhenCalledWithInput()
    {
        var catalog = new WorkoutLabTestCatalog().WithDefaultCatalog();

        await catalog.CreateGenerator().GenerateAsync(new WorkoutPlanGenerationInput { Goal = FitnessGoal.BuildMuscle });

        catalog.Uow.Mock.Verify(u => u.Repository<UserProfile>(), Moq.Times.Never);
        catalog.Uow.Mock.Verify(u => u.Repository<UserGoal>(), Moq.Times.Never);
        catalog.Uow.Mock.Verify(u => u.Repository<UserEquipment>(), Moq.Times.Never);
        catalog.Uow.Mock.Verify(u => u.Repository<Subscription>(), Moq.Times.Never);
    }
}
