using MerfitCustomerApi.Business.Dtos.Customer.Profile;
using MerfitCustomerApi.Business.Interfaces.Services;
using MerfitCustomerApi.Domain.Entities;
using MerfitCustomerApi.Domain.Entities.Enums;
using MerfitCustomerApi.Domain.Exceptions;
using MerfitCustomerApi.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace MerfitCustomerApi.Business.Services.Profile;

/// <summary>
/// IProfileService'in varsayilan implementasyonu. Mevcut ApplicationUser/UserProfile/UserGoal/
/// UserPreference/UserNotificationSetting/UserPrivacySetting/UserEquipment/WorkoutSession/UserStreak
/// entity'lerini oldugu gibi kullanir, yeni entity eklemez.
/// </summary>
public class ProfileService : IProfileService
{
    private static readonly string[] WeekdayCodes = { "Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun" };

    private readonly IUnitOfWork _unitOfWork;

    public ProfileService(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<CustomerProfileResponse> GetProfileAsync(long userId, CancellationToken cancellationToken = default)
    {
        var user = await _unitOfWork.Repository<ApplicationUser>().GetByIdAsync(userId, cancellationToken);
        var profile = await _unitOfWork.Repository<UserProfile>()
            .FirstOrDefaultAsync(p => p.UserId == userId, cancellationToken);
        if (user is null || profile is null)
        {
            throw new NotFoundException(nameof(UserProfile), userId);
        }

        return await BuildResponseAsync(user, profile, cancellationToken);
    }

    public async Task<CustomerProfileResponse> UpdateProfileAsync(long userId, UpdateProfileRequest request, CancellationToken cancellationToken = default)
    {
        var user = await _unitOfWork.Repository<ApplicationUser>().GetByIdAsync(userId, cancellationToken);
        var profile = await _unitOfWork.Repository<UserProfile>()
            .FirstOrDefaultAsync(p => p.UserId == userId, cancellationToken);
        if (user is null || profile is null)
        {
            throw new NotFoundException(nameof(UserProfile), userId);
        }

        await ApplyIdentityChangesAsync(user, profile, request, cancellationToken);
        ApplyBodyAndGoalChanges(profile, request);
        ApplyTrainingPreferenceChanges(profile, request);

        _unitOfWork.Repository<ApplicationUser>().Update(user);
        _unitOfWork.Repository<UserProfile>().Update(profile);

        if (request.TargetWeight.HasValue)
        {
            await UpdateActiveGoalTargetWeightAsync(userId, request.TargetWeight.Value, cancellationToken);
        }

        if (request.UnitSystem is not null)
        {
            await UpdateUnitSystemPreferenceAsync(userId, request.UnitSystem, cancellationToken);
        }

        if (request.Appearance is not null)
        {
            await UpdateAppearancePreferenceAsync(userId, request.Appearance, cancellationToken);
        }

        if (request.Notifications is not null)
        {
            await UpdateNotificationSettingsAsync(userId, request.Notifications, cancellationToken);
        }

        if (request.Privacy is not null)
        {
            await UpdatePrivacySettingsAsync(userId, request.Privacy, cancellationToken);
        }

        try
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Yukaridaki AnyAsync kontrolu ile bu SaveChanges arasinda baska bir istek ayni
            // kullanici adini almis olabilir (race condition); UserProfileConfiguration'daki
            // veritabani unique index'i burada devreye girer.
            throw new ConflictException("Bu kullanici adi zaten kullaniliyor.");
        }

        return await BuildResponseAsync(user, profile, cancellationToken);
    }

