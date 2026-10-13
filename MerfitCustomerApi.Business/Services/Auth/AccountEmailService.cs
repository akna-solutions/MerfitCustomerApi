using MerfitCustomerApi.Business.Common;
using MerfitCustomerApi.Business.Dtos.Auth;
using MerfitCustomerApi.Business.Interfaces.Services;
using MerfitCustomerApi.Domain.Entities;
using MerfitCustomerApi.Domain.Entities.Enums;
using MerfitCustomerApi.Domain.Exceptions;
using MerfitCustomerApi.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MerfitCustomerApi.Business.Services.Auth;

/// <summary>
/// <see cref="IAccountEmailService"/> implementasyonu.
///
/// Guvenlik kurallari:
/// - Kod 6 haneli kriptografik rastgele sayidir; veritabaninda yalnizca SHA-256 ozeti tutulur (bkz. AccountTokenGenerator).
/// - Kod sureli ve tek kullanimliktir; kullanici ve amac basina ayni anda yalnizca en son gonderilen kod gecerlidir.
/// - Kod basina en fazla MaxCodeAttempts deneme yapilir. Her deneme, kod karsilastirilmadan once sayaci artirip
///   kaydeder (AttemptCount concurrency token); paralel isteklerle sinir asilamaz. UsedAt da concurrency token'dir.
/// - Kod, uretildigi andaki e-posta adresine baglidir; kullanici e-postasini degistirirse eski kod gecersizdir.
/// - Sifremi unuttum, hesap olsun olmasin ayni yaniti doner. Saglayici hic yapilandirilmamissa bu kontrol hesap
///   aranmadan once yapilir; boylece 503 yaniti da hesabin varligini aciga cikarmaz.
/// - Kod, parola ve e-posta adresleri loglanmaz.
/// </summary>
public class AccountEmailService : IAccountEmailService
{
    public const string ForgotPasswordGenericMessage =
        "Bu e-posta adresiyle kayıtlı bir hesap varsa şifre sıfırlama kodu gönderdik. Gelen kutunu ve spam klasörünü kontrol et.";
    public const string InvalidCodeMessage = "Kod hatalı. Lütfen e-postadaki 6 haneli kodu kontrol et.";
    public const string NoActiveCodeMessage = "Geçerli bir kod yok. Yeni kod iste.";
    public const string ExpiredCodeMessage = "Kodun süresi dolmuş. Yeni kod iste.";
    public const string TooManyAttemptsMessage = "Çok fazla hatalı deneme yaptın. Yeni kod iste.";
    public const string PasswordResetSuccessMessage = "Şifren güncellendi. Yeni şifrenle giriş yapabilirsin.";
    public const string ResendCooldownMessage = "Yeni bir e-posta istemeden önce biraz beklemelisin.";

    private readonly IUnitOfWork _unitOfWork;
    private readonly IEmailSender _emailSender;
    private readonly AccountEmailOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<AccountEmailService> _logger;

    public AccountEmailService(
        IUnitOfWork unitOfWork,
        IEmailSender emailSender,
        IOptions<AccountEmailOptions> options,
        TimeProvider timeProvider,
        ILogger<AccountEmailService> logger)
    {
        _unitOfWork = unitOfWork;
        _emailSender = emailSender;
        _options = options.Value;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    private DateTime UtcNow => _timeProvider.GetUtcNow().UtcDateTime;

    public async Task<bool> TrySendVerificationAfterRegistrationAsync(ApplicationUser user, string? firstName, string? ipAddress, CancellationToken cancellationToken = default)
    {
        try
        {
            await IssueAndSendAsync(user, firstName, AccountTokenPurpose.EmailVerification, ipAddress, cancellationToken);
            return true;
        }
        catch (EmailDeliveryException)
        {
            // Kayit tamamlandi; e-posta gonderilemedi. Ayrinti ResendEmailSender'da loglandi.
            // Kullanici dogrulama ekranindan tekrar isteyebilir; uygulama yanittaki bayragi gosterir.
            return false;
        }
    }

    public async Task<ResendVerificationEmailResponse> ResendVerificationEmailAsync(long userId, string? ipAddress, CancellationToken cancellationToken = default)
    {
        var user = await GetActiveUserAsync(userId, cancellationToken)
            ?? throw new UnauthorizedException("Oturumun geçersiz. Lütfen tekrar giriş yap.");

        if (user.EmailConfirmed)
        {
            return new ResendVerificationEmailResponse { AlreadyVerified = true };
        }

        var wait = await GetCooldownAsync(user.Id, AccountTokenPurpose.EmailVerification, cancellationToken);
        if (wait is { } retryAfter)
        {
            throw new TooManyRequestsException(ResendCooldownMessage, retryAfter);
        }

        var profile = await _unitOfWork.Repository<UserProfile>().FirstOrDefaultAsync(p => p.UserId == user.Id, cancellationToken);
        await IssueAndSendAsync(user, profile?.FirstName, AccountTokenPurpose.EmailVerification, ipAddress, cancellationToken);

        return new ResendVerificationEmailResponse
        {
            Sent = true,
            ResendAvailableInSeconds = (int)Math.Ceiling(_options.ResendCooldown.TotalSeconds),
        };
    }

    public async Task<ConfirmEmailResponse> ConfirmEmailAsync(long userId, string code, CancellationToken cancellationToken = default)
    {
        var user = await GetActiveUserAsync(userId, cancellationToken)
            ?? throw new UnauthorizedException("Oturumun geçersiz. Lütfen tekrar giriş yap.");

        if (user.EmailConfirmed)
        {
            return new ConfirmEmailResponse { Email = user.Email, EmailConfirmed = true };
        }

        var accountToken = await ConsumeAttemptAsync(user, AccountTokenPurpose.EmailVerification, code, cancellationToken);
        var now = UtcNow;

        accountToken.UsedAt = now;
        accountToken.UpdatedAt = now;
        user.EmailConfirmed = true;
        user.UpdatedAt = now;
        _unitOfWork.Repository<UserAccountToken>().Update(accountToken);
        _unitOfWork.Repository<ApplicationUser>().Update(user);

        await SaveConsumingTokenAsync(cancellationToken);
        _logger.LogInformation("E-posta dogrulandi. UserId: {UserId}", user.Id);

        return new ConfirmEmailResponse { Email = user.Email, EmailConfirmed = true };
    }

    public async Task<AccountActionResponse> RequestPasswordResetAsync(string email, string? ipAddress, CancellationToken cancellationToken = default)
    {
        var generic = new AccountActionResponse { Message = ForgotPasswordGenericMessage };

        // Hesap aranmadan once: yapilandirma eksikse herkes ayni 503'u alir.
        if (!_emailSender.IsConfigured)
        {
            _logger.LogError("Sifre sifirlama e-postasi gonderilemiyor: Resend:ApiKey tanimli degil.");
            throw new EmailDeliveryException(Email.ResendEmailSender.NotConfiguredMessage, isConfigurationError: true);
        }

        var normalizedEmail = (email ?? string.Empty).Trim().ToUpperInvariant();
        if (normalizedEmail.Length == 0)
        {
            return generic;
        }

        var user = await _unitOfWork.Repository<ApplicationUser>()
            .FirstOrDefaultAsync(u => u.NormalizedEmail == normalizedEmail && u.DeletedAt == null, cancellationToken);
        if (user is null || !user.IsActive)
        {
            _logger.LogInformation("Sifre sifirlama istegi: uygun hesap yok; genel yanit donuldu.");
            return generic;
        }

        if (await GetCooldownAsync(user.Id, AccountTokenPurpose.PasswordReset, cancellationToken) is not null)
        {
            // Hesap kesfini onlemek icin bekleme suresinde de ayni genel yanit doner; e-posta gonderilmez.
            _logger.LogInformation("Sifre sifirlama istegi bekleme suresinde; e-posta gonderilmedi. UserId: {UserId}", user.Id);
            return generic;
        }

        var profile = await _unitOfWork.Repository<UserProfile>().FirstOrDefaultAsync(p => p.UserId == user.Id, cancellationToken);
        await IssueAndSendAsync(user, profile?.FirstName, AccountTokenPurpose.PasswordReset, ipAddress, cancellationToken);

        return generic;
    }

    public async Task VerifyPasswordResetCodeAsync(string email, string code, CancellationToken cancellationToken = default)
    {
        var user = await FindUserForResetAsync(email, cancellationToken);
        await ConsumeAttemptAsync(user, AccountTokenPurpose.PasswordReset, code, cancellationToken);
    }

    public async Task<AccountActionResponse> ResetPasswordAsync(ResetPasswordRequest request, string? ipAddress, CancellationToken cancellationToken = default)
    {
        if (request.NewPassword != request.ConfirmPassword)
        {
            throw new AppValidationException(nameof(request.ConfirmPassword), "Şifreler eşleşmiyor.");
        }

        if (request.NewPassword.Length is < 6 or > 100)
        {
            throw new AppValidationException(nameof(request.NewPassword), "Şifre 6-100 karakter arasında olmalıdır.");
        }

        var user = await FindUserForResetAsync(request.Email, cancellationToken);
        var accountToken = await ConsumeAttemptAsync(user, AccountTokenPurpose.PasswordReset, request.Code, cancellationToken);
        var now = UtcNow;

        accountToken.UsedAt = now;
        accountToken.UpdatedAt = now;
        _unitOfWork.Repository<UserAccountToken>().Update(accountToken);

        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.NewPassword);
        // Kod bu adrese gonderildi ve dogru girildi: adresin sahibi oldugu kanitlandi.
        user.EmailConfirmed = true;
        user.UpdatedAt = now;
        _unitOfWork.Repository<ApplicationUser>().Update(user);

        // Ayni kullanicinin bekleyen diger sifirlama kodlari da gecersiz olur.
        await RevokeActiveTokensAsync(user.Id, AccountTokenPurpose.PasswordReset, exceptTokenId: accountToken.Id, now, cancellationToken);

        // Tum oturumlar kapatilir: tum aktif refresh token'lar iptal edilir. (Access token'lar kendi
        // sureleri dolana kadar - Jwt:AccessTokenExpirationMinutes - gecerli kalir; bkz. rapor.)
        var refreshTokens = await _unitOfWork.Repository<UserRefreshToken>()
            .GetQueryable(asNoTracking: false)
            .Where(t => t.UserId == user.Id && !t.IsRevoked)
            .ToListAsync(cancellationToken);
        foreach (var refreshToken in refreshTokens)
        {
            refreshToken.IsRevoked = true;
            refreshToken.RevokedAt = now;
            refreshToken.RevokedByIp = ipAddress;
            refreshToken.UpdatedAt = now;
            _unitOfWork.Repository<UserRefreshToken>().Update(refreshToken);
        }

        await SaveConsumingTokenAsync(cancellationToken);
        _logger.LogInformation("Sifre sifirlandi; {Count} oturum kapatildi. UserId: {UserId}", refreshTokens.Count, user.Id);

        await TrySendPasswordChangedNoticeAsync(user, cancellationToken);

        return new AccountActionResponse { Message = PasswordResetSuccessMessage };
    }

