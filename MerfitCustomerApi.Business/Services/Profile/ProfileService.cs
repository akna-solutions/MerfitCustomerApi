using MerfitCustomerApi.Business.Common;
using MerfitCustomerApi.Business.Dtos.Customer.Profile;
using MerfitCustomerApi.Business.Interfaces.Services;
using MerfitCustomerApi.Domain.Entities;
using MerfitCustomerApi.Domain.Entities.Enums;
using MerfitCustomerApi.Domain.Exceptions;
using MerfitCustomerApi.Domain.Interfaces;
using System.Globalization;
using Microsoft.EntityFrameworkCore;

namespace MerfitCustomerApi.Business.Services.Profile;

/// <summary>
/// IProfileService'in varsayilan implementasyonu. Mevcut ApplicationUser/UserProfile/UserGoal/
/// UserPreference/UserNotificationSetting/UserPrivacySetting/UserEquipment/WorkoutSession/UserStreak
/// entity'lerini oldugu gibi kullanir, yeni entity eklemez.
/// </summary>
public class ProfileService : IProfileService
{
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

        var planInputsBefore = PlanInputs.From(profile);

        await ApplyIdentityChangesAsync(user, profile, request, cancellationToken);
        ApplyBodyAndGoalChanges(profile, request);
        ApplyTrainingPreferenceChanges(profile, request);

        var planInputsAfter = PlanInputs.From(profile);
        if (planInputsAfter != planInputsBefore)
        {
            if (planInputsAfter.Goal != planInputsBefore.Goal && profile.Goal.HasValue)
            {
                await UpdateActiveGoalTypeAsync(userId, profile.Goal.Value, cancellationToken);
            }

            await EnqueuePlanRegenerationAsync(userId, cancellationToken);
        }

        // NOT: user/profile yukarida AsNoTracking OLMADAN cekildi, yani EF change tracker
        // ikisini de zaten izliyor - buradaki alan atamalari SaveChangesAsync'te otomatik
        // algilanir. Daha once burada ekstra bir Repository.Update(user/profile) cagrisi
        // vardi; DbSet.Update() zaten izlenen bir entity'de TUM alanlari (Username dahil,
        // degismis olsun olmasin) "Modified" isaretledigi icin, kullanici sadece temayi
        // (Appearance) veya baska alakasiz bir alani degistirse bile UPDATE cumlesi
        // Username'i de yeniden yaziyordu - bu da UserProfile.Username uzerindeki veritabani
        // unique index'ine carpip asagidaki catch bloguyla (alakasiz olmasina ragmen) "Bu
        // kullanici adi zaten kullaniliyor" hatasi olarak donuyordu. Update() cagrisi
        // kaldirilinca Username yalnizca GERCEKTEN degistiginde UPDATE'e dahil olur.

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
        catch (DbUpdateException ex) when (DbExceptionClassifier.IsUniqueViolation(ex, out var constraint))
        {
            // Yukaridaki AnyAsync kontrolu ile bu SaveChanges arasinda baska bir istek ayni
            // kullanici adini / e-postayi almis olabilir (race condition); veritabani unique
            // index'i burada devreye girer. Diger veritabani hatalari (orn. gecersiz deger) bu
            // mesajla ORTULMEZ - daha once her DbUpdateException "kullanici adi zaten
            // kullaniliyor" olarak donuyor ve gercek hatayi gizliyordu.
            throw new ConflictException(constraint?.Contains("Email", StringComparison.OrdinalIgnoreCase) == true
                ? "Bu e-posta adresi ile kayitli baska bir hesap var."
                : "Bu kullanici adi zaten kullaniliyor.");
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

        if (request.DateOfBirth is not null
            && DateOnly.TryParseExact(request.DateOfBirth, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dob))
        {
            // Kolon "timestamp with time zone"; Npgsql yalnizca Kind=Utc DateTime yazabilir.
            profile.DateOfBirth = DateTime.SpecifyKind(dob.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);
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

        // Kullanici gunleri tek tek secerse (TrainingDays) secim UserProfile.PreferredTrainingDays'e
        // yazilir ve haftalik gun sayisi bu listeden turetilir. Yalnizca sayi gelirse ve onceki
        // secimle uyusmuyorsa secim temizlenir; plan uretici o durumda varsayilan gun desenini kullanir.
        var requestedDays = (request.TrainingDays ?? new List<string>())
            .Select(code => TrainingDayCodes.TryParseCode(code, out var day) ? (DayOfWeek?)day : null)
            .ToList();
        if (requestedDays.Any(day => day is null))
        {
            throw new AppValidationException(nameof(request.TrainingDays), "Gecersiz antrenman gunu. Gecerli degerler: Mon, Tue, Wed, Thu, Fri, Sat, Sun.");
        }

        var selectedDays = requestedDays.Select(day => day!.Value).Distinct().ToList();
        if (selectedDays.Count > 0)
        {
            profile.PreferredTrainingDays = TrainingDayCodes.Format(selectedDays);
            profile.TrainingDaysPerWeek = selectedDays.Count;
        }
        else if (request.TrainingDaysPerWeek.HasValue)
        {
            profile.TrainingDaysPerWeek = request.TrainingDaysPerWeek.Value;
            if (TrainingDayCodes.ParseStored(profile.PreferredTrainingDays).Count != request.TrainingDaysPerWeek.Value)
            {
                profile.PreferredTrainingDays = null;
            }
        }

        if (request.TrainingLocation is not null
            && Enum.TryParse<TrainingLocation>(request.TrainingLocation, ignoreCase: true, out var location))
        {
            profile.TrainingLocation = location;
        }
    }

    /// <summary>Antrenman programini etkileyen profil alanlari; biri degisirse program yeniden uretilir.</summary>
    private sealed record PlanInputs(
        FitnessGoal? Goal,
        ExperienceLevel? ExperienceLevel,
        int? TrainingDaysPerWeek,
        string? PreferredTrainingDays,
        TrainingLocation? TrainingLocation)
    {
        public static PlanInputs From(UserProfile profile) => new(
            profile.Goal,
            profile.ExperienceLevel,
            profile.TrainingDaysPerWeek,
            profile.PreferredTrainingDays,
            profile.TrainingLocation);
    }

    private async Task UpdateActiveGoalTypeAsync(long userId, FitnessGoal goalType, CancellationToken cancellationToken)
    {
        var goal = await _unitOfWork.Repository<UserGoal>()
            .GetQueryable(asNoTracking: false)
            .FirstOrDefaultAsync(g => g.UserId == userId && g.IsActive, cancellationToken);
        if (goal is not null && goal.GoalType != goalType)
        {
            goal.GoalType = goalType;
            goal.UpdatedAt = DateTime.UtcNow;
        }
    }

    /// <summary>
    /// Kayittaki ile ayni sekilde bir PersonalizationJob kuyruga alir; arka plan islemcisi yeni
    /// workout + nutrition planini uretip eski aktif planlari pasiflestirir. Bekleyen veya islenen
    /// bir is zaten varsa yenisi eklenmez (o is guncel profili okuyacaktir).
    /// </summary>
    private async Task EnqueuePlanRegenerationAsync(long userId, CancellationToken cancellationToken)
    {
        var jobs = _unitOfWork.Repository<PersonalizationJob>();
        var hasOpenJob = await jobs.GetQueryable()
            .AnyAsync(j => j.UserId == userId
                && (j.Status == PersonalizationJobStatus.Pending || j.Status == PersonalizationJobStatus.Processing),
                cancellationToken);
        if (hasOpenJob)
        {
            return;
        }

        await jobs.AddAsync(new PersonalizationJob
        {
            UserId = userId,
            Status = PersonalizationJobStatus.Pending,
            CreatedAt = DateTime.UtcNow,
        }, cancellationToken);
    }

    private async Task UpdateActiveGoalTargetWeightAsync(long userId, decimal targetWeight, CancellationToken cancellationToken)
    {
        var profile = await _unitOfWork.Repository<UserProfile>()
            .GetQueryable(asNoTracking: false)
            .FirstOrDefaultAsync(p => p.UserId == userId, cancellationToken);
        if (profile is not null)
        {
            profile.TargetWeightKg = targetWeight;
        }

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
            // GET /api/profile'in kayitsiz kullaniciya gosterdigi varsayilanlarla baslat; aksi halde tek
            // bir ayari degistirmek digerlerini sessizce kapatirdi.
            settings = new UserNotificationSetting
            {
                UserId = userId,
                WorkoutReminders = true,
                DailyGoalReminder = true,
                StreakReminder = true,
                ProgressUpdates = true,
                LeaderboardUpdates = true,
                ProductUpdates = false,
                CreatedAt = DateTime.UtcNow,
            };
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

        // Kullanici gunlerini kendisi sectiyse o secim, secmediyse aktif planin gunleri gosterilir.
        var preferredDays = TrainingDayCodes.ParseStored(profile.PreferredTrainingDays);
        var trainingDays = preferredDays.Count > 0
            ? preferredDays.Select(TrainingDayCodes.ToCode).ToList()
            : await GetActivePlanTrainingDaysAsync(user.Id, cancellationToken);

        var age = profile.DateOfBirth.HasValue ? CalculateAge(profile.DateOfBirth.Value, DateTime.UtcNow) : 0;

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
            // Hedef kilo kayitta sorulmuyor; belirlenmemisse 0 doner (istemci "Belirlenmedi" gosterir).
            TargetWeight = activeGoal?.TargetWeightKg ?? profile.TargetWeightKg ?? 0,
            Goal = MapGoalToDisplay(profile.Goal),
            ExperienceLevel = profile.ExperienceLevel?.ToString().ToUpperInvariant() ?? "BEGINNER",
            Equipment = DeriveEquipmentCategory(profile.TrainingLocation, equipmentCount),
            TrainingLocation = profile.TrainingLocation?.ToString().ToLowerInvariant(),
            WorkoutDurationMin = profile.WorkoutDurationMin ?? 45,
            TrainingDaysPerWeek = preferredDays.Count > 0 ? preferredDays.Count : profile.TrainingDaysPerWeek ?? trainingDays.Count,
            TrainingDays = trainingDays,
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

    /// <summary>
    /// Kayitta secilen antrenman yeri (TrainingLocation) ve UserEquipment kayitlarindan kaba bir
    /// kategori turetir. Salon secen kullanici ekipman isaretlemese bile FULL_GYM'dir.
    /// </summary>
    private static string DeriveEquipmentCategory(TrainingLocation? location, int equipmentCount) => location switch
    {
        TrainingLocation.Gym => "FULL_GYM",
        TrainingLocation.Outdoor => equipmentCount == 0 ? "BODYWEIGHT" : "MINIMAL_EQUIPMENT",
        _ => equipmentCount switch
        {
            0 => "BODYWEIGHT",
            <= 2 => "MINIMAL_EQUIPMENT",
            _ => "HOME_GYM",
        },
    };

    /// <summary>
    /// Kullanicinin gercek antrenman gunleri: aktif WorkoutPlan'in WorkoutPlanDay kayitlari
    /// (Pazartesi'den Pazar'a sirali). Plan henuz uretilmediyse bos liste doner; gunler
    /// UserProfile'da ayrica saklanmaz.
    /// </summary>
    private async Task<List<string>> GetActivePlanTrainingDaysAsync(long userId, CancellationToken cancellationToken)
    {
        var activePlan = await _unitOfWork.Repository<WorkoutPlan>()
            .GetQueryable()
            .Where(p => p.UserId == userId && p.IsActive)
            .OrderByDescending(p => p.StartDate)
            .FirstOrDefaultAsync(cancellationToken);
        if (activePlan is null)
        {
            return new List<string>();
        }

        var days = await _unitOfWork.Repository<WorkoutPlanDay>()
            .GetQueryable()
            .Where(d => d.WorkoutPlanId == activePlan.Id)
            .Select(d => d.DayOfWeek)
            .ToListAsync(cancellationToken);

        return days
            .Distinct()
            .OrderBy(TrainingDayCodes.MondayFirstIndex)
            .Select(TrainingDayCodes.ToCode)
            .ToList();
    }

    /// <summary>
    /// Tam yas. DayOfYear karsilastirmasi artik yillarda bir gun kayiyordu (orn. 12 Ekim 1996
    /// dogumlu biri 12 Ekim 2026'da 29 gorunuyordu); bu yuzden dogum gunu tarihle karsilastirilir.
    /// </summary>
    private static int CalculateAge(DateTime dateOfBirth, DateTime today)
    {
        var age = today.Year - dateOfBirth.Year;
        if (dateOfBirth.Date > today.Date.AddYears(-age)) age--;
        return Math.Max(0, age);
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
