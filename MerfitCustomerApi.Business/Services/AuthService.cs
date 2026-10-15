using System.Text.RegularExpressions;
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
using Microsoft.Extensions.Options;

namespace MerfitCustomerApi.Business.Services.Auth;

/// <summary>
/// IAuthService'in varsayilan implementasyonu. ApplicationUser + UserProfile kaydini
/// tek bir islemde (transaction) olusturur ve JWT token cifti uretir.
/// </summary>
public class AuthService : IAuthService
{
    /// <summary>
    /// Kayit sirasinda UserProfile.Goal bos birakildiginde UserGoal.GoalType icin kullanilan varsayilan hedef:
    /// uc ana hedeften kalori acigi uygulamayan Kas Gelistirme (bkz. FitnessGoals.Default).
    /// </summary>
    private const FitnessGoal DefaultGoalType = FitnessGoals.Default;

    /// <summary>ExternalLogin.Provider degeri.</summary>
    public const string GoogleProvider = "Google";

    private const string GoogleAccountAlreadyRegisteredMessage = "Bu Google hesabı zaten kayıtlı. Google ile giriş yap.";

    private readonly IUnitOfWork _unitOfWork;
    private readonly ITokenService _tokenService;
    private readonly INutritionCalculator _nutritionCalculator;
    private readonly JwtSettings _jwtSettings;
    private readonly IAccountEmailService _accountEmailService;
    private readonly IGoogleIdTokenValidator _googleIdTokenValidator;
    private readonly BodyCompositionOptions _bodyCompositionOptions;

    public AuthService(
        IUnitOfWork unitOfWork,
        ITokenService tokenService,
        INutritionCalculator nutritionCalculator,
        IOptions<JwtSettings> jwtSettings,
        IAccountEmailService accountEmailService,
        IGoogleIdTokenValidator googleIdTokenValidator,
        IOptions<BodyCompositionOptions>? bodyCompositionOptions = null)
    {
        _bodyCompositionOptions = bodyCompositionOptions?.Value ?? new BodyCompositionOptions();
        _unitOfWork = unitOfWork;
        _tokenService = tokenService;
        _nutritionCalculator = nutritionCalculator;
        _jwtSettings = jwtSettings.Value;
        _accountEmailService = accountEmailService;
        _googleIdTokenValidator = googleIdTokenValidator;
    }

    public async Task<AuthResponse> RegisterAsync(RegisterRequest request, string? ipAddress)
    {
        if (request.Password != request.ConfirmPassword)
        {
            throw new AppValidationException(nameof(request.ConfirmPassword), "Parolalar eslesmiyor.");
        }

        ValidateProfileFields(request);

        var email = request.Email.Trim();
        var normalizedEmail = email.ToUpperInvariant();
        var emailTaken = await _unitOfWork.Repository<ApplicationUser>().AnyAsync(u => u.NormalizedEmail == normalizedEmail);
        if (emailTaken)
        {
            throw new ConflictException("Bu e-posta adresi ile kayitli bir hesap zaten mevcut.");
        }

        var (user, response) = await CreateAccountAsync(
            request, email, BCrypt.Net.BCrypt.HashPassword(request.Password), emailConfirmed: false, externalLogin: null, ipAddress);

        // Hesap olustuktan sonra dogrulama e-postasi gonderilir. Gonderim hatasi kaydi bozmaz; yanittaki
        // VerificationEmailSent=false ile uygulama kullaniciya bildirir ve tekrar gondermeyi sunar.
        response.VerificationEmailSent = await _accountEmailService.TrySendVerificationAfterRegistrationAsync(user, request.FirstName.Trim(), ipAddress);
        return response;
    }

