using MerfitCustomerApi.Business.Common;
using MerfitCustomerApi.Business.Interfaces.Services;
using MerfitCustomerApi.Business.Services.Personalization;
using MerfitCustomerApi.Business.Services.Personalization.Generators;
using MerfitCustomerApi.Business.Services.Personalization.Models;
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
/// Geriye donuk uyumluluk: henuz hicbir antrenmana ozellik puani girilmemis mevcut bir katalogla,
/// Pending bir PersonalizationJob'un gercek WorkoutPlanGenerator ile islenip, plan/gun/egzersiz
/// grafiginin tek transaction icinde kaydedildigini ve job'un Completed oldugunu dogrular.
/// </summary>
public class PersonalizationJobFlowTests
{
    private const long UserId = 7;

    [Fact]
    public async Task PendingJob_IsCompleted_WithLegacyCatalogThatHasNoAttributeScores()
    {
        await RunJobAsync(nutritionFails: false);
    }

    [Fact]
    public async Task NutritionFailure_StillSavesWorkoutPlan_AndKeepsExistingNutritionPlan()
    {
        await RunJobAsync(nutritionFails: true);
    }

    private static async Task RunJobAsync(bool nutritionFails)
    {
        var jobs = new List<PersonalizationJob> { new() { Id = 1, UserId = UserId, Status = PersonalizationJobStatus.Pending, CreatedAt = DateTime.UtcNow } };
        var workoutPlans = new List<WorkoutPlan>();
        var planDays = new List<WorkoutPlanDay>();
        var planExercises = new List<WorkoutPlanExercise>();
        var workouts = new List<Workout>
        {
            new() { Id = 1, Title = "A", Slug = "a", Difficulty = DifficultyLevel.Beginner, DurationMin = 30, MuscleGroupId = 1, IsActive = true },
            new() { Id = 2, Title = "B", Slug = "b", Difficulty = DifficultyLevel.Beginner, DurationMin = 30, MuscleGroupId = 2, IsActive = true },
        };
        var workoutExercises = workouts
            .Select(w => new WorkoutExercise { Id = w.Id, WorkoutId = w.Id, ExerciseId = w.Id * 10, Order = 1, Sets = 3 })
            .ToList();

        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(u => u.Repository<PersonalizationJob>()).Returns(MockRepositoryFactory.Create(jobs).Object);
        unitOfWork.Setup(u => u.Repository<UserProfile>()).Returns(MockRepositoryFactory.Create(new List<UserProfile>
        {
            new() { Id = 1, UserId = UserId, Goal = FitnessGoal.BuildMuscle, ExperienceLevel = ExperienceLevel.Beginner, TrainingDaysPerWeek = 3 },
        }).Object);
        unitOfWork.Setup(u => u.Repository<UserGoal>()).Returns(MockRepositoryFactory.Create(new List<UserGoal>()).Object);
        unitOfWork.Setup(u => u.Repository<UserEquipment>()).Returns(MockRepositoryFactory.Create(new List<UserEquipment>()).Object);
        unitOfWork.Setup(u => u.Repository<Subscription>()).Returns(MockRepositoryFactory.Create(new List<Subscription>()).Object);
        unitOfWork.Setup(u => u.Repository<Workout>()).Returns(MockRepositoryFactory.Create(workouts).Object);
        unitOfWork.Setup(u => u.Repository<WorkoutEquipment>()).Returns(MockRepositoryFactory.Create(new List<WorkoutEquipment>()).Object);
        unitOfWork.Setup(u => u.Repository<WorkoutExercise>()).Returns(MockRepositoryFactory.Create(workoutExercises).Object);
        unitOfWork.Setup(u => u.Repository<WorkoutAttributeScore>()).Returns(MockRepositoryFactory.Create(new List<WorkoutAttributeScore>()).Object);
        unitOfWork.Setup(u => u.Repository<WorkoutPlan>()).Returns(MockRepositoryFactory.Create(workoutPlans).Object);
        unitOfWork.Setup(u => u.Repository<WorkoutPlanDay>()).Returns(MockRepositoryFactory.Create(planDays).Object);
        unitOfWork.Setup(u => u.Repository<WorkoutPlanExercise>()).Returns(MockRepositoryFactory.Create(planExercises).Object);
        unitOfWork.Setup(u => u.Repository<NutritionPlanDay>()).Returns(MockRepositoryFactory.Create(new List<NutritionPlanDay>()).Object);
        unitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var nutritionPlans = new List<NutritionPlan> { new() { Id = 50, UserId = UserId, IsActive = true } };
        unitOfWork.Setup(u => u.Repository<NutritionPlan>()).Returns(MockRepositoryFactory.Create(nutritionPlans).Object);

        var nutritionGenerator = new Mock<INutritionPlanGenerator>();
        if (nutritionFails)
        {
            nutritionGenerator
                .Setup(g => g.GenerateAsync(UserId, It.IsAny<CancellationToken>()))
                .ThrowsAsync(new MerfitCustomerApi.Domain.Exceptions.PersonalizationGenerationException("Besin katalogu bos."));
        }
        else
        {
            nutritionGenerator
                .Setup(g => g.GenerateAsync(UserId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new NutritionPlanGenerationResult
                {
                    Plan = new NutritionPlan { UserId = UserId, IsActive = true },
                    Days = new List<NutritionPlanDayResult>(),
                    Summary = "test",
                });
        }

        var processor = new PersonalizationJobProcessor(
            unitOfWork.Object,
            new WorkoutPlanGenerator(unitOfWork.Object, Options.Create(new WorkoutPlanGenerationOptions()), NullLogger<WorkoutPlanGenerator>.Instance),
            nutritionGenerator.Object,
            Options.Create(new PersonalizationJobProcessingOptions()),
            NullLogger<PersonalizationJobProcessor>.Instance);

        var processed = await processor.ProcessNextAsync();

        Assert.True(processed);
        var job = Assert.Single(jobs);
        Assert.Equal(PersonalizationJobStatus.Completed, job.Status);
        Assert.Equal(nutritionFails ? "Besin katalogu bos." : null, job.ErrorMessage);
        Assert.Single(workoutPlans);
        // Beslenme uretilemediyse eski beslenme programi pasiflestirilmez.
        Assert.Equal(nutritionFails, nutritionPlans[0].IsActive);
        Assert.Equal(3, planDays.Count);
        Assert.Equal(3, planExercises.Count);
        unitOfWork.Verify(u => u.BeginTransactionAsync(It.IsAny<CancellationToken>()), Times.Once);
        unitOfWork.Verify(u => u.CommitTransactionAsync(It.IsAny<CancellationToken>()), Times.Once);
        unitOfWork.Verify(u => u.RollbackTransactionAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
}
