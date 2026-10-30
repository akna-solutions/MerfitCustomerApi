using System.Text.RegularExpressions;
using MerfitCustomerApi.Business.Common;
using MerfitCustomerApi.Business.Dtos.Auth;
using MerfitCustomerApi.Business.Interfaces.Services;
using MerfitCustomerApi.Business.Services.BodyComposition;
using MerfitCustomerApi.Business.Services.ContentModeration;
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
    private readonly IContentModerationService _contentModeration;

    public AuthService(
        IUnitOfWork unitOfWork,
        ITokenService tokenService,
        INutritionCalculator nutritionCalculator,
        IOptions<JwtSettings> jwtSettings,
        IAccountEmailService accountEmailService,
        IGoogleIdTokenValidator googleIdTokenValidator,
        IOptions<BodyCompositionOptions>? bodyCompositionOptions = null,
        IContentModerationService? contentModeration = null)
    {
        _bodyCompositionOptions = bodyCompositionOptions?.Value ?? new BodyCompositionOptions();
        _contentModeration = contentModeration ?? new ContentModerationService();
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
        // Ad, soyad ve kullanici adi herkese acik alanlardir (profil, liderlik tablosu): uygunsuz icerik varsa hicbir
        // kayit olusturulmadan 400 doner. Hem e-posta/sifre hem Google kaydi buradan gecer.
        _contentModeration.EnsureAllowed(
            (nameof(request.FirstName), request.FirstName, ContentModerationType.FirstName),
            (nameof(request.LastName), request.LastName, ContentModerationType.LastName),
            (nameof(request.Username), request.Username, ContentModerationType.Username));

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

        var firstName = request.FirstName.Trim();
        var lastName = request.LastName.Trim();

        // Yalnizca alisveris icin kayitta fitness alanlari sorulmaz/yok sayilir; aksi halde dogrulama ve dusuk yag
        // orani onayi veritabanina yazmadan once yapilir.
        var shoppingOnly = request.ShoppingOnly == true;
        var fitness = shoppingOnly ? null : FitnessOnboarding.Prepare(request, _bodyCompositionOptions);

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
            UnitSystem = UnitSystem.Metric,
            CreatedAt = DateTime.UtcNow,
        };
        fitness?.ApplyTo(profile);

        await _unitOfWork.Repository<UserProfile>().AddAsync(profile);

        if (externalLogin is not null)
        {
            await _unitOfWork.Repository<ExternalLogin>().AddAsync(NewGoogleLogin(user.Id, externalLogin));
        }

        // --- Kisisellestirme altyapisi (FAZ 1) ---
        // Kayit tamamlandiginda, kisiye ozel antrenman/beslenme programi uretebilmek icin gereken temel kayitlari
        // (UserGoal, NutritionGoal, PersonalizationJob) olustur; plani arka plan islemcisi uretir. Yalnizca
        // alisveris icin kayitta bunlar olusturulmaz: kullanici MB Fit'i actiginda profilini tamamlar
        // (POST /api/profile/fitness) ve ayni kayitlar o zaman olusur.
        PersonalizationJob? personalizationJob = null;
        if (fitness is not null)
        {
            await fitness.PersistAsync(_unitOfWork, _nutritionCalculator, profile, CancellationToken.None);

            personalizationJob = new PersonalizationJob
            {
                UserId = user.Id,
                Status = PersonalizationJobStatus.Pending,
                AttemptCount = 0,
                CreatedAt = DateTime.UtcNow,
            };

            await _unitOfWork.Repository<PersonalizationJob>().AddAsync(personalizationJob);
        }

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
            PersonalizationStatus = personalizationJob?.Status.ToString(),
            EmailConfirmed = user.EmailConfirmed,
            IsNewUser = true,
            FitnessProfileCompleted = fitness is not null,
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

        var moderation = _contentModeration.Check(normalizedUsername, ContentModerationType.Username);
        if (!moderation.IsAllowed)
        {
            response.Message = moderation.RejectionReason;
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
            FitnessProfileCompleted = FitnessOnboarding.IsCompleted(profile),
        };
    }

    // ---- Oturum yenileme / cikis ----

    private const string SessionExpiredMessage = "Oturumunun süresi doldu. Lütfen tekrar giriş yap.";

    public async Task<AuthResponse> RefreshAsync(RefreshTokenRequest request, string? ipAddress, CancellationToken cancellationToken = default)
    {
        var tokenValue = request.RefreshToken?.Trim();
        if (string.IsNullOrEmpty(tokenValue))
        {
            throw new UnauthorizedException(SessionExpiredMessage);
        }

        var refreshTokenRepo = _unitOfWork.Repository<UserRefreshToken>();
        var current = await refreshTokenRepo
            .GetQueryable(asNoTracking: false)
            .FirstOrDefaultAsync(t => t.Token == tokenValue, cancellationToken);

        var now = DateTime.UtcNow;
        if (current is null || current.IsRevoked || current.ExpiresAt <= now)
        {
            throw new UnauthorizedException(SessionExpiredMessage);
        }

        if (current.IsUsed)
        {
            // Yenileme yaniti agda kaybolup uygulama ayni token'la tekrar denediyse kisa bir sure daha kabul edilir;
            // daha sonra eski token ile gelen istek reddedilir.
            var rotatedAt = current.UpdatedAt ?? current.CreatedAt;
            if (now - rotatedAt > TimeSpan.FromSeconds(_jwtSettings.RefreshTokenReuseGraceSeconds))
            {
                throw new UnauthorizedException(SessionExpiredMessage);
            }
        }

        var user = await _unitOfWork.Repository<ApplicationUser>().FirstOrDefaultAsync(u => u.Id == current.UserId);
        if (user is null || !user.IsActive)
        {
            throw new UnauthorizedException(SessionExpiredMessage);
        }

        var profile = await _unitOfWork.Repository<UserProfile>().FirstOrDefaultAsync(p => p.UserId == user.Id);
        var (accessToken, refreshTokenValue, expiresAt) = await AddSessionTokensAsync(user, ipAddress);

        if (!current.IsUsed)
        {
            current.IsUsed = true;
            current.ReplacementToken = refreshTokenValue;
            current.UpdatedAt = now;
            refreshTokenRepo.Update(current);
        }

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
            FitnessProfileCompleted = FitnessOnboarding.IsCompleted(profile),
        };
    }

    public async Task LogoutAsync(RefreshTokenRequest request, string? ipAddress, CancellationToken cancellationToken = default)
    {
        var tokenValue = request.RefreshToken?.Trim();
        if (string.IsNullOrEmpty(tokenValue))
        {
            return;
        }

        var refreshTokenRepo = _unitOfWork.Repository<UserRefreshToken>();
        var current = await refreshTokenRepo
            .GetQueryable(asNoTracking: false)
            .FirstOrDefaultAsync(t => t.Token == tokenValue && !t.IsRevoked, cancellationToken);
        if (current is null)
        {
            return;
        }

        var now = DateTime.UtcNow;
        current.IsRevoked = true;
        current.RevokedAt = now;
        current.RevokedByIp = ipAddress;
        current.UpdatedAt = now;
        refreshTokenRepo.Update(current);
        await _unitOfWork.SaveChangesAsync();
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
}
