using MerfitCustomerApi.Business.Common;
using MerfitCustomerApi.Business.Dtos.Auth;
using MerfitCustomerApi.Business.Interfaces.Services;
using MerfitCustomerApi.Business.Services.BodyComposition;
using MerfitCustomerApi.Business.Services.Progress;
using MerfitCustomerApi.Domain.Entities;
using MerfitCustomerApi.Domain.Entities.Enums;
using MerfitCustomerApi.Domain.Exceptions;
using MerfitCustomerApi.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace MerfitCustomerApi.Business.Services.Auth;

/// <summary>
/// Onboarding'in fitness alanlarini (<see cref="FitnessProfileRequest"/>) profile ve kisisellestirme altyapisina
/// (UserGoal, ilk kilo/olcum kaydi, NutritionGoal, ekipmanlar) uygular. Hem kayitta (AuthService) hem de yalnizca
/// alisveris icin kayit olmus kullanicinin profilini sonradan tamamlamasinda (FitnessProfileService) ayni kurallar
/// kullanilir.
///
/// Kullanim: once <see cref="Prepare"/> (dogrulama + dusuk yag orani onayi; veritabanina yazmadan once), sonra
/// <see cref="ApplyTo"/> ile profil alanlari, en son <see cref="PersistAsync"/> ile iliskili kayitlar.
/// </summary>
internal sealed class FitnessOnboarding
{
    /// <summary>
    /// UserProfile.Goal bos birakildiginda UserGoal.GoalType icin kullanilan varsayilan hedef: uc ana hedeften
    /// kalori acigi uygulamayan Kas Gelistirme (bkz. FitnessGoals.Default).
    /// </summary>
    private const FitnessGoal DefaultGoalType = FitnessGoals.Default;

    private readonly FitnessProfileRequest _request;
    private readonly decimal? _heightCm;
    private readonly decimal? _weightKg;
    private readonly FitnessGoal? _goal;
    private readonly Gender? _gender;
    private readonly DateTime? _dateOfBirth;
    private readonly BodyMeasurement? _measurement;
    private readonly BodyFatAssessment _bodyFat;
    private readonly bool _lowBodyFatConfirmed;

    private FitnessOnboarding(FitnessProfileRequest request, BodyCompositionOptions options)
    {
        _request = request;
        _heightCm = ResolveHeightCm(request);
        _weightKg = ResolveWeightKg(request);
        _goal = ParseFitnessGoal(request.Goal);
        _gender = ParseGender(request.Gender);
        _dateOfBirth = request.Age.HasValue ? DateTime.UtcNow.Date.AddYears(-request.Age.Value) : null;
        _measurement = BuildOnboardingMeasurement(request, _gender, _heightCm, _weightKg);
        _bodyFat = BodyFatAssessor.Assess(
            _gender,
            _dateOfBirth,
            _heightCm,
            _measurement is null ? Array.Empty<BodyMeasurement>() : new[] { _measurement },
            DateTime.UtcNow,
            options);

        // Yag Kaybi secildiyse ve olcumlere gore yag orani dusuk araliktaysa kayit, kullanici uyariyi acikca
        // onaylamadan tamamlanmaz (profildeki kuralla ayni).
        _lowBodyFatConfirmed = _goal.HasValue
            && LowBodyFatGoalGuard.EnsureConfirmed(_goal.Value, _bodyFat, request.ConfirmLowBodyFatWarning);
    }

