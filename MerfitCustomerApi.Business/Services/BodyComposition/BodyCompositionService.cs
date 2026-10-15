using MerfitCustomerApi.Business.Common;
using MerfitCustomerApi.Business.Dtos.Customer.BodyComposition;
using MerfitCustomerApi.Business.Interfaces.Services;
using MerfitCustomerApi.Business.Services.Personalization;
using MerfitCustomerApi.Business.Services.Progress;
using MerfitCustomerApi.Domain.Entities;
using MerfitCustomerApi.Domain.Entities.Enums;
using MerfitCustomerApi.Domain.Exceptions;
using MerfitCustomerApi.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace MerfitCustomerApi.Business.Services.BodyComposition;

/// <summary>
/// IBodyCompositionService'in varsayilan implementasyonu. Mevcut BodyMeasurement tablosunu kullanir;
/// her olcum yeni bir satirdir ve gecmis kayitlar degistirilmez.
/// </summary>
public class BodyCompositionService : IBodyCompositionService
{
    private const int MaxBackfillDays = 365;

    private readonly IUnitOfWork _unitOfWork;
    private readonly BodyCompositionOptions _options;

    public BodyCompositionService(IUnitOfWork unitOfWork, IOptions<BodyCompositionOptions>? options = null)
    {
        _unitOfWork = unitOfWork;
        _options = options?.Value ?? new BodyCompositionOptions();
    }

    public async Task<BodyMeasurementHistoryResponse> GetHistoryAsync(long userId, CancellationToken cancellationToken = default)
    {
        var profile = await GetProfileAsync(userId, cancellationToken);
        return await BuildHistoryAsync(profile, cancellationToken);
    }