    /// <summary>
    /// ApplicationUser + UserProfile ve kisisellestirme altyapisini tek bir transaction'da olusturur ve token cifti
    /// uretir. E-posta/sifre kaydi ile Google kaydinin ortak govdesidir; e-posta benzersizligini cagiran kontrol eder.
    /// </summary>
    private async Task<(ApplicationUser User, AuthResponse Response)> CreateAccountAsync(
        AccountProfileRequest request,
        string email,
        string passwordHash,
        bool emailConfirmed,
        GoogleIdentity? externalLogin,
        string? ipAddress)
    {
        var normalizedEmail = email.ToUpperInvariant();
        var userRepo = _unitOfWork.Repository<ApplicationUser>();

        // Kullanici adi karsilastirmalari her zaman kucuk harfe cevrilip yapilir/saklanir (bkz.
        // ProfileService.ApplyIdentityChangesAsync ve AuthService.FindUserByEmailOrUsernameAsync
        // ile ayni normallesme kurali). Bu kontrol hizli/dostane bir hata mesaji icin var ama tek
        // basina yeterli degildir; UserProfileConfiguration'daki veritabani unique index'i, es
        // zamanli (race condition) istekler icin asil garantiyi saglar (bkz. asagidaki commit
        // sonrasi DbUpdateException yakalama).
        var normalizedUsername = request.Username.Trim().ToLowerInvariant();
        var usernameTaken = await _unitOfWork.Repository<UserProfile>()
            .AnyAsync(p => p.Username == normalizedUsername);
        if (usernameTaken)
        {
            throw new ConflictException("Bu kullanici adi zaten kullaniliyor.");
        }

        var heightCm = ResolveHeightCm(request);
        var weightKg = ResolveWeightKg(request);
        var unitSystem = request.HeightUnit.Equals("ft_in", StringComparison.OrdinalIgnoreCase)
            || request.WeightUnit.Equals("lb", StringComparison.OrdinalIgnoreCase)
            ? UnitSystem.Imperial
            : UnitSystem.Metric;

        var firstName = request.FirstName.Trim();
        var lastName = request.LastName.Trim();

        var equipmentIds = (request.EquipmentIds ?? new List<long>()).Distinct().ToList();

        // Onboarding'de girilen opsiyonel vucut olcumleri. Yag Kaybi secildiyse ve bu olcumlere gore yag orani
        // dusuk araliktaysa kayit, kullanici uyariyi acikca onaylamadan tamamlanmaz (profildeki kuralla ayni).
        var goal = ParseFitnessGoal(request.Goal);
        var gender = ParseGender(request.Gender);
        DateTime? dateOfBirth = request.Age.HasValue ? DateTime.UtcNow.Date.AddYears(-request.Age.Value) : null;
        var onboardingMeasurement = BuildOnboardingMeasurement(request, gender, heightCm, weightKg);
        var bodyFat = BodyFatAssessor.Assess(
            gender,
            dateOfBirth,
            heightCm,
            onboardingMeasurement is null ? Array.Empty<BodyMeasurement>() : new[] { onboardingMeasurement },
            DateTime.UtcNow,
            _bodyCompositionOptions);
        var lowBodyFatConfirmed = goal.HasValue
            && LowBodyFatGoalGuard.EnsureConfirmed(goal.Value, bodyFat, request.ConfirmLowBodyFatWarning);

        await _unitOfWork.BeginTransactionAsync();
        var user = new ApplicationUser
        {
            Email = email,
            NormalizedEmail = normalizedEmail,
            UserName = email,
            NormalizedUserName = normalizedEmail,
            PasswordHash = passwordHash,
            EmailConfirmed = emailConfirmed,
            PhoneNumberConfirmed = false,
            LockoutEnabled = true,
            Role = UserRole.User,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
        };

        await userRepo.AddAsync(user);
        await _unitOfWork.SaveChangesAsync();

        var profile = new UserProfile
        {
            UserId = user.Id,
            FirstName = firstName,
            LastName = lastName,
            Username = normalizedUsername,
            DateOfBirth = dateOfBirth,
            Gender = gender,
            HeightCm = heightCm,
            WeightKg = weightKg,
            Goal = goal,
            ExperienceLevel = ParseExperienceLevel(request.TrainingExperience),
            ActivityLevel = ParseActivityLevel(request.ActivityLevel),
            TrainingLocation = ParseTrainingLocation(request.TrainingLocation),
            TrainingDaysPerWeek = request.TrainingDays,
            UnitSystem = unitSystem,
            CreatedAt = DateTime.UtcNow,
        };

        await _unitOfWork.Repository<UserProfile>().AddAsync(profile);

        if (externalLogin is not null)
        {
            await _unitOfWork.Repository<ExternalLogin>().AddAsync(NewGoogleLogin(user.Id, externalLogin));
        }

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
        // Hedef kilo kayitta sorulmaz; hedefe, boya ve kiloya gore otomatik hesaplanir
        // (bkz. TargetWeightCalculator). Kullanici sonradan profilden degistirebilir.
        profile.TargetWeightKg ??= TargetWeightCalculator.Calculate(
            profile.Goal ?? DefaultGoalType, profile.WeightKg, profile.HeightCm, profile.ExperienceLevel);

        var userGoal = new UserGoal
        {
            UserId = user.Id,
            GoalType = profile.Goal ?? DefaultGoalType,
            StartingWeightKg = profile.WeightKg,
            CurrentWeightKg = profile.WeightKg,
            TargetWeightKg = profile.TargetWeightKg,
            IsActive = true,
            LowBodyFatWarningAcknowledgedAt = lowBodyFatConfirmed ? DateTime.UtcNow : null,
            CreatedAt = DateTime.UtcNow,
        };

        await _unitOfWork.Repository<UserGoal>().AddAsync(userGoal);

        if (onboardingMeasurement is not null)
        {
            // Onboarding'de cevre/yag orani girildiyse kilo dahil tam bir olcum kaydi olarak saklanir.
            onboardingMeasurement.UserId = user.Id;
            await _unitOfWork.Repository<BodyMeasurement>().AddAsync(onboardingMeasurement);
        }
        else if (profile.WeightKg is not null)
        {
            // Kayittaki kilo, kilo gecmisinin (ilerleme grafigi) ilk noktasidir.
            await WeightTracking.UpsertDailyWeightAsync(
                _unitOfWork, user.Id, DateOnly.FromDateTime(DateTime.UtcNow), profile.WeightKg.Value, profile.HeightCm, CancellationToken.None);
        }

        var nutritionCalculation = _nutritionCalculator.Calculate(profile, bodyFat);
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

        var (accessToken, refreshTokenValue, expiresAt) = await AddSessionTokensAsync(user, ipAddress);

        try
        {
            // CommitTransactionAsync basarisiz olursa transaction'i kendi icinde zaten geri alir
            // (bkz. UnitOfWork.CommitTransactionAsync); burada yalnizca hatayi anlamli bir
            // ConflictException'a ceviriyoruz.
            await _unitOfWork.CommitTransactionAsync();
        }
        catch (DbUpdateException ex) when (DbExceptionClassifier.IsUniqueViolation(ex, out var constraintName))
        {
            // Yukarida yaptigimiz AnyAsync kontrolu ile bu commit arasinda baska bir istek ayni
            // kullanici adini (veya ayni Google hesabini) alip kaydetmis olabilir (race condition).
            // Veritabani unique index'leri burada devreye girer; kullaniciya net bir hata donduruyoruz.
            if (constraintName?.Contains(nameof(ExternalLogin), StringComparison.OrdinalIgnoreCase) == true)
            {
                throw new ConflictException(GoogleAccountAlreadyRegisteredMessage);
            }

            throw new ConflictException("Bu kullanici adi zaten kullaniliyor.");
        }

        var response = new AuthResponse
        {
            UserId = user.Id,
            Email = user.Email,
            Name = $"{firstName} {lastName}".Trim(),
            Username = normalizedUsername,
            AccessToken = accessToken,
            RefreshToken = refreshTokenValue,
            AccessTokenExpiresAt = expiresAt,
            PersonalizationStatus = personalizationJob.Status.ToString(),
            EmailConfirmed = user.EmailConfirmed,
            IsNewUser = true,
        };

        return (user, response);
    }