    /// <summary>
    /// Alanlari dogrular ve hesaplar. Boy her zaman zorunludur; <paramref name="requireWeight"/> true ise kilo da
    /// (eski istemcilerin kaydi kilosuz gonderebilmesi icin kayitta zorunlu degildir).
    /// </summary>
    /// <exception cref="AppValidationException">Zorunlu alan eksik veya olcumler tutarsizsa.</exception>
    /// <exception cref="LowBodyFatConfirmationRequiredException">Dusuk yag orani uyarisi onaylanmadiysa.</exception>
    public static FitnessOnboarding Prepare(FitnessProfileRequest request, BodyCompositionOptions options, bool requireWeight = false)
    {
        if (request.HeightUnit.Equals("cm", StringComparison.OrdinalIgnoreCase) && request.HeightCm is null)
        {
            throw new AppValidationException(nameof(request.HeightCm), "Boy (cm) alani zorunludur.");
        }

        if (request.HeightUnit.Equals("ft_in", StringComparison.OrdinalIgnoreCase) && request.HeightFeet is null)
        {
            throw new AppValidationException(nameof(request.HeightFeet), "Boy (ft/in) alani zorunludur.");
        }

        if (requireWeight && request.Weight is null)
        {
            throw new AppValidationException(nameof(request.Weight), "Kilo alani zorunludur.");
        }

        return new FitnessOnboarding(request, options);
    }

    /// <summary>Profilde fitness alanlari doldurulmus mu (yalnizca alisveris icin kayit olanlarda boy bostur).</summary>
    public static bool IsCompleted(UserProfile? profile) => profile?.HeightCm is not null;

    /// <summary>Hesaplanan fitness alanlarini profile yazar (kaydetmez).</summary>
    public void ApplyTo(UserProfile profile)
    {
        profile.DateOfBirth = _dateOfBirth;
        profile.Gender = _gender;
        profile.HeightCm = _heightCm;
        profile.WeightKg = _weightKg;
        profile.Goal = _goal;
        profile.ExperienceLevel = ParseExperienceLevel(_request.TrainingExperience);
        profile.ActivityLevel = ParseActivityLevel(_request.ActivityLevel);
        profile.TrainingLocation = ParseTrainingLocation(_request.TrainingLocation);
        profile.TrainingDaysPerWeek = _request.TrainingDays;
        profile.UnitSystem = _request.HeightUnit.Equals("ft_in", StringComparison.OrdinalIgnoreCase)
            || _request.WeightUnit.Equals("lb", StringComparison.OrdinalIgnoreCase)
            ? UnitSystem.Imperial
            : UnitSystem.Metric;
    }

