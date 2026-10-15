using MerfitCustomerApi.Business.Dtos.Customer.BodyComposition;
using MerfitCustomerApi.Business.Services.BodyComposition;
using MerfitCustomerApi.Business.Tests.TestHelpers;
using MerfitCustomerApi.Domain.Entities;
using MerfitCustomerApi.Domain.Entities.Enums;
using MerfitCustomerApi.Domain.Exceptions;
using MerfitCustomerApi.Domain.Interfaces;
using Moq;
using Xunit;

namespace MerfitCustomerApi.Business.Tests.BodyComposition;

/// <summary>
/// Olcum kaydi: her giris yeni satirdir (gecmis ezilmez), tahmin girilen degerden ayri saklanir ve
/// "tahmini" etiketiyle doner, gecersiz olcumler reddedilir, Yag Kaybi + dusuk yag orani uyari bayragi tasir.
/// </summary>
public class BodyCompositionServiceTests
{
    private const long UserId = 11;

    private sealed class Fixture
    {
        public required BodyCompositionService Service { get; init; }
        public required List<BodyMeasurement> Measurements { get; init; }
        public required List<PersonalizationJob> Jobs { get; init; }
        public required UserProfile Profile { get; init; }
    }

    private static Fixture Create(FitnessGoal goal = FitnessGoal.BuildMuscle, Gender gender = Gender.Male)
    {
        var profile = new UserProfile
        {
            Id = 1,
            UserId = UserId,
            Gender = gender,
            HeightCm = 180,
            WeightKg = 75,
            DateOfBirth = DateTime.UtcNow.Date.AddYears(-28),
            Goal = goal,
        };
        var measurements = new List<BodyMeasurement>();
        var jobs = new List<PersonalizationJob>();
        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(u => u.Repository<UserProfile>()).Returns(MockRepositoryFactory.Create(new List<UserProfile> { profile }).Object);
        unitOfWork.Setup(u => u.Repository<UserGoal>()).Returns(MockRepositoryFactory.Create(new List<UserGoal>
        {
            new() { Id = 1, UserId = UserId, GoalType = goal, IsActive = true },
        }).Object);
        unitOfWork.Setup(u => u.Repository<BodyMeasurement>()).Returns(MockRepositoryFactory.Create(measurements).Object);
        unitOfWork.Setup(u => u.Repository<PersonalizationJob>()).Returns(MockRepositoryFactory.Create(jobs).Object);
        unitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Callback(() =>
            {
                foreach (var m in measurements.Where(m => m.Id == 0))
                {
                    m.Id = measurements.Max(x => x.Id) + 1;
                }
            })
            .ReturnsAsync(1);

        return new Fixture { Service = new BodyCompositionService(unitOfWork.Object), Measurements = measurements, Jobs = jobs, Profile = profile };
    }

    [Fact]
    public async Task EachEntry_CreatesNewRow_HistoryIsNeverOverwritten()
    {
        var fx = Create();

        await fx.Service.AddMeasurementAsync(UserId, new CreateBodyMeasurementRequest { WeightKg = 75, WaistCm = 84, NeckCm = 38 });
        var history = await fx.Service.AddMeasurementAsync(UserId, new CreateBodyMeasurementRequest { WeightKg = 73.8m, WaistCm = 83, NeckCm = 38 });

        Assert.Equal(2, fx.Measurements.Count);
        Assert.Equal(new[] { 84m, 83m }, fx.Measurements.Select(m => m.WaistCm!.Value));
        Assert.Equal(2, history.Measurements.Count);
        Assert.Equal(73.8m, fx.Profile.WeightKg); // en yeni kilo profile yazilir (1 kg esigi)
    }

    [Fact]
    public async Task CircumferenceEntry_StoresEstimateSeparately_AndLabelsItEstimated()
    {
        var fx = Create();

        var history = await fx.Service.AddMeasurementAsync(UserId, new CreateBodyMeasurementRequest { NeckCm = 38, WaistCm = 86 });

        var row = Assert.Single(fx.Measurements);
        Assert.Null(row.BodyFatPercentage);                 // olculen alan bos kalir
        Assert.NotNull(row.EstimatedBodyFatPercentage);
        Assert.Equal(BodyFatEstimator.UsNavyMethod, row.BodyFatEstimationMethod);
        Assert.Equal("estimated", history.BodyFat.Source);
        Assert.Equal("healthy", history.BodyFat.Category);
        Assert.NotNull(history.BodyFat.MarginOfErrorPercent);
        // Yag orani beslenme hedeflerini etkiler: beslenme yeniden hesaplanir.
        Assert.True(Assert.Single(fx.Jobs).NutritionOnly);
    }

    [Fact]
    public async Task WeightOnlyEntry_DoesNotProduceBodyFat()
    {
        var fx = Create();

        var history = await fx.Service.AddMeasurementAsync(UserId, new CreateBodyMeasurementRequest { WeightKg = 70 });

        Assert.Null(Assert.Single(fx.Measurements).EstimatedBodyFatPercentage);
        Assert.Equal("unknown", history.BodyFat.Category);
        Assert.Null(history.BodyFat.Percentage);
    }

    [Fact]
    public async Task InvalidEntries_AreRejected()
    {
        var fx = Create();

        await Assert.ThrowsAsync<AppValidationException>(() =>
            fx.Service.AddMeasurementAsync(UserId, new CreateBodyMeasurementRequest()));
        await Assert.ThrowsAsync<AppValidationException>(() =>
            fx.Service.AddMeasurementAsync(UserId, new CreateBodyMeasurementRequest { NeckCm = 40, WaistCm = 39 }));
        await Assert.ThrowsAsync<AppValidationException>(() =>
            fx.Service.AddMeasurementAsync(UserId, new CreateBodyMeasurementRequest { WeightKg = 70, Date = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(5) }));
        Assert.Empty(fx.Measurements);
    }

    [Fact]
    public void OutOfRangeValues_FailDtoValidation()
    {
        var request = new CreateBodyMeasurementRequest { BodyFatPercentage = 1, WaistCm = 500 };
        var results = new List<System.ComponentModel.DataAnnotations.ValidationResult>();

        var valid = System.ComponentModel.DataAnnotations.Validator.TryValidateObject(
            request, new System.ComponentModel.DataAnnotations.ValidationContext(request), results, validateAllProperties: true);

        Assert.False(valid);
        Assert.Equal(2, results.Count);
    }

    [Fact]
    public async Task LoseWeightGoal_WithLowBodyFat_FlagsWarning()
    {
        var fx = Create(goal: FitnessGoal.LoseWeight);

        var history = await fx.Service.AddMeasurementAsync(UserId, new CreateBodyMeasurementRequest { BodyFatPercentage = 6 });

        Assert.True(history.ShowLowBodyFatWarning);
        Assert.Equal(LowBodyFatGoalGuard.WarningMessage, history.LowBodyFatWarningMessage);
        Assert.Equal("measured", history.BodyFat.Source);
    }
}
