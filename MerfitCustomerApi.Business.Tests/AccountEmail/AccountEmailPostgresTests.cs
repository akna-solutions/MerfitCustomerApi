using MerfitCustomerApi.Business.Common;
using MerfitCustomerApi.Business.Dtos.Auth;
using MerfitCustomerApi.Business.Services.Auth;
using MerfitCustomerApi.Business.Tests.Notifications;
using MerfitCustomerApi.Domain.Entities;
using MerfitCustomerApi.Domain.Entities.Enums;
using MerfitCustomerApi.Domain.Exceptions;
using MerfitCustomerApi.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace MerfitCustomerApi.Business.Tests.AccountEmail;

/// <summary>
/// Gercek PostgreSQL'de (MERFIT_TEST_POSTGRES) uctan uca: ayni sifre sifirlama koduyla es zamanli isteklerden
/// yalnizca biri sifreyi degistirir; paralel hatali denemeler deneme sinirini asamaz; dogrulama kodu tek kullanimliktir.
/// </summary>
public class AccountEmailPostgresTests
{
    private static AppDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(Environment.GetEnvironmentVariable(PostgresNotificationQueueTests.ConnectionVariable)).Options);

    private static (AccountEmailService Service, AppDbContext Db) CreateService(FakeEmailSender sender)
    {
        var db = CreateContext();
        var service = new AccountEmailService(new UnitOfWork(db), sender, Options.Create(new AccountEmailOptions()), TimeProvider.System, NullLogger<AccountEmailService>.Instance);
        return (service, db);
    }

    private static async Task<ApplicationUser> CreateUserAsync()
    {
        await using var db = CreateContext();
        var email = $"account-{Guid.NewGuid():N}@test.local";
        var user = new ApplicationUser
        {
            Email = email, NormalizedEmail = email.ToUpperInvariant(), UserName = email, NormalizedUserName = email.ToUpperInvariant(),
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("old-password"), IsActive = true, Role = UserRole.User, CreatedAt = DateTime.UtcNow,
        };
        db.ApplicationUsers.Add(user);
        await db.SaveChangesAsync();
        return user;
    }

    [PostgresFact]
    public async Task ConcurrentPasswordResets_WithSameCode_OnlyOneSucceeds()
    {
        var user = await CreateUserAsync();
        var sender = new FakeEmailSender();
        var (requester, requesterDb) = CreateService(sender);
        await using (requesterDb)
        {
            await requester.RequestPasswordResetAsync(user.Email, null);
        }
        var code = FakeEmailSender.ExtractCode(sender.Sent.Single().Email);

        async Task<bool> ResetAsync(string password)
        {
            var (service, db) = CreateService(new FakeEmailSender());
            await using (db)
            {
                try
                {
                    await service.ResetPasswordAsync(new ResetPasswordRequest { Email = user.Email, Code = code, NewPassword = password, ConfirmPassword = password }, null);
                    return true;
                }
                catch (AppValidationException)
                {
                    return false;
                }
            }
        }

        var results = await Task.WhenAll(Enumerable.Range(0, 5).Select(i => Task.Run(() => ResetAsync($"new-password-{i}"))));

        Assert.Equal(1, results.Count(r => r));
        await using var check = CreateContext();
        var row = await check.UserAccountTokens.SingleAsync(t => t.UserId == user.Id);
        Assert.NotNull(row.UsedAt);
        Assert.NotEqual(code, row.TokenHash);
        Assert.True(row.AttemptCount <= 5);
    }

    [PostgresFact]
    public async Task ParallelWrongGuesses_NeverExceedAttemptLimit()
    {
        var user = await CreateUserAsync();
        var sender = new FakeEmailSender();
        var (requester, requesterDb) = CreateService(sender);
        await using (requesterDb)
        {
            await requester.RequestPasswordResetAsync(user.Email, null);
        }
        var code = FakeEmailSender.ExtractCode(sender.Sent.Single().Email);
        var wrong = ((int.Parse(code) + 1) % 1_000_000).ToString("D6");

        async Task GuessAsync()
        {
            var (service, db) = CreateService(new FakeEmailSender());
            await using (db)
            {
                try { await service.VerifyPasswordResetCodeAsync(user.Email, wrong); } catch (AppValidationException) { }
            }
        }

        await Task.WhenAll(Enumerable.Range(0, 30).Select(_ => Task.Run(GuessAsync)));

        await using (var afterParallel = CreateContext())
        {
            var row = await afterParallel.UserAccountTokens.SingleAsync(t => t.UserId == user.Id);
            Assert.True(row.AttemptCount <= 5, $"AttemptCount: {row.AttemptCount}");
        }

        // Cakisan istekler kodu hic karsilastirmadan reddedildi; kalan haklar sirayla tuketilir.
        for (var i = 0; i < 5; i++)
        {
            await GuessAsync();
        }

        await using var check = CreateContext();
        Assert.Equal(5, (await check.UserAccountTokens.SingleAsync(t => t.UserId == user.Id)).AttemptCount);

        // Sinir doldu: dogru kod da artik kabul edilmez.
        var (final, finalDb) = CreateService(new FakeEmailSender());
        await using (finalDb)
        {
            var ex = await Assert.ThrowsAsync<AppValidationException>(() => final.VerifyPasswordResetCodeAsync(user.Email, code));
            Assert.Equal(AccountEmailService.TooManyAttemptsMessage, ex.Message);
        }
    }

    [PostgresFact]
    public async Task EmailVerification_EndToEnd_CodeIsSingleUse()
    {
        var user = await CreateUserAsync();
        var sender = new FakeEmailSender();
        var (service, db) = CreateService(sender);
        await using (db)
        {
            Assert.True(await service.TrySendVerificationAfterRegistrationAsync(user, "Test", null));
        }
        var code = FakeEmailSender.ExtractCode(sender.Sent.Single().Email);

        var (first, firstDb) = CreateService(sender);
        await using (firstDb)
        {
            await first.ConfirmEmailAsync(user.Id, code);
        }

        var (second, secondDb) = CreateService(sender);
        await using (secondDb)
        {
            // Dogrulandiktan sonra tekrar cagri basarili doner ama kodu ikinci kez tuketmez.
            Assert.True((await second.ConfirmEmailAsync(user.Id, code)).EmailConfirmed);
        }

        await using var check = CreateContext();
        Assert.True((await check.ApplicationUsers.SingleAsync(u => u.Id == user.Id)).EmailConfirmed);
        Assert.Equal(1, (await check.UserAccountTokens.SingleAsync(t => t.UserId == user.Id)).AttemptCount);
    }
}