    /// <summary>
    /// Ekipmanlari, aktif hedefi (UserGoal), ilk kilo/olcum kaydini ve beslenme hedefini (NutritionGoal) ekler veya
    /// gunceller. Profil (<see cref="ApplyTo"/> uygulanmis) ve UserId hazir olmalidir. SaveChanges cagirmaz; plan
    /// uretim isini (PersonalizationJob) cagiran ekler.
    /// </summary>
    public async Task PersistAsync(IUnitOfWork unitOfWork, INutritionCalculator nutritionCalculator, UserProfile profile, CancellationToken cancellationToken)
    {
        var userId = profile.UserId;
        var now = DateTime.UtcNow;

        var equipmentIds = (_request.EquipmentIds ?? new List<long>()).Distinct().ToList();
        var equipmentRepo = unitOfWork.Repository<UserEquipment>();
        var existingEquipment = await equipmentRepo.GetQueryable(asNoTracking: false)
            .Where(e => e.UserId == userId)
            .ToListAsync(cancellationToken);
        foreach (var item in existingEquipment.Where(e => !equipmentIds.Contains(e.EquipmentId)))
        {
            equipmentRepo.Remove(item);
        }

        var newEquipment = equipmentIds
            .Where(id => existingEquipment.All(e => e.EquipmentId != id))
            .Select(equipmentId => new UserEquipment { UserId = userId, EquipmentId = equipmentId, CreatedAt = now })
            .ToList();
        if (newEquipment.Count > 0)
        {
            await equipmentRepo.AddRangeAsync(newEquipment, cancellationToken);
        }

        // Hedef kilo sorulmaz; hedefe, boya ve kiloya gore otomatik hesaplanir (bkz. TargetWeightCalculator).
        // Kullanici sonradan profilden degistirebilir.
        profile.TargetWeightKg ??= TargetWeightCalculator.Calculate(
            profile.Goal ?? DefaultGoalType, profile.WeightKg, profile.HeightCm, profile.ExperienceLevel);

        var goalRepo = unitOfWork.Repository<UserGoal>();
        var userGoal = await goalRepo.GetQueryable(asNoTracking: false)
            .Where(g => g.UserId == userId && g.IsActive)
            .OrderByDescending(g => g.Id)
            .FirstOrDefaultAsync(cancellationToken);
        if (userGoal is null)
        {
            userGoal = new UserGoal { UserId = userId, IsActive = true, CreatedAt = now };
            await goalRepo.AddAsync(userGoal, cancellationToken);
        }
        else
        {
            userGoal.UpdatedAt = now;
        }

        userGoal.GoalType = profile.Goal ?? DefaultGoalType;
        userGoal.StartingWeightKg = profile.WeightKg;
        userGoal.CurrentWeightKg = profile.WeightKg;
        userGoal.TargetWeightKg = profile.TargetWeightKg;
        userGoal.LowBodyFatWarningAcknowledgedAt = _lowBodyFatConfirmed ? now : null;

        if (_measurement is not null)
        {
            // Onboarding'de cevre/yag orani girildiyse kilo dahil tam bir olcum kaydi olarak saklanir.
            _measurement.UserId = userId;
            await unitOfWork.Repository<BodyMeasurement>().AddAsync(_measurement, cancellationToken);
        }
        else if (profile.WeightKg is not null)
        {
            // Onboarding'deki kilo, kilo gecmisinin (ilerleme grafigi) ilk noktasidir.
            await WeightTracking.UpsertDailyWeightAsync(
                unitOfWork, userId, DateOnly.FromDateTime(now), profile.WeightKg.Value, profile.HeightCm, cancellationToken);
        }

        var nutrition = nutritionCalculator.Calculate(profile, _bodyFat);
        var nutritionRepo = unitOfWork.Repository<NutritionGoal>();
        var nutritionGoal = await nutritionRepo.GetQueryable(asNoTracking: false)
            .FirstOrDefaultAsync(g => g.UserId == userId, cancellationToken);
        if (nutritionGoal is null)
        {
            nutritionGoal = new NutritionGoal { UserId = userId, CreatedAt = now };
            await nutritionRepo.AddAsync(nutritionGoal, cancellationToken);
        }
        else
        {
            nutritionGoal.UpdatedAt = now;
        }

        nutritionGoal.DailyCalories = nutrition.DailyCalories;
        nutritionGoal.ProteinTarget = nutrition.ProteinTarget;
        nutritionGoal.CarbsTarget = nutrition.CarbsTarget;
        nutritionGoal.FatTarget = nutrition.FatTarget;
        nutritionGoal.WaterTargetMl = nutrition.WaterTargetMl;
    }

    private static decimal? ResolveHeightCm(FitnessProfileRequest request)
    {
        if (request.HeightUnit.Equals("ft_in", StringComparison.OrdinalIgnoreCase))
        {
            if (request.HeightFeet is null) return null;
            var totalInches = (request.HeightFeet.Value * 12) + (request.HeightInches ?? 0);
            return Math.Round(totalInches * 2.54m, 2);
        }

        return request.HeightCm;
    }

    private static decimal? ResolveWeightKg(FitnessProfileRequest request)
    {
        if (request.Weight is null) return null;

        return request.WeightUnit.Equals("lb", StringComparison.OrdinalIgnoreCase)
            ? Math.Round(request.Weight.Value * 0.453592m, 2)
            : request.Weight;
    }

    private static Gender? ParseGender(string? genderStr)
    {
        if (string.IsNullOrWhiteSpace(genderStr))
            return null;

        return genderStr.ToLowerInvariant() switch
        {
            "male" => Gender.Male,
            "female" => Gender.Female,
            "other" => Gender.Other,
            _ => null,
        };
    }

