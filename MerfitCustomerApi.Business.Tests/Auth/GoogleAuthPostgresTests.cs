using MerfitCustomerApi.Business.Common;
using MerfitCustomerApi.Business.Dtos.Auth;
using MerfitCustomerApi.Business.Interfaces.Services;
using MerfitCustomerApi.Business.Services.Auth;
using MerfitCustomerApi.Business.Services.Nutrition;
using MerfitCustomerApi.Business.Services.TokenService;
using MerfitCustomerApi.Business.Tests.AccountEmail;
using MerfitCustomerApi.Business.Tests.Notifications;
using MerfitCustomerApi.Domain.Exceptions;
using MerfitCustomerApi.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace MerfitCustomerApi.Business.Tests.Auth;

/// <summary>
/// Gercek PostgreSQL uzerinde: ayni Google hesabiyla es zamanli kayit istekleri tek bir hesap olusturur
/// (ExternalLogin (Provider, ProviderUserId) unique index'i).
/// </summary>
public class GoogleAuthPostgresTests
{
    private static AppDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(Environment.GetEnvironmentVariable(PostgresNotificationQueueTests.ConnectionVariable)).Options);

    [PostgresFact]
    public async Task ConcurrentGoogleRegistrations_WithSameGoogleAccount_CreateOneAccount()
    {
        var subject = $"google-{Guid.NewGuid():N}";
        var email = $"{subject}@gmail.test";
        var google = new Mock<IGoogleIdTokenValidator>();
        google.Setup(g => g.ValidateAsync("tok", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GoogleIdentity(subject, email, EmailVerified: true, "Mert", "Akpınar"));
        var calculator = new Mock<INutritionCalculator>();
        calculator.Setup(c => c.Calculate(It.IsAny<Domain.Entities.UserProfile>())).Returns(new NutritionCalculationResult { DailyCalories = 2000 });
        var tokens = new TokenService(Options.Create(new JwtSettings { Key = new string('k', 64), Issuer = "t", Audience = "t" }));

        async Task<string> RegisterAsync(int i)
        {
            await using var db = CreateContext();
            var unitOfWork = new UnitOfWork(db);
            var accountEmails = new AccountEmailService(unitOfWork, new FakeEmailSender(), Options.Create(new AccountEmailOptions()), TimeProvider.System, NullLogger<AccountEmailService>.Instance);
            var service = new AuthService(unitOfWork, tokens, calculator.Object, Options.Create(new JwtSettings { RefreshTokenExpirationDays = 30 }), accountEmails, google.Object);
            try
            {
                var result = await service.GoogleRegisterAsync(new GoogleRegisterRequest
                {
                    IdToken = "tok", FirstName = "Mert", LastName = "Akpınar", Username = $"g{subject[7..20]}{i}",
                    HeightUnit = "cm", HeightCm = 180, WeightUnit = "kg", Weight = 78,
                }, null);
                return result.IsNewUser ? "created" : "signed-in";
            }
            catch (ConflictException)
            {
                return "conflict";
            }
        }

        var results = await Task.WhenAll(Enumerable.Range(0, 5).Select(i => Task.Run(() => RegisterAsync(i))));

        Assert.Equal(1, results.Count(r => r == "created"));
        await using var check = CreateContext();
        Assert.Equal(1, await check.ExternalLogins.CountAsync(l => l.ProviderUserId == subject));
        Assert.Equal(1, await check.ApplicationUsers.CountAsync(u => u.NormalizedEmail == email.ToUpperInvariant()));
    }

    [PostgresFact]
    public async Task ConcurrentRegistrations_WithSameUsername_LoserGetsConflictNotCrash()
    {
        // UnitOfWork.CommitTransactionAsync hata aninda NullReferenceException firlatip unique ihlalini gizliyordu.
        var username = $"u{Guid.NewGuid():N}"[..20];
        var calculator = new Mock<INutritionCalculator>();
        calculator.Setup(c => c.Calculate(It.IsAny<Domain.Entities.UserProfile>())).Returns(new NutritionCalculationResult { DailyCalories = 2000 });
        var tokens = new TokenService(Options.Create(new JwtSettings { Key = new string('k', 64), Issuer = "t", Audience = "t" }));

        async Task<string> RegisterAsync(int i)
        {
            await using var db = CreateContext();
            var unitOfWork = new UnitOfWork(db);
            var accountEmails = new AccountEmailService(unitOfWork, new FakeEmailSender(), Options.Create(new AccountEmailOptions()), TimeProvider.System, NullLogger<AccountEmailService>.Instance);
            var service = new AuthService(unitOfWork, tokens, calculator.Object, Options.Create(new JwtSettings { RefreshTokenExpirationDays = 30 }), accountEmails, Mock.Of<IGoogleIdTokenValidator>());
            try
            {
                await service.RegisterAsync(new RegisterRequest
                {
                    FirstName = "Mert", LastName = "Akpınar", Username = username, Email = $"{username}-{i}@test.local",
                    Password = "password1", ConfirmPassword = "password1", HeightUnit = "cm", HeightCm = 180, WeightUnit = "kg", Weight = 78,
                }, null);
                return "created";
            }
            catch (ConflictException)
            {
                return "conflict";
            }
        }

        var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(i => Task.Run(() => RegisterAsync(i))));

        Assert.Equal(1, results.Count(r => r == "created"));
        Assert.Equal(3, results.Count(r => r == "conflict"));
    }
}