    /// <summary>
    /// Yeni kod uretir, e-postayi gonderir ve ancak gonderim basariliysa ayni turdeki eski kodlari iptal eder.
    /// Gonderim basarisizsa yeni kod silinir (bekleme suresini tuketmez, eski kod calismaya devam eder).
    /// </summary>
    private async Task IssueAndSendAsync(ApplicationUser user, string? firstName, AccountTokenPurpose purpose, string? ipAddress, CancellationToken cancellationToken)
    {
        var now = UtcNow;
        var lifetime = purpose == AccountTokenPurpose.EmailVerification
            ? _options.EmailVerificationTokenLifetime
            : _options.PasswordResetTokenLifetime;
        var (code, codeHash) = AccountTokenGenerator.CreateCode(user.Id, purpose);

        var accountToken = new UserAccountToken
        {
            UserId = user.Id,
            Purpose = purpose,
            TokenHash = codeHash,
            NormalizedEmail = user.NormalizedEmail,
            ExpiresAt = now.Add(lifetime),
            RequestedByIp = ipAddress,
            CreatedAt = now,
        };
        var tokenRepository = _unitOfWork.Repository<UserAccountToken>();
        await tokenRepository.AddAsync(accountToken, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        var email = purpose == AccountTokenPurpose.EmailVerification
            ? AccountEmailTemplates.EmailVerification(user.Email, firstName, code, lifetime)
            : AccountEmailTemplates.PasswordReset(user.Email, firstName, code, lifetime);

        try
        {
            // Idempotency anahtari koda ozeldir: yeniden denemeler ayni e-postayi ikinci kez gondermez.
            await _emailSender.SendAsync(email, $"account-{purpose}-{accountToken.Id}", cancellationToken);
        }
        catch (EmailDeliveryException)
        {
            tokenRepository.Remove(accountToken);
            await _unitOfWork.SaveChangesAsync(CancellationToken.None);
            throw;
        }

        await RevokeActiveTokensAsync(user.Id, purpose, exceptTokenId: accountToken.Id, now, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("{Purpose} e-postasi gonderildi. UserId: {UserId}", purpose, user.Id);
    }

    private async Task RevokeActiveTokensAsync(long userId, AccountTokenPurpose purpose, long exceptTokenId, DateTime now, CancellationToken cancellationToken)
    {
        var activeTokens = await _unitOfWork.Repository<UserAccountToken>()
            .GetQueryable(asNoTracking: false)
            .Where(t => t.UserId == userId && t.Purpose == purpose && t.Id != exceptTokenId
                && t.UsedAt == null && t.RevokedAt == null && t.ExpiresAt > now)
            .ToListAsync(cancellationToken);

        foreach (var activeToken in activeTokens)
        {
            activeToken.RevokedAt = now;
            activeToken.UpdatedAt = now;
            _unitOfWork.Repository<UserAccountToken>().Update(activeToken);
        }
    }

    /// <summary>
    /// Hesap ve tur basina bekleme suresi: son e-postadan bu yana <see cref="AccountEmailOptions.ResendCooldown"/>
    /// gecmediyse veya son bir saatte <see cref="AccountEmailOptions.MaxEmailsPerHour"/> e-posta gonderildiyse
    /// kalan sureyi doner; aksi halde null.
    /// </summary>
    private async Task<TimeSpan?> GetCooldownAsync(long userId, AccountTokenPurpose purpose, CancellationToken cancellationToken)
    {
        var now = UtcNow;
        var windowStart = now.AddHours(-1);
        var recent = await _unitOfWork.Repository<UserAccountToken>()
            .GetQueryable()
            .Where(t => t.UserId == userId && t.Purpose == purpose && t.CreatedAt > windowStart)
            .Select(t => t.CreatedAt)
            .ToListAsync(cancellationToken);

        if (recent.Count == 0)
        {
            return null;
        }

        var sinceLast = now - recent.Max();
        if (sinceLast < _options.ResendCooldown)
        {
            return _options.ResendCooldown - sinceLast;
        }

        if (_options.MaxEmailsPerHour > 0 && recent.Count >= _options.MaxEmailsPerHour)
        {
            var oldestInWindow = recent.OrderByDescending(t => t).Skip(_options.MaxEmailsPerHour - 1).First();
            return oldestInWindow.AddHours(1) - now;
        }

        return null;
    }

    /// <summary>
    /// Sifre sifirlama icin hesabi e-postadan bulur. Hesap yoksa "gecerli kod yok" hatasi doner: kod hic
    /// gonderilmemis bir hesapla ayni yanit oldugu icin hesabin varligini aciga cikarmaz.
    /// </summary>
    private async Task<ApplicationUser> FindUserForResetAsync(string? email, CancellationToken cancellationToken)
    {
        var normalizedEmail = (email ?? string.Empty).Trim().ToUpperInvariant();
        var user = normalizedEmail.Length == 0
            ? null
            : await _unitOfWork.Repository<ApplicationUser>()
                .GetQueryable(asNoTracking: false)
                .FirstOrDefaultAsync(u => u.NormalizedEmail == normalizedEmail && u.DeletedAt == null, cancellationToken);

        if (user is null || !user.IsActive)
        {
            throw new AppValidationException("code", NoActiveCodeMessage);
        }

        return user;
    }

    /// <summary>
    /// Kullanicinin bu amactaki en son kodunu bulur, bir deneme hakkini kaydederek tuketir ve ancak ondan sonra
    /// kodu karsilastirir. Kod dogruysa takip edilen (tracked) kaydi doner; cagiran UsedAt'i isaretleyip kaydeder.
    /// </summary>
    private async Task<UserAccountToken> ConsumeAttemptAsync(ApplicationUser user, AccountTokenPurpose purpose, string? code, CancellationToken cancellationToken)
    {
        var normalizedCode = AccountTokenGenerator.Normalize(code)
            ?? throw new AppValidationException("code", "Kod 6 haneli olmalıdır.");

        var now = UtcNow;
        var accountToken = await _unitOfWork.Repository<UserAccountToken>()
            .GetQueryable(asNoTracking: false)
            .Where(t => t.UserId == user.Id && t.Purpose == purpose && t.UsedAt == null && t.RevokedAt == null)
            .OrderByDescending(t => t.CreatedAt)
            .ThenByDescending(t => t.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (accountToken is null || accountToken.NormalizedEmail != user.NormalizedEmail)
        {
            throw new AppValidationException("code", NoActiveCodeMessage);
        }

        if (accountToken.ExpiresAt <= now)
        {
            throw new AppValidationException("code", ExpiredCodeMessage);
        }

        if (accountToken.AttemptCount >= _options.MaxCodeAttempts)
        {
            throw new AppValidationException("code", TooManyAttemptsMessage);
        }

        // Deneme hakki, kod karsilastirilmadan once kaydedilir. Es zamanli bir deneme sayaci once artirdiysa
        // bu istek kodu hic karsilastirmadan reddedilir.
        accountToken.AttemptCount++;
        accountToken.UpdatedAt = now;
        _unitOfWork.Repository<UserAccountToken>().Update(accountToken);
        try
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new AppValidationException("code", InvalidCodeMessage);
        }

        var expectedHash = AccountTokenGenerator.Hash(user.Id, purpose, normalizedCode);
        if (!AccountTokenGenerator.HashEquals(accountToken.TokenHash, expectedHash))
        {
            _logger.LogInformation("Hatali {Purpose} kodu denendi ({Attempt}/{Max}). UserId: {UserId}",
                purpose, accountToken.AttemptCount, _options.MaxCodeAttempts, user.Id);
            throw new AppValidationException("code", accountToken.AttemptCount >= _options.MaxCodeAttempts
                ? TooManyAttemptsMessage
                : InvalidCodeMessage);
        }

        return accountToken;
    }

    private async Task<ApplicationUser?> GetActiveUserAsync(long userId, CancellationToken cancellationToken)
    {
        var user = await _unitOfWork.Repository<ApplicationUser>().GetByIdAsync(userId, cancellationToken);
        return user is { IsActive: true, DeletedAt: null } ? user : null;
    }

    private async Task SaveConsumingTokenAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Ayni kod es zamanli baska bir istekte kullanildi.
            throw new AppValidationException("code", NoActiveCodeMessage);
        }
    }

    private async Task TrySendPasswordChangedNoticeAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        try
        {
            var profile = await _unitOfWork.Repository<UserProfile>().FirstOrDefaultAsync(p => p.UserId == user.Id, cancellationToken);
            var notice = AccountEmailTemplates.PasswordChanged(user.Email, profile?.FirstName);
            await _emailSender.SendAsync(notice, $"account-PasswordChanged-{user.Id}-{UtcNow.Ticks}", cancellationToken);
        }
        catch (EmailDeliveryException)
        {
            // Bilgilendirme e-postasidir; sifre zaten degisti. Hata ResendEmailSender'da loglandi.
        }
    }
}