    // ---- Google ile giris / kayit ----

    public async Task<GoogleSignInResponse> GoogleSignInAsync(GoogleSignInRequest request, string? ipAddress, CancellationToken cancellationToken = default)
    {
        var identity = await _googleIdTokenValidator.ValidateAsync(request.IdToken, cancellationToken);
        var user = await FindOrLinkGoogleUserAsync(identity, ipAddress, cancellationToken);

        if (user is null)
        {
            return new GoogleSignInResponse
            {
                RegistrationRequired = true,
                Profile = new GoogleProfileDto
                {
                    Email = identity.Email,
                    FirstName = identity.GivenName,
                    LastName = identity.FamilyName,
                },
            };
        }

        return new GoogleSignInResponse { Auth = await SignInAsync(user, ipAddress) };
    }

    public async Task<AuthResponse> GoogleRegisterAsync(GoogleRegisterRequest request, string? ipAddress, CancellationToken cancellationToken = default)
    {
        var identity = await _googleIdTokenValidator.ValidateAsync(request.IdToken, cancellationToken);

        // Kullanici onboarding'i doldururken hesap baska bir yoldan (ikinci cihaz, cift dokunma) olusmus veya
        // ayni e-postayla baglanabilir bir hesap varsa yeni hesap acilmaz; var olan hesapla giris yapilir.
        var existing = await FindOrLinkGoogleUserAsync(identity, ipAddress, cancellationToken);
        if (existing is not null)
        {
            return await SignInAsync(existing, ipAddress);
        }

        ValidateProfileFields(request);

        var (user, response) = await CreateAccountAsync(
            request, identity.Email, UnusablePasswordHash(), emailConfirmed: identity.EmailVerified, externalLogin: identity, ipAddress);

        if (!user.EmailConfirmed)
        {
            // Google e-postayi dogrulanmis saymiyorsa (nadir; Gmail disi Google hesaplari) e-posta/sifre kaydindaki
            // gibi 6 haneli kod gonderilir ve uygulama kod ekranina gecer.
            response.VerificationEmailSent = await _accountEmailService.TrySendVerificationAfterRegistrationAsync(user, request.FirstName.Trim(), ipAddress, cancellationToken);
        }

        return response;
    }