    private async Task ApplyIdentityChangesAsync(ApplicationUser user, UserProfile profile, UpdateProfileRequest request, CancellationToken cancellationToken)
    {
        if (request.FirstName is not null) profile.FirstName = request.FirstName.Trim();
        if (request.LastName is not null) profile.LastName = request.LastName.Trim();

        if (request.Username is not null && !string.Equals(request.Username.Trim(), profile.Username, StringComparison.OrdinalIgnoreCase))
        {
            var normalizedUsername = request.Username.Trim().ToLowerInvariant();
            var taken = await _unitOfWork.Repository<UserProfile>()
                .AnyAsync(p => p.Username == normalizedUsername && p.UserId != user.Id, cancellationToken);
            if (taken)
            {
                throw new ConflictException("Bu kullanici adi zaten kullaniliyor.");
            }
            profile.Username = normalizedUsername;
        }

        if (request.Email is not null && !string.Equals(request.Email.Trim(), user.Email, StringComparison.OrdinalIgnoreCase))
        {
            var normalizedEmail = request.Email.Trim().ToUpperInvariant();
            var taken = await _unitOfWork.Repository<ApplicationUser>()
                .AnyAsync(u => u.NormalizedEmail == normalizedEmail && u.Id != user.Id, cancellationToken);
            if (taken)
            {
                throw new ConflictException("Bu e-posta adresi ile kayitli baska bir hesap var.");
            }
            user.Email = request.Email.Trim();
            user.NormalizedEmail = normalizedEmail;
            user.UserName = request.Email.Trim();
            user.NormalizedUserName = normalizedEmail;
            user.EmailConfirmed = false;
        }

        if (request.DateOfBirth is not null && DateOnly.TryParse(request.DateOfBirth, out var dob))
        {
            profile.DateOfBirth = dob.ToDateTime(TimeOnly.MinValue);
        }

        if (request.Gender is not null)
        {
            profile.Gender = MapGenderToDomain(request.Gender);
        }
    }

    private static void ApplyBodyAndGoalChanges(UserProfile profile, UpdateProfileRequest request)
    {
        if (request.Height.HasValue) profile.HeightCm = request.Height.Value;
        if (request.Weight.HasValue) profile.WeightKg = request.Weight.Value;

        if (request.Goal is not null && TryMapGoalToDomain(request.Goal, out var goal))
        {
            profile.Goal = goal;
        }

        if (request.ExperienceLevel is not null && TryMapExperienceToDomain(request.ExperienceLevel, out var experience))
        {
            profile.ExperienceLevel = experience;
        }
    }

    private static void ApplyTrainingPreferenceChanges(UserProfile profile, UpdateProfileRequest request)
    {
        if (request.WorkoutDurationMin.HasValue)
        {
            profile.WorkoutDurationMin = request.WorkoutDurationMin.Value;
        }

        // NOT: UserProfile yalnizca haftalik gun SAYISINI saklar; hangi spesifik gunlerin
        // secildigi ayri bir tabloda tutulmadigindan burada sadece uzunluk kalici olur.
        if (request.TrainingDays is not null)
        {
            profile.TrainingDaysPerWeek = request.TrainingDays.Count;
        }
    }

    private async Task UpdateActiveGoalTargetWeightAsync(long userId, decimal targetWeight, CancellationToken cancellationToken)
    {
        var goal = await _unitOfWork.Repository<UserGoal>()
            .GetQueryable(asNoTracking: false)
            .FirstOrDefaultAsync(g => g.UserId == userId && g.IsActive, cancellationToken);

        if (goal is not null)
        {
            goal.TargetWeightKg = targetWeight;
            _unitOfWork.Repository<UserGoal>().Update(goal);
        }
        else
        {
            await _unitOfWork.Repository<UserGoal>().AddAsync(new UserGoal
            {
                UserId = userId,
                GoalType = FitnessGoal.MaintainWeight,
                TargetWeightKg = targetWeight,
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
            }, cancellationToken);
        }
    }

    private async Task UpdateUnitSystemPreferenceAsync(long userId, string unitSystem, CancellationToken cancellationToken)
    {
        if (!Enum.TryParse<UnitSystem>(unitSystem, ignoreCase: true, out var parsed))
        {
            return;
        }

        var profile = await _unitOfWork.Repository<UserProfile>()
            .GetQueryable(asNoTracking: false)
            .FirstOrDefaultAsync(p => p.UserId == userId, cancellationToken);
        if (profile is not null)
        {
            profile.UnitSystem = parsed;
        }

        var preference = await GetOrCreatePreferenceAsync(userId, cancellationToken);
        preference.UnitSystem = parsed;
    }

    private async Task UpdateAppearancePreferenceAsync(long userId, string appearance, CancellationToken cancellationToken)
    {
        if (!Enum.TryParse<Theme>(appearance, ignoreCase: true, out var parsed))
        {
            return;
        }

        var preference = await GetOrCreatePreferenceAsync(userId, cancellationToken);
        preference.Theme = parsed;
    }

    public async Task<ThemePreferenceResponse> GetThemePreferenceAsync(long userId, CancellationToken cancellationToken = default)
    {
        var preference = await _unitOfWork.Repository<UserPreference>()
            .FirstOrDefaultAsync(p => p.UserId == userId, cancellationToken);

        return new ThemePreferenceResponse
        {
            ThemeMode = (preference?.Theme ?? Theme.System).ToString(),
        };
    }

    public async Task<ThemePreferenceResponse> UpdateThemePreferenceAsync(long userId, UpdateThemePreferenceRequest request, CancellationToken cancellationToken = default)
    {
        if (!Enum.TryParse<Theme>(request.ThemeMode, ignoreCase: true, out var parsed))
        {
            throw new AppValidationException(
                nameof(request.ThemeMode),
                "ThemeMode yalnizca 'System', 'Light' veya 'Dark' olabilir.");
        }

        var preference = await GetOrCreatePreferenceAsync(userId, cancellationToken);
        preference.Theme = parsed;

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new ThemePreferenceResponse { ThemeMode = parsed.ToString() };
    }

    private async Task<UserPreference> GetOrCreatePreferenceAsync(long userId, CancellationToken cancellationToken)
    {
        var preference = await _unitOfWork.Repository<UserPreference>()
            .GetQueryable(asNoTracking: false)
            .FirstOrDefaultAsync(p => p.UserId == userId, cancellationToken);

        if (preference is null)
        {
            preference = new UserPreference { UserId = userId, Language = "tr", Theme = Theme.System, CreatedAt = DateTime.UtcNow };
            await _unitOfWork.Repository<UserPreference>().AddAsync(preference, cancellationToken);
        }
        else
        {
            _unitOfWork.Repository<UserPreference>().Update(preference);
        }

        return preference;
    }

    private async Task UpdateNotificationSettingsAsync(long userId, UpdateNotificationSettingsRequest request, CancellationToken cancellationToken)
    {
        var settings = await _unitOfWork.Repository<UserNotificationSetting>()
            .GetQueryable(asNoTracking: false)
            .FirstOrDefaultAsync(s => s.UserId == userId, cancellationToken);

        if (settings is null)
        {
            settings = new UserNotificationSetting { UserId = userId, CreatedAt = DateTime.UtcNow };
            await _unitOfWork.Repository<UserNotificationSetting>().AddAsync(settings, cancellationToken);
        }
        else
        {
            _unitOfWork.Repository<UserNotificationSetting>().Update(settings);
        }

        if (request.WorkoutReminders.HasValue) settings.WorkoutReminders = request.WorkoutReminders.Value;
        if (request.DailyGoalReminder.HasValue) settings.DailyGoalReminder = request.DailyGoalReminder.Value;
        if (request.StreakReminder.HasValue) settings.StreakReminder = request.StreakReminder.Value;
        if (request.ProgressUpdates.HasValue) settings.ProgressUpdates = request.ProgressUpdates.Value;
        if (request.LeaderboardUpdates.HasValue) settings.LeaderboardUpdates = request.LeaderboardUpdates.Value;
        if (request.ProductUpdates.HasValue) settings.ProductUpdates = request.ProductUpdates.Value;
    }

