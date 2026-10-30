using MerfitCustomerApi.Business.Common;
using MerfitCustomerApi.Business.Dtos.Auth;
using MerfitCustomerApi.Business.Interfaces.Services;
using MerfitCustomerApi.Business.Services.Auth;
using MerfitCustomerApi.Business.Tests.TestHelpers;
using MerfitCustomerApi.Domain.Entities;
using MerfitCustomerApi.Domain.Exceptions;
using Moq;
using Microsoft.Extensions.Options;
using Xunit;

namespace MerfitCustomerApi.Business.Tests.Auth;

/// <summary>
/// Oturum yenileme ve cikis: refresh token rotasyonu, kayan 10 gunluk sure, gecersiz/iptal edilmis/eski token'larin
/// reddi, yaniti kaybolan yenilemenin kisa sure tekrar denenebilmesi ve cikista token'in iptali.
/// </summary>
public class AuthServiceRefreshTests
{
    private const long UserId = 7;

    private sealed class Fixture
    {
        public InMemoryUnitOfWork Db { get; } = new();
        public AuthService Service { get; }

        public Fixture()
        {
            Db.Register<ApplicationUser>().Register<UserProfile>().Register<UserRefreshToken>();
            Db.Set<ApplicationUser>().Add(new ApplicationUser { Id = UserId, Email = "mert@example.com", IsActive = true, EmailConfirmed = true });
            Db.Set<UserProfile>().Add(new UserProfile { Id = 70, UserId = UserId, FirstName = "Mert", LastName = "Akpınar", Username = "mert" });

            var tokenService = new Mock<ITokenService>();
            tokenService.Setup(t => t.GenerateAccessToken(It.IsAny<ApplicationUser>())).Returns(() => ("access-" + Guid.NewGuid().ToString("N"), DateTime.UtcNow.AddHours(1)));
            tokenService.Setup(t => t.GenerateRefreshToken()).Returns(() => Guid.NewGuid().ToString("N"));

            Service = new AuthService(
                Db.Object, tokenService.Object, Mock.Of<INutritionCalculator>(),
                Options.Create(new JwtSettings { RefreshTokenExpirationDays = 10, RefreshTokenReuseGraceSeconds = 60 }),
                Mock.Of<IAccountEmailService>(), Mock.Of<IGoogleIdTokenValidator>());
        }

        public UserRefreshToken AddToken(string value, DateTime? expiresAt = null, bool used = false, bool revoked = false, DateTime? updatedAt = null)
        {
            var token = new UserRefreshToken
            {
                UserId = UserId, Token = value, ExpiresAt = expiresAt ?? DateTime.UtcNow.AddDays(3),
                IsUsed = used, IsRevoked = revoked, CreatedAt = DateTime.UtcNow.AddDays(-7), UpdatedAt = updatedAt,
            };
            Db.Set<UserRefreshToken>().Add(token);
            return token;
        }

        public Task<AuthResponse> RefreshAsync(string value) => Service.RefreshAsync(new RefreshTokenRequest { RefreshToken = value }, "1.2.3.4");
    }

    [Fact]
    public async Task Refresh_RotatesToken_AndNewTokenIsValidForTenDays()
    {
        var fx = new Fixture();
        var old = fx.AddToken("old-token");

        var result = await fx.RefreshAsync("old-token");

        Assert.Equal(UserId, result.UserId);
        Assert.Equal("Mert Akpınar", result.Name);
        Assert.Equal("mert", result.Username);
        Assert.StartsWith("access-", result.AccessToken);
        Assert.NotEqual("old-token", result.RefreshToken);

        Assert.True(old.IsUsed);
        Assert.Equal(result.RefreshToken, old.ReplacementToken);
        var created = Assert.Single(fx.Db.Set<UserRefreshToken>(), t => t.Token == result.RefreshToken);
        Assert.False(created.IsUsed || created.IsRevoked);
        Assert.InRange(created.ExpiresAt, DateTime.UtcNow.AddDays(10).AddMinutes(-1), DateTime.UtcNow.AddDays(10).AddMinutes(1));
        Assert.Equal(1, fx.Db.SaveCount);
    }

    [Fact]
    public async Task Refresh_ChainKeepsSessionAlive()
    {
        var fx = new Fixture();
        fx.AddToken("t0");

        var first = await fx.RefreshAsync("t0");
        var second = await fx.RefreshAsync(first.RefreshToken);

        Assert.NotEqual(first.RefreshToken, second.RefreshToken);
        Assert.Equal(3, fx.Db.Set<UserRefreshToken>().Count);
    }

    [Theory]
    [InlineData("")]
    [InlineData("unknown")]
    public async Task Refresh_UnknownOrEmptyToken_Is401(string value)
    {
        var fx = new Fixture();
        fx.AddToken("real");

        await Assert.ThrowsAsync<UnauthorizedException>(() => fx.RefreshAsync(value));
    }

    [Fact]
    public async Task Refresh_ExpiredToken_Is401()
    {
        // 10 gun ust uste hic girilmediyse token suresi dolmustur: oturum kapanir.
        var fx = new Fixture();
        fx.AddToken("expired", expiresAt: DateTime.UtcNow.AddSeconds(-1));

        await Assert.ThrowsAsync<UnauthorizedException>(() => fx.RefreshAsync("expired"));
        Assert.Equal(0, fx.Db.SaveCount);
    }

    [Fact]
    public async Task Refresh_RevokedToken_Is401()
    {
        var fx = new Fixture();
        fx.AddToken("revoked", revoked: true);

        await Assert.ThrowsAsync<UnauthorizedException>(() => fx.RefreshAsync("revoked"));
    }

    [Fact]
    public async Task Refresh_InactiveUser_Is401()
    {
        var fx = new Fixture();
        fx.Db.Set<ApplicationUser>().Single().IsActive = false;
        fx.AddToken("t");

        await Assert.ThrowsAsync<UnauthorizedException>(() => fx.RefreshAsync("t"));
    }

    [Fact]
    public async Task Refresh_AlreadyRotatedToken_IsAcceptedOnlyWithinGraceWindow()
    {
        var fx = new Fixture();
        fx.AddToken("just-rotated", used: true, updatedAt: DateTime.UtcNow.AddSeconds(-10));
        fx.AddToken("rotated-long-ago", used: true, updatedAt: DateTime.UtcNow.AddMinutes(-5));

        var retried = await fx.RefreshAsync("just-rotated");
        Assert.False(string.IsNullOrEmpty(retried.RefreshToken));

        await Assert.ThrowsAsync<UnauthorizedException>(() => fx.RefreshAsync("rotated-long-ago"));
    }

    [Fact]
    public async Task Logout_RevokesToken_AndRefreshIsRejectedAfterwards()
    {
        var fx = new Fixture();
        var token = fx.AddToken("t");

        await fx.Service.LogoutAsync(new RefreshTokenRequest { RefreshToken = "t" }, "1.2.3.4");

        Assert.True(token.IsRevoked);
        Assert.Equal("1.2.3.4", token.RevokedByIp);
        await Assert.ThrowsAsync<UnauthorizedException>(() => fx.RefreshAsync("t"));
    }

    [Fact]
    public async Task Logout_UnknownToken_DoesNothing()
    {
        var fx = new Fixture();

        await fx.Service.LogoutAsync(new RefreshTokenRequest { RefreshToken = "nope" }, null);

        Assert.Equal(0, fx.Db.SaveCount);
    }
}