    /// <summary>
    /// Onboarding hedef kodunu ana hedeflerden birine cevirir; eski kodlar (improve_fitness, maintain_weight,
    /// improve_endurance) <see cref="FitnessGoals.Normalize(FitnessGoal)"/> ile eslenir.
    /// </summary>
    private static FitnessGoal? ParseFitnessGoal(string? goalStr)
    {
        if (string.IsNullOrWhiteSpace(goalStr))
            return null;

        FitnessGoal? goal = goalStr.ToLowerInvariant() switch
        {
            "lose_weight" => FitnessGoal.LoseWeight,
            "build_muscle" => FitnessGoal.BuildMuscle,
            "get_stronger" => FitnessGoal.GetStronger,
            "improve_fitness" => FitnessGoal.ImproveFitness,
            "maintain_weight" => FitnessGoal.MaintainWeight,
            "improve_endurance" => FitnessGoal.ImproveEndurance,
            _ => null,
        };

        return goal.HasValue ? FitnessGoals.Normalize(goal.Value) : null;
    }

    /// <summary>
    /// Onboarding'de yag orani veya cevre olcumu girildiyse olcum satirini hazirlar (UserId sonradan atanir).
    /// Hicbiri girilmediyse null; o durumda yalnizca gunluk kilo kaydi olusturulur.
    /// </summary>
    private static BodyMeasurement? BuildOnboardingMeasurement(FitnessProfileRequest request, Gender? gender, decimal? heightCm, decimal? weightKg)
    {
        if (request.BodyFatPercentage is null && request.NeckCm is null && request.WaistCm is null && request.HipCm is null)
        {
            return null;
        }

        if (request.NeckCm is not null && request.WaistCm is not null && request.WaistCm <= request.NeckCm)
        {
            throw new AppValidationException(nameof(request.WaistCm), "Bel çevresi boyun çevresinden büyük olmalıdır.");
        }

        var estimate = BodyFatEstimator.EstimateUsNavy(gender, heightCm, request.NeckCm, request.WaistCm, request.HipCm);
        var now = DateTime.UtcNow;
        return new BodyMeasurement
        {
            RecordedAt = now,
            WeightKg = weightKg,
            BodyFatPercentage = request.BodyFatPercentage,
            EstimatedBodyFatPercentage = estimate,
            BodyFatEstimationMethod = estimate is null ? null : BodyFatEstimator.UsNavyMethod,
            NeckCm = request.NeckCm,
            WaistCm = request.WaistCm,
            HipCm = request.HipCm,
            BMI = weightKg is not null && heightCm is > 0
                ? Math.Round(weightKg.Value / (heightCm.Value / 100m * (heightCm.Value / 100m)), 1)
                : null,
            CreatedAt = now,
        };
    }

    private static ExperienceLevel? ParseExperienceLevel(string? levelStr)
    {
        if (string.IsNullOrWhiteSpace(levelStr))
            return null;

        return levelStr.ToLowerInvariant() switch
        {
            "beginner" => ExperienceLevel.Beginner,
            "intermediate" => ExperienceLevel.Intermediate,
            "advanced" => ExperienceLevel.Advanced,
            _ => null,
        };
    }

    private static ActivityLevel? ParseActivityLevel(string? activityStr)
    {
        if (string.IsNullOrWhiteSpace(activityStr))
            return null;

        return activityStr.ToLowerInvariant() switch
        {
            "sedentary" => ActivityLevel.Sedentary,
            "light" => ActivityLevel.LightlyActive,
            "moderate" => ActivityLevel.ModeratelyActive,
            "active" => ActivityLevel.VeryActive,
            "athlete" => ActivityLevel.ExtraActive,
            _ => null,
        };
    }

    private static TrainingLocation? ParseTrainingLocation(string? locationStr)
    {
        if (string.IsNullOrWhiteSpace(locationStr))
            return null;

        return locationStr.ToLowerInvariant() switch
        {
            "gym" => TrainingLocation.Gym,
            "home" => TrainingLocation.Home,
            "outdoor" => TrainingLocation.Outdoor,
            _ => null,
        };
    }
}