    private async Task UpdatePrivacySettingsAsync(long userId, UpdatePrivacySettingsRequest request, CancellationToken cancellationToken)
    {
        var settings = await _unitOfWork.Repository<UserPrivacySetting>()
            .GetQueryable(asNoTracking: false)
            .FirstOrDefaultAsync(s => s.UserId == userId, cancellationToken);

        if (settings is null)
        {
            settings = new UserPrivacySetting
            {
                UserId = userId,
                ProfileVisibleOnLeaderboard = true,
                ShareWorkoutStatistics = true,
                PersonalizedRecommendations = true,
                CreatedAt = DateTime.UtcNow,
            };
            await _unitOfWork.Repository<UserPrivacySetting>().AddAsync(settings, cancellationToken);
        }
        else
        {
            _unitOfWork.Repository<UserPrivacySetting>().Update(settings);
        }

        if (request.ProfileVisibleOnLeaderboard.HasValue) settings.ProfileVisibleOnLeaderboard = request.ProfileVisibleOnLeaderboard.Value;
        if (request.ShareWorkoutStatistics.HasValue) settings.ShareWorkoutStatistics = request.ShareWorkoutStatistics.Value;
        if (request.PersonalizedRecommendations.HasValue) settings.PersonalizedRecommendations = request.PersonalizedRecommendations.Value;
    }

    private async Task<CustomerProfileResponse> BuildResponseAsync(ApplicationUser user, UserProfile profile, CancellationToken cancellationToken)
    {
        var activeGoal = await _unitOfWork.Repository<UserGoal>()
            .FirstOrDefaultAsync(g => g.UserId == user.Id && g.IsActive, cancellationToken);

        var preference = await _unitOfWork.Repository<UserPreference>()
            .FirstOrDefaultAsync(p => p.UserId == user.Id, cancellationToken);

        var notificationSettings = await _unitOfWork.Repository<UserNotificationSetting>()
            .FirstOrDefaultAsync(s => s.UserId == user.Id, cancellationToken);

        var privacySettings = await _unitOfWork.Repository<UserPrivacySetting>()
            .FirstOrDefaultAsync(s => s.UserId == user.Id, cancellationToken);

        var equipmentCount = await _unitOfWork.Repository<UserEquipment>()
            .GetQueryable()
            .CountAsync(ue => ue.UserId == user.Id, cancellationToken);

        var workoutsCount = await _unitOfWork.Repository<WorkoutSession>()
            .GetQueryable()
            .CountAsync(s => s.UserId == user.Id && s.Status == WorkoutSessionStatus.Completed, cancellationToken);

        var streak = await _unitOfWork.Repository<UserStreak>()
            .FirstOrDefaultAsync(s => s.UserId == user.Id, cancellationToken);

        var age = profile.DateOfBirth.HasValue
            ? Math.Max(0, DateTime.UtcNow.Year - profile.DateOfBirth.Value.Year -
                (DateTime.UtcNow.DayOfYear < profile.DateOfBirth.Value.DayOfYear ? 1 : 0))
            : 0;

        return new CustomerProfileResponse
        {
            FirstName = profile.FirstName,
            LastName = profile.LastName,
            Username = profile.Username,
            Email = user.Email,
            DateOfBirth = profile.DateOfBirth?.ToString("yyyy-MM-dd") ?? string.Empty,
            Gender = MapGenderToDisplay(profile.Gender),
            Age = age,
            Height = profile.HeightCm ?? 0,
            Weight = profile.WeightKg ?? 0,
            TargetWeight = activeGoal?.TargetWeightKg ?? profile.WeightKg ?? 0,
            Goal = MapGoalToDisplay(profile.Goal),
            ExperienceLevel = profile.ExperienceLevel?.ToString().ToUpperInvariant() ?? "BEGINNER",
            Equipment = DeriveEquipmentCategory(equipmentCount),
            WorkoutDurationMin = profile.WorkoutDurationMin ?? 45,
            TrainingDays = BuildTrainingDaysDisplay(profile.TrainingDaysPerWeek ?? 3),
            UnitSystem = profile.UnitSystem.ToString().ToLowerInvariant(),
            Appearance = (preference?.Theme ?? Theme.System).ToString().ToLowerInvariant(),
            Notifications = notificationSettings is null
                ? new CustomerNotificationSettingsDto
                {
                    WorkoutReminders = true,
                    DailyGoalReminder = true,
                    StreakReminder = true,
                    ProgressUpdates = true,
                    LeaderboardUpdates = true,
                    ProductUpdates = false,
                }
                : new CustomerNotificationSettingsDto
                {
                    WorkoutReminders = notificationSettings.WorkoutReminders,
                    DailyGoalReminder = notificationSettings.DailyGoalReminder,
                    StreakReminder = notificationSettings.StreakReminder,
                    ProgressUpdates = notificationSettings.ProgressUpdates,
                    LeaderboardUpdates = notificationSettings.LeaderboardUpdates,
                    ProductUpdates = notificationSettings.ProductUpdates,
                },
            Privacy = privacySettings is null
                ? new CustomerPrivacySettingsDto
                {
                    ProfileVisibleOnLeaderboard = true,
                    ShareWorkoutStatistics = true,
                    PersonalizedRecommendations = true,
                }
                : new CustomerPrivacySettingsDto
                {
                    ProfileVisibleOnLeaderboard = privacySettings.ProfileVisibleOnLeaderboard,
                    ShareWorkoutStatistics = privacySettings.ShareWorkoutStatistics,
                    PersonalizedRecommendations = privacySettings.PersonalizedRecommendations,
                },
            Stats = new CustomerProfileStatsDto
            {
                Workouts = workoutsCount,
                Streak = streak?.CurrentStreak ?? 0,
            },
        };
    }

    /// <summary>Kullanicinin secili ekipman sayisindan kaba bir kategori turetir (bkz. DTO'daki not).</summary>
    private static string DeriveEquipmentCategory(int equipmentCount) => equipmentCount switch
    {
        0 => "BODYWEIGHT",
        1 => "MINIMAL_EQUIPMENT",
        <= 3 => "HOME_GYM",
        _ => "FULL_GYM",
    };

    /// <summary>Sadece dogru SAYIYI yansitan, sabit bir haftalik gun dagilimi uretir (bkz. DTO'daki not).</summary>
    private static List<string> BuildTrainingDaysDisplay(int count)
    {
        var clamped = Math.Clamp(count, 0, 7);
        return WeekdayCodes.Take(clamped).ToList();
    }

    private static Gender? MapGenderToDomain(string display) => display switch
    {
        "Erkek" => Gender.Male,
        "Kadın" => Gender.Female,
        "Diğer" => Gender.Other,
        _ => null, // "Belirtmek istemiyorum" veya taninmayan deger
    };

    private static string MapGenderToDisplay(Gender? gender) => gender switch
    {
        Gender.Male => "Erkek",
        Gender.Female => "Kadın",
        Gender.Other => "Diğer",
        _ => "Belirtmek istemiyorum",
    };

    private static bool TryMapGoalToDomain(string display, out FitnessGoal goal)
    {
        switch (display)
        {
            case "LOSE_WEIGHT": goal = FitnessGoal.LoseWeight; return true;
            case "BUILD_MUSCLE": goal = FitnessGoal.BuildMuscle; return true;
            case "MAINTAIN_WEIGHT": goal = FitnessGoal.MaintainWeight; return true;
            case "IMPROVE_ENDURANCE": goal = FitnessGoal.ImproveEndurance; return true;
            case "GET_STRONGER": goal = FitnessGoal.GetStronger; return true;
            case "GENERAL_FITNESS": goal = FitnessGoal.ImproveFitness; return true;
            default: goal = default; return false;
        }
    }

    private static string MapGoalToDisplay(FitnessGoal? goal) => goal switch
    {
        FitnessGoal.LoseWeight => "LOSE_WEIGHT",
        FitnessGoal.BuildMuscle => "BUILD_MUSCLE",
        FitnessGoal.MaintainWeight => "MAINTAIN_WEIGHT",
        FitnessGoal.ImproveEndurance => "IMPROVE_ENDURANCE",
        FitnessGoal.GetStronger => "GET_STRONGER",
        FitnessGoal.ImproveFitness => "GENERAL_FITNESS",
        _ => "GENERAL_FITNESS",
    };

    private static bool TryMapExperienceToDomain(string display, out ExperienceLevel level)
    {
        switch (display)
        {
            case "BEGINNER": level = ExperienceLevel.Beginner; return true;
            case "INTERMEDIATE": level = ExperienceLevel.Intermediate; return true;
            case "ADVANCED": level = ExperienceLevel.Advanced; return true;
            default: level = default; return false;
        }
    }
}