    /// <summary>
    /// Google kimligine bagli hesabi bulur. Bagli hesap yoksa ve Google e-postayi dogrulanmis sayiyorsa ayni
    /// e-postayla kayitli hesaba Google girisini baglar. Hesap yoksa null doner.
    /// </summary>
    private async Task<ApplicationUser?> FindOrLinkGoogleUserAsync(GoogleIdentity identity, string? ipAddress, CancellationToken cancellationToken)
    {
        var userRepo = _unitOfWork.Repository<ApplicationUser>();

        var link = await _unitOfWork.Repository<ExternalLogin>()
            .FirstOrDefaultAsync(l => l.Provider == GoogleProvider && l.ProviderUserId == identity.Subject);
        if (link is not null)
        {
            var linkedUser = await userRepo.GetByIdAsync(link.UserId);
            if (linkedUser is not null)
            {
                EnsureCanSignIn(linkedUser);
                return linkedUser;
            }
        }

        var normalizedEmail = identity.Email.ToUpperInvariant();
        var user = await userRepo.FirstOrDefaultAsync(u => u.NormalizedEmail == normalizedEmail);
        if (user is null)
        {
            return null;
        }

        if (!identity.EmailVerified)
        {
            // Adresin bu Google hesabina ait oldugu kanitlanmadi; var olan hesaba baglanmaz.
            throw new ConflictException("Bu e-posta adresiyle kayıtlı bir hesap var. E-posta ve şifrenle giriş yap.");
        }

        EnsureCanSignIn(user);

        var now = DateTime.UtcNow;
        await _unitOfWork.Repository<ExternalLogin>().AddAsync(NewGoogleLogin(user.Id, identity));

        if (!user.EmailConfirmed)
        {
            // Hesap e-postasi hic dogrulanmamisti: hesabi bu adresin sahibi olmayan biri acmis olabilir (hesabi
            // onceden ele gecirme). Google adresin sahibini kanitladigi icin hesap ona verilir; eski parola ve acik
            // oturumlar gecersiz olur. Gercek sahip parolayi "Sifremi unuttum" ile yeniden belirleyebilir.
            user.EmailConfirmed = true;
            user.PasswordHash = UnusablePasswordHash();
            user.UpdatedAt = now;
            await RevokeRefreshTokensAsync(user.Id, ipAddress, now, cancellationToken);
        }

        userRepo.Update(user);
        return user;
    }