    public async Task<BodyMeasurementHistoryResponse> AddMeasurementAsync(
        long userId, CreateBodyMeasurementRequest request, CancellationToken cancellationToken = default)
    {
        if (request.WeightKg is null && request.BodyFatPercentage is null && request.NeckCm is null
            && request.ChestCm is null && request.WaistCm is null && request.HipCm is null
            && request.ArmCm is null && request.ThighCm is null)
        {
            throw new AppValidationException("En az bir ölçüm girilmelidir.");
        }

        var now = DateTime.UtcNow;
        var today = DateOnly.FromDateTime(now);
        var date = request.Date ?? today;
        // Kullanicinin saat dilimi UTC'nin onunde olabilir; yarina kadar kabul edilir (bkz. kilo girisi).
        if (date > today.AddDays(1))
        {
            throw new AppValidationException(nameof(request.Date), "Gelecek bir tarihe ölçüm girilemez.");
        }

        if (date < today.AddDays(-MaxBackfillDays))
        {
            throw new AppValidationException(nameof(request.Date), "En fazla bir yıl öncesine ölçüm girilebilir.");
        }

        if (request.NeckCm is not null && request.WaistCm is not null && request.WaistCm <= request.NeckCm)
        {
            throw new AppValidationException(nameof(request.WaistCm), "Bel çevresi boyun çevresinden büyük olmalıdır.");
        }

        var profile = await GetProfileAsync(userId, cancellationToken);
        var recordedAt = request.Date is null || date == today
            ? now
            : date.ToDateTime(new TimeOnly(12, 0), DateTimeKind.Utc);

        var weight = request.WeightKg is null ? (decimal?)null : Math.Round(request.WeightKg.Value, 1);
        var estimate = BodyFatEstimator.EstimateUsNavy(profile.Gender, profile.HeightCm, request.NeckCm, request.WaistCm, request.HipCm);
        var measurement = new BodyMeasurement
        {
            UserId = userId,
            RecordedAt = recordedAt,
            WeightKg = weight,
            BodyFatPercentage = Round(request.BodyFatPercentage),
            EstimatedBodyFatPercentage = estimate,
            BodyFatEstimationMethod = estimate is null ? null : BodyFatEstimator.UsNavyMethod,
            NeckCm = Round(request.NeckCm),
            ChestCm = Round(request.ChestCm),
            WaistCm = Round(request.WaistCm),
            HipCm = Round(request.HipCm),
            ArmCm = Round(request.ArmCm),
            ThighCm = Round(request.ThighCm),
            BMI = weight is not null && profile.HeightCm is > 0
                ? Math.Round(weight.Value / (profile.HeightCm.Value / 100m * (profile.HeightCm.Value / 100m)), 1)
                : null,
            CreatedAt = now,
        };
        await _unitOfWork.Repository<BodyMeasurement>().AddAsync(measurement, cancellationToken);

        if (weight is not null)
        {
            var hasNewerWeight = await _unitOfWork.Repository<BodyMeasurement>()
                .AnyAsync(m => m.UserId == userId && m.RecordedAt > recordedAt && m.WeightKg != null, cancellationToken);
            if (!hasNewerWeight)
            {
                var activeGoal = await _unitOfWork.Repository<UserGoal>()
                    .FirstOrDefaultAsync(g => g.UserId == userId && g.IsActive, cancellationToken);
                await WeightTracking.ApplyNewestWeightAsync(_unitOfWork, profile, activeGoal, weight.Value, cancellationToken);
            }
        }

        if (measurement.BodyFatPercentage is not null || measurement.EstimatedBodyFatPercentage is not null)
        {
            // Yag orani beslenme stratejisini etkiler (kalori fazlasi/acigi); hedefler yeniden hesaplanir.
            await PlanRegenerationQueue.EnqueueAsync(_unitOfWork, userId, nutritionOnly: true, cancellationToken);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return await BuildHistoryAsync(profile, cancellationToken);
    }

    private async Task<UserProfile> GetProfileAsync(long userId, CancellationToken cancellationToken) =>
        await _unitOfWork.Repository<UserProfile>().FirstOrDefaultAsync(p => p.UserId == userId, cancellationToken)
        ?? throw new NotFoundException(nameof(UserProfile), userId);

    private async Task<BodyMeasurementHistoryResponse> BuildHistoryAsync(UserProfile profile, CancellationToken cancellationToken)
    {
        var measurements = await _unitOfWork.Repository<BodyMeasurement>()
            .GetQueryable()
            .Where(m => m.UserId == profile.UserId)
            .OrderByDescending(m => m.RecordedAt)
            .ThenByDescending(m => m.Id)
            .ToListAsync(cancellationToken);

        var assessment = BodyFatAssessor.Assess(profile, measurements, DateTime.UtcNow, _options);
        var activeGoal = await _unitOfWork.Repository<UserGoal>()
            .FirstOrDefaultAsync(g => g.UserId == profile.UserId && g.IsActive, cancellationToken);
        var goal = FitnessGoals.Normalize(activeGoal?.GoalType ?? profile.Goal);
        var showWarning = LowBodyFatGoalGuard.RequiresWarning(goal, assessment);

        return new BodyMeasurementHistoryResponse
        {
            HeightCm = profile.HeightCm,
            Gender = profile.Gender switch
            {
                Gender.Male => "male",
                Gender.Female => "female",
                Gender.Other => "other",
                _ => null,
            },
            Measurements = measurements.Select(m => new BodyMeasurementDto
            {
                Id = m.Id,
                RecordedAt = m.RecordedAt,
                WeightKg = m.WeightKg,
                BodyFatPercentage = m.BodyFatPercentage,
                EstimatedBodyFatPercentage = m.EstimatedBodyFatPercentage,
                BodyFatEstimationMethod = m.BodyFatEstimationMethod,
                NeckCm = m.NeckCm,
                ChestCm = m.ChestCm,
                WaistCm = m.WaistCm,
                HipCm = m.HipCm,
                ArmCm = m.ArmCm,
                ThighCm = m.ThighCm,
                Bmi = m.BMI,
            }).ToList(),
            BodyFat = BodyFatAssessmentDto.From(assessment),
            ShowLowBodyFatWarning = showWarning,
            LowBodyFatWarningMessage = showWarning ? LowBodyFatGoalGuard.WarningMessage : null,
        };
    }

    private static decimal? Round(decimal? value) => value is null ? null : Math.Round(value.Value, 1);
}
