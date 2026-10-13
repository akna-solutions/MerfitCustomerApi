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
/// Gercek PostgreSQL'de (MERFIT_TEST_POSTGRES) uctan uca: ayni sifre sifirlama jetonuyla es zamanli iki
/// istekten yalnizca biri sifreyi degistirir; dogrulama jetonu ikinci kez kullanilamaz.
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
    public async Task ConcurrentPasswordResets_WithSameToken_OnlyOneSucceeds()
    {
        var user = await CreateUserAsync();
        var sender = new FakeEmailSender();
        var (requester, requesterDb) = CreateService(sender);
        await using (requesterDb)
        {
            await requester.RequestPasswordResetAsync(user.Email, null);
        }
        var token = FakeEmailSender.ExtractToken(sender.Sent.Single().Email);

        async Task<bool> ResetAsync(string password)
        {
            var (service, db) = CreateService(new FakeEmailSender());
            await using (db)
            {
                try
                {
                    await service.ResetPasswordAsync(new ResetPasswordRequest { Token = token, NewPassword = password, ConfirmPassword = password }, null);
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
        Assert.NotEqual(token, row.TokenHash);
    }

    [PostgresFact]
    public async Task EmailVerification_EndToEnd_TokenIsSingleUse()
    {
        var user = await CreateUserAsync();
        var sender = new FakeEmailSender();
        var (service, db) = CreateService(sender);
        await using (db)
        {
            Assert.True(await service.TrySendVerificationAfterRegistrationAsync(user, "Test", null));
        }
        var token = FakeEmailSender.ExtractToken(sender.Sent.Single().Email);

        var (first, firstDb) = CreateService(sender);
        await using (firstDb)
        {
            await first.ConfirmEmailAsync(token);
        }

        var (second, secondDb) = CreateService(sender);
        await using (secondDb)
        {
            await Assert.ThrowsAsync<AppValidationException>(() => second.ConfirmEmailAsync(token));
        }

        await using var check = CreateContext();
        Assert.True((await check.ApplicationUsers.SingleAsync(u => u.Id == user.Id)).EmailConfirmed);
    }
}