    private static void EnsureCanSignIn(ApplicationUser user)
    {
        if (!user.IsActive)
        {
            throw new UnauthorizedException("Hesabiniz aktif degil.");
        }
    }

    private async Task RevokeRefreshTokensAsync(long userId, string? ipAddress, DateTime now, CancellationToken cancellationToken)
    {
        var refreshTokenRepo = _unitOfWork.Repository<UserRefreshToken>();
        var refreshTokens = await refreshTokenRepo
            .GetQueryable(asNoTracking: false)
            .Where(t => t.UserId == userId && !t.IsRevoked)
            .ToListAsync(cancellationToken);
        foreach (var refreshToken in refreshTokens)
        {
            refreshToken.IsRevoked = true;
            refreshToken.RevokedAt = now;
            refreshToken.RevokedByIp = ipAddress;
            refreshToken.UpdatedAt = now;
            refreshTokenRepo.Update(refreshToken);
        }
    }

    private static ExternalLogin NewGoogleLogin(long userId, GoogleIdentity identity) => new()
    {
        UserId = userId,
        Provider = GoogleProvider,
        ProviderUserId = identity.Subject,
        Email = identity.Email,
        CreatedAt = DateTime.UtcNow,
    };

    /// <summary>
    /// Parolasi olmayan (yalnizca Google ile giris yapan) hesaplar icin: kimsenin bilmedigi rastgele bir degerin
    /// BCrypt ozeti. PasswordHash zorunlu bir alan oldugu icin bos birakilmaz; parola ile giris her zaman basarisiz olur.
    /// </summary>
    private static string UnusablePasswordHash() =>
        BCrypt.Net.BCrypt.HashPassword(Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)));

    // RegisterRequest.Username dogrulama kurallariyla ayni (3-30 karakter, harf/rakam/alt cizgi).
    private static readonly Regex UsernamePattern = new("^[a-zA-Z0-9_]{3,30}$", RegexOptions.Compiled);

    public async Task<UsernameAvailabilityResponse> CheckUsernameAvailabilityAsync(string? username, CancellationToken cancellationToken = default)
    {
        var normalizedUsername = (username ?? string.Empty).Trim().ToLowerInvariant();
        var response = new UsernameAvailabilityResponse { Username = normalizedUsername };

        if (normalizedUsername.Length is < 3 or > 30)
        {
            response.Message = "Kullanıcı adı 3-30 karakter arasında olmalıdır.";
            return response;
        }

        if (!UsernamePattern.IsMatch(normalizedUsername))
        {
            response.Message = "Kullanıcı adı yalnızca harf, rakam ve alt çizgi (_) içerebilir.";
            return response;
        }

        var taken = await _unitOfWork.Repository<UserProfile>()
            .GetQueryable()
            .AnyAsync(p => p.Username == normalizedUsername, cancellationToken);

        response.Available = !taken;
        response.Message = taken ? "Bu kullanıcı adı zaten kullanılıyor." : null;
        return response;
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

        return await SignInAsync(user, ipAddress);
    }

    /// <summary>
    /// Var olan (aktif) kullanici icin oturum acar: LastLoginAt'i gunceller, token cifti uretir ve kaydeder.
    /// </summary>
    private async Task<AuthResponse> SignInAsync(ApplicationUser user, string? ipAddress)
    {
        var profile = await _unitOfWork.Repository<UserProfile>().FirstOrDefaultAsync(p => p.UserId == user.Id);

        user.LastLoginAt = DateTime.UtcNow;
        _unitOfWork.Repository<ApplicationUser>().Update(user);

        var (accessToken, refreshTokenValue, expiresAt) = await AddSessionTokensAsync(user, ipAddress);
        await _unitOfWork.SaveChangesAsync();

        return new AuthResponse
        {
            UserId = user.Id,
            Email = user.Email,
            Name = profile is not null ? $"{profile.FirstName} {profile.LastName}".Trim() : string.Empty,
            Username = profile?.Username ?? string.Empty,
            AccessToken = accessToken,
            RefreshToken = refreshTokenValue,
            AccessTokenExpiresAt = expiresAt,
            EmailConfirmed = user.EmailConfirmed,
        };
    }

    /// <summary>Access token uretir ve yeni refresh token'i (kaydetmeden) ekler.</summary>
    private async Task<(string AccessToken, string RefreshToken, DateTime ExpiresAt)> AddSessionTokensAsync(ApplicationUser user, string? ipAddress)
    {
        var (accessToken, expiresAt) = _tokenService.GenerateAccessToken(user);
        var refreshTokenValue = _tokenService.GenerateRefreshToken();

        await _unitOfWork.Repository<UserRefreshToken>().AddAsync(new UserRefreshToken
        {
            UserId = user.Id,
            Token = refreshTokenValue,
            ExpiresAt = DateTime.UtcNow.AddDays(_jwtSettings.RefreshTokenExpirationDays),
            CreatedByIp = ipAddress,
            IsRevoked = false,
            IsUsed = false,
            CreatedAt = DateTime.UtcNow,
        });

        return (accessToken, refreshTokenValue, expiresAt);
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

    private static void ValidateProfileFields(AccountProfileRequest request)
    {
        if (request.HeightUnit.Equals("cm", StringComparison.OrdinalIgnoreCase) && request.HeightCm is null)
        {
            throw new AppValidationException(nameof(request.HeightCm), "Boy (cm) alani zorunludur.");
        }

        if (request.HeightUnit.Equals("ft_in", StringComparison.OrdinalIgnoreCase) && request.HeightFeet is null)
        {
            throw new AppValidationException(nameof(request.HeightFeet), "Boy (ft/in) alani zorunludur.");
        }
    }

    private static decimal? ResolveHeightCm(AccountProfileRequest request)
    {
        if (request.HeightUnit.Equals("ft_in", StringComparison.OrdinalIgnoreCase))
        {
            if (request.HeightFeet is null) return null;
            var totalInches = (request.HeightFeet.Value * 12) + (request.HeightInches ?? 0);
            return Math.Round(totalInches * 2.54m, 2);
        }

        return request.HeightCm;
    }

    private static decimal? ResolveWeightKg(AccountProfileRequest request)
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
    /// Onboarding'de yag orani veya cevre olcumu girildiyse kayit icin olcum satirini hazirlar (UserId sonradan
    /// atanir). Hicbiri girilmediyse null; o durumda yalnizca gunluk kilo kaydi olusturulur.
    /// </summary>
    private static BodyMeasurement? BuildOnboardingMeasurement(AccountProfileRequest request, Gender? gender, decimal? heightCm, decimal? weightKg)
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