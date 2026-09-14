using MerfitCustomerApi.Business.Common;
using MerfitCustomerApi.Business.Dtos.Auth;
using MerfitCustomerApi.Business.Interfaces.Services;
using MerfitCustomerApi.Domain.Entities;
using MerfitCustomerApi.Domain.Entities.Enums;
using MerfitCustomerApi.Domain.Exceptions;
using MerfitCustomerApi.Domain.Interfaces;
using Microsoft.Extensions.Options;

namespace MerfitCustomerApi.Business.Services.Auth;

/// <summary>
/// IAuthService'in varsayilan implementasyonu. ApplicationUser + UserProfile kaydini
/// tek bir islemde (transaction) olusturur ve JWT token cifti uretir.
/// </summary>
public class AuthService : IAuthService
{
    /// <summary>
    /// Kayit sirasinda UserProfile.Goal bos birakildiginde UserGoal.GoalType icin kullanilan
    /// guvenli varsayilan hedef. UserGoal.GoalType (FitnessGoal, non-nullable) alanini,
    /// mevcut kayit davranisini bozmadan doldurabilmek icin "notr" bir hedef secilmistir.
    /// </summary>
    private const FitnessGoal DefaultGoalType = FitnessGoal.MaintainWeight;

    private readonly IUnitOfWork _unitOfWork;
    private readonly ITokenService _tokenService;
    private readonly INutritionCalculator _nutritionCalculator;
    private readonly JwtSettings _jwtSettings;

    public AuthService(
        IUnitOfWork unitOfWork,
        ITokenService tokenService,
        INutritionCalculator nutritionCalculator,
        IOptions<JwtSettings> jwtSettings)
    {
        _unitOfWork = unitOfWork;
        _tokenService = tokenService;
        _nutritionCalculator = nutritionCalculator;
        _jwtSettings = jwtSettings.Value;
    }

    public async Task<AuthResponse> RegisterAsync(RegisterRequest request, string? ipAddress)
    {
        ValidateAccountFields(request);

        var normalizedEmail = request.Email.Trim().ToUpperInvariant();

        var userRepo = _unitOfWork.Repository<ApplicationUser>();
        var emailTaken = await userRepo.AnyAsync(u => u.NormalizedEmail == normalizedEmail);
        if (emailTaken)
        {
            throw new ConflictException("Bu e-posta adresi ile kayitli bir hesap zaten mevcut.");
        }

        var heightCm = ResolveHeightCm(request);
        var weightKg = ResolveWeightKg(request);
        var unitSystem = request.HeightUnit.Equals("ft_in", StringComparison.OrdinalIgnoreCase)
            || request.WeightUnit.Equals("lb", StringComparison.OrdinalIgnoreCase)
            ? UnitSystem.Imperial
            : UnitSystem.Metric;

        var (firstName, lastName) = SplitName(request.Name);

        var equipmentIds = (request.EquipmentIds ?? new List<long>()).Distinct().ToList();

        await _unitOfWork.BeginTransactionAsync();
        var user = new ApplicationUser
        {
            Email = request.Email.Trim(),
            NormalizedEmail = normalizedEmail,
            UserName = request.Email.Trim(),
            NormalizedUserName = normalizedEmail,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password),
            EmailConfirmed = false,
            PhoneNumberConfirmed = false,
            LockoutEnabled = true,
            Role = UserRole.User,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
        };

        await userRepo.AddAsync(user);
        await _unitOfWork.SaveChangesAsync();

        var username = await GenerateUniqueUsernameAsync(request.Email);

        var profile = new UserProfile
        {
            UserId = user.Id,
            FirstName = firstName,
            LastName = lastName,
            Username = username,
            DateOfBirth = request.Age.HasValue
                ? DateTime.UtcNow.Date.AddYears(-request.Age.Value)
                : null,
            Gender = ParseGender(request.Gender),
            HeightCm = heightCm,
            WeightKg = weightKg,
            Goal = ParseFitnessGoal(request.Goal),
            ExperienceLevel = ParseExperienceLevel(request.TrainingExperience),
            ActivityLevel = ParseActivityLevel(request.ActivityLevel),
            TrainingLocation = ParseTrainingLocation(request.TrainingLocation),
            TrainingDaysPerWeek = request.TrainingDays,
            UnitSystem = unitSystem,
            CreatedAt = DateTime.UtcNow,
        };

        await _unitOfWork.Repository<UserProfile>().AddAsync(profile);

        if (equipmentIds.Count > 0)
        {
            var userEquipments = equipmentIds.Select(equipmentId => new UserEquipment
            {
                UserId = user.Id,
                EquipmentId = equipmentId,
                CreatedAt = DateTime.UtcNow,
            });

            await _unitOfWork.Repository<UserEquipment>().AddRangeAsync(userEquipments);
        }

        // --- Kisisellestirme altyapisi (FAZ 1) ---
        // Kayit tamamlandiginda, ileride kisiye ozel antrenman/beslenme programi uretebilmek icin
        // gereken temel kayitlari (UserGoal, NutritionGoal, PersonalizationJob) olustur. Bu asamada
        // gercek bir plan uretilmez; sadece veri altyapisi hazirlanir (bkz. IPersonalizationService
        // ihtiyaci ileride ikinci fazda arka plan islemcisiyle birlikte eklenecektir).
        var userGoal = new UserGoal
        {
            UserId = user.Id,
            GoalType = profile.Goal ?? DefaultGoalType,
            StartingWeightKg = profile.WeightKg,
            CurrentWeightKg = profile.WeightKg,
            TargetWeightKg = profile.TargetWeightKg,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
        };

        await _unitOfWork.Repository<UserGoal>().AddAsync(userGoal);

        var nutritionCalculation = _nutritionCalculator.Calculate(profile);
        var nutritionGoal = new NutritionGoal
        {
            UserId = user.Id,
            DailyCalories = nutritionCalculation.DailyCalories,
            ProteinTarget = nutritionCalculation.ProteinTarget,
            CarbsTarget = nutritionCalculation.CarbsTarget,
            FatTarget = nutritionCalculation.FatTarget,
            WaterTargetMl = nutritionCalculation.WaterTargetMl,
            CreatedAt = DateTime.UtcNow,
        };

        await _unitOfWork.Repository<NutritionGoal>().AddAsync(nutritionGoal);

        var personalizationJob = new PersonalizationJob
        {
            UserId = user.Id,
            Status = PersonalizationJobStatus.Pending,
            AttemptCount = 0,
            CreatedAt = DateTime.UtcNow,
        };

        await _unitOfWork.Repository<PersonalizationJob>().AddAsync(personalizationJob);
        // --- Kisisellestirme altyapisi sonu ---

        var (accessToken, expiresAt) = _tokenService.GenerateAccessToken(user);
        var refreshTokenValue = _tokenService.GenerateRefreshToken();

        var refreshToken = new UserRefreshToken
        {
            UserId = user.Id,
            Token = refreshTokenValue,
            ExpiresAt = DateTime.UtcNow.AddDays(_jwtSettings.RefreshTokenExpirationDays),
            CreatedByIp = ipAddress,
            IsRevoked = false,
            IsUsed = false,
            CreatedAt = DateTime.UtcNow,
        };

        await _unitOfWork.Repository<UserRefreshToken>().AddAsync(refreshToken);

        await _unitOfWork.CommitTransactionAsync();

        return new AuthResponse
        {
            UserId = user.Id,
            Email = user.Email,
            Name = request.Name.Trim(),
            AccessToken = accessToken,
            RefreshToken = refreshTokenValue,
            AccessTokenExpiresAt = expiresAt,
            PersonalizationStatus = personalizationJob.Status.ToString(),
        };
    }

    public async Task<AuthResponse> LoginAsync(LoginRequest request, string? ipAddress)
    {
        var identifier = request.EmailOrUsername.Trim();

        var user = await FindUserByEmailOrUsernameAsync(identifier);

        if (user is null || !BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
        {
            throw new UnauthorizedException("E-posta/kullanici adi veya parola hatali.");
        }

        if (!user.IsActive)
        {
            throw new UnauthorizedException("Hesabiniz aktif degil.");
        }

        var profile = await _unitOfWork.Repository<UserProfile>().FirstOrDefaultAsync(p => p.UserId == user.Id);

        user.LastLoginAt = DateTime.UtcNow;
        _unitOfWork.Repository<ApplicationUser>().Update(user);

        var (accessToken, expiresAt) = _tokenService.GenerateAccessToken(user);
        var refreshTokenValue = _tokenService.GenerateRefreshToken();

        var refreshToken = new UserRefreshToken
        {
            UserId = user.Id,
            Token = refreshTokenValue,
            ExpiresAt = DateTime.UtcNow.AddDays(_jwtSettings.RefreshTokenExpirationDays),
            CreatedByIp = ipAddress,
            IsRevoked = false,
            IsUsed = false,
            CreatedAt = DateTime.UtcNow,
        };

        await _unitOfWork.Repository<UserRefreshToken>().AddAsync(refreshToken);
        await _unitOfWork.SaveChangesAsync();

        return new AuthResponse
        {
            UserId = user.Id,
            Email = user.Email,
            Name = profile is not null ? $"{profile.FirstName} {profile.LastName}".Trim() : string.Empty,
            AccessToken = accessToken,
            RefreshToken = refreshTokenValue,
            AccessTokenExpiresAt = expiresAt,
        };
    }

    /// <summary>
    /// Girilen degeri once e-posta, bulunamazsa kullanici adi (UserProfile.Username) olarak arar.
    /// </summary>
    private async Task<ApplicationUser?> FindUserByEmailOrUsernameAsync(string identifier)
    {
        var userRepo = _unitOfWork.Repository<ApplicationUser>();

        var normalizedEmail = identifier.ToUpperInvariant();
        var user = await userRepo.FirstOrDefaultAsync(u => u.NormalizedEmail == normalizedEmail);
        if (user is not null)
        {
            return user;
        }

        var normalizedUsername = identifier.ToLowerInvariant();
        var profile = await _unitOfWork.Repository<UserProfile>()
            .FirstOrDefaultAsync(p => p.Username == normalizedUsername);

        return profile is null ? null : await userRepo.GetByIdAsync(profile.UserId);
    }

    private static void ValidateAccountFields(RegisterRequest request)
    {
        if (request.Password != request.ConfirmPassword)
        {
            throw new AppValidationException(nameof(request.ConfirmPassword), "Parolalar eslesmiyor.");
        }

        if (request.HeightUnit.Equals("cm", StringComparison.OrdinalIgnoreCase) && request.HeightCm is null)
        {
            throw new AppValidationException(nameof(request.HeightCm), "Boy (cm) alani zorunludur.");
        }

        if (request.HeightUnit.Equals("ft_in", StringComparison.OrdinalIgnoreCase) && request.HeightFeet is null)
        {
            throw new AppValidationException(nameof(request.HeightFeet), "Boy (ft/in) alani zorunludur.");
        }
    }

    private static decimal? ResolveHeightCm(RegisterRequest request)
    {
        if (request.HeightUnit.Equals("ft_in", StringComparison.OrdinalIgnoreCase))
        {
            if (request.HeightFeet is null) return null;
            var totalInches = (request.HeightFeet.Value * 12) + (request.HeightInches ?? 0);
            return Math.Round(totalInches * 2.54m, 2);
        }

        return request.HeightCm;
    }

    private static decimal? ResolveWeightKg(RegisterRequest request)
    {
        if (request.Weight is null) return null;

        return request.WeightUnit.Equals("lb", StringComparison.OrdinalIgnoreCase)
            ? Math.Round(request.Weight.Value * 0.453592m, 2)
            : request.Weight;
    }

    private static (string FirstName, string LastName) SplitName(string fullName)
    {
        var trimmed = fullName.Trim();
        var parts = trimmed.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);

        return parts.Length switch
        {
            0 => (string.Empty, string.Empty),
            1 => (parts[0], string.Empty),
            _ => (parts[0], parts[1]),
        };
    }

    /// <summary>
    /// RN onboarding akisi ayrica bir "kullanici adi" toplamadigindan, e-postanin
    /// yerel kismindan (@'den once) benzersiz bir kullanici adi turetir.
    /// </summary>
    private async Task<string> GenerateUniqueUsernameAsync(string email)
    {
        var baseUsername = email.Split('@')[0].Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(baseUsername))
        {
            baseUsername = "user";
        }

        var profileRepo = _unitOfWork.Repository<UserProfile>();
        var candidate = baseUsername;
        var attempt = 0;

        while (await profileRepo.AnyAsync(p => p.Username == candidate))
        {
            attempt++;
            candidate = $"{baseUsername}{Random.Shared.Next(1000, 9999)}";

            if (attempt > 10)
            {
                candidate = $"{baseUsername}{Guid.NewGuid():N}"[..30];
                break;
            }
        }

        return candidate;
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

    private static FitnessGoal? ParseFitnessGoal(string? goalStr)
    {
        if (string.IsNullOrWhiteSpace(goalStr))
            return null;

        return goalStr.ToLowerInvariant() switch
        {
            "lose_weight" => FitnessGoal.LoseWeight,
            "build_muscle" => FitnessGoal.BuildMuscle,
            "get_stronger" => FitnessGoal.GetStronger,
            "improve_fitness" => FitnessGoal.ImproveFitness,
            "maintain_weight" => FitnessGoal.MaintainWeight,
            "improve_endurance" => FitnessGoal.ImproveEndurance,
            _ => null,
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