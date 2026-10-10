using MerfitCustomerApi.Business.Dtos.Admin.Workouts;
using MerfitCustomerApi.Business.Services.Workouts;
using MerfitCustomerApi.Business.Tests.TestHelpers;
using MerfitCustomerApi.Domain.Entities;
using MerfitCustomerApi.Domain.Entities.Enums;
using MerfitCustomerApi.Domain.Exceptions;
using MerfitCustomerApi.Domain.Interfaces;
using Moq;
using Xunit;

namespace MerfitCustomerApi.Business.Tests.Workouts;

/// <summary>Antrenman ozellik puanlarinin admin tarafindan replace-all mantigiyla yonetimini dogrular.</summary>
public class WorkoutAttributeScoreServiceTests
{
    private const long WorkoutId = 5;

    private readonly List<Workout> _workouts = new() { new Workout { Id = WorkoutId, Title = "W", Slug = "w" } };
    private readonly List<WorkoutAttributeScore> _scores = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly WorkoutAttributeScoreService _service;

    public WorkoutAttributeScoreServiceTests()
    {
        _unitOfWork.Setup(u => u.Repository<Workout>()).Returns(MockRepositoryFactory.Create(_workouts).Object);
        _unitOfWork.Setup(u => u.Repository<WorkoutAttributeScore>()).Returns(MockRepositoryFactory.Create(_scores).Object);
        _unitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        _service = new WorkoutAttributeScoreService(_unitOfWork.Object);
    }

    private static AdminSetWorkoutAttributeScoresRequest Request(params (WorkoutAttributeType? Type, int Score)[] items) => new()
    {
        Scores = items.Select(i => new AdminWorkoutAttributeScoreInput { AttributeType = i.Type, Score = i.Score }).ToList(),
    };

    [Fact]
    public async Task SetAsync_AddsUpdatesAndRemoves_ToMatchRequest()
    {
        _scores.Add(new WorkoutAttributeScore { Id = 1, WorkoutId = WorkoutId, AttributeType = WorkoutAttributeType.Hypertrophy, Score = 50 });
        _scores.Add(new WorkoutAttributeScore { Id = 2, WorkoutId = WorkoutId, AttributeType = WorkoutAttributeType.Cardio, Score = 30 });

        var result = await _service.SetAsync(
            WorkoutId,
            Request((WorkoutAttributeType.Hypertrophy, 90), (WorkoutAttributeType.Strength, 75)),
            adminUserId: 42);

        Assert.Equal(2, _scores.Count);
        var hypertrophy = Assert.Single(_scores, s => s.AttributeType == WorkoutAttributeType.Hypertrophy);
        Assert.Equal(90, hypertrophy.Score);
        Assert.Equal(42, hypertrophy.UpdatedUser);
        var strength = Assert.Single(_scores, s => s.AttributeType == WorkoutAttributeType.Strength);
        Assert.Equal(42, strength.CreatedUser);
        Assert.DoesNotContain(_scores, s => s.AttributeType == WorkoutAttributeType.Cardio);

        Assert.Equal(new[] { "Hypertrophy", "Strength" }, result.Scores.Select(s => s.AttributeType));
        Assert.Equal(new[] { "Cardio", "Endurance", "Mobility", "CoreStability" }, result.MissingAttributeTypes);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    public async Task SetAsync_RejectsScoresOutsideZeroToHundred(int score)
    {
        await Assert.ThrowsAsync<AppValidationException>(() =>
            _service.SetAsync(WorkoutId, Request((WorkoutAttributeType.Cardio, score)), adminUserId: 1));

        Assert.Empty(_scores);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SetAsync_RejectsDuplicateAttributeTypesInRequest()
    {
        await Assert.ThrowsAsync<AppValidationException>(() =>
            _service.SetAsync(WorkoutId, Request((WorkoutAttributeType.Cardio, 10), (WorkoutAttributeType.Cardio, 20)), adminUserId: 1));
    }

    [Theory]
    [InlineData(null)]
    [InlineData((WorkoutAttributeType)99)]
    public async Task SetAsync_RejectsMissingOrUndefinedAttributeType(WorkoutAttributeType? type)
    {
        await Assert.ThrowsAsync<AppValidationException>(() =>
            _service.SetAsync(WorkoutId, Request((type, 10)), adminUserId: 1));
    }

    [Fact]
    public async Task SetAsync_ThrowsNotFound_WhenWorkoutDoesNotExist()
    {
        await Assert.ThrowsAsync<NotFoundException>(() =>
            _service.SetAsync(999, Request((WorkoutAttributeType.Cardio, 10)), adminUserId: 1));
    }

    [Fact]
    public async Task GetAsync_ListsMissingAttributeTypes_ForUnscoredWorkout()
    {
        var result = await _service.GetAsync(WorkoutId);

        Assert.Empty(result.Scores);
        Assert.Equal(6, result.MissingAttributeTypes.Count);
    }
}
