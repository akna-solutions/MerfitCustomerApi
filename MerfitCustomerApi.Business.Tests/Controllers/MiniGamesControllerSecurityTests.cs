using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using MerfitCustomerApi.Api.BackgroundJobs;
using MerfitCustomerApi.Api.Controllers;
using MerfitCustomerApi.Business.Dtos.Customer.MiniGames;
using MerfitCustomerApi.Business.Interfaces.Services;
using MerfitCustomerApi.Domain.Entities;
using MerfitCustomerApi.Domain.Entities.Enums;
using MerfitCustomerApi.Domain.Exceptions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace MerfitCustomerApi.Business.Tests.Controllers;

/// <summary>
/// Mini oyun uc noktalari kimlik dogrulamasi ister, kullanici kimligini yalnizca JWT'den alir ve gunluk
/// oturum siniri 429 olarak doner.
/// </summary>
public class MiniGamesControllerSecurityTests : IClassFixture<WebApplicationFactory<MiniGamesController>>
{
    private readonly WebApplicationFactory<MiniGamesController> _factory;
    private readonly Mock<IMiniGameService> _service = new();

    public MiniGamesControllerSecurityTests(WebApplicationFactory<MiniGamesController> factory)
    {
        _service.Setup(s => s.StartSessionAsync(It.IsAny<long>(), It.IsAny<StartMiniGameSessionRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MiniGameSessionDto { RewardsEnabled = true, SessionId = 1, Seed = 5, EngineVersion = 1 });
        _service.Setup(s => s.FinishSessionAsync(It.IsAny<long>(), It.IsAny<long>(), It.IsAny<FinishMiniGameSessionRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MiniGameResultDto { SessionId = 1, Status = MiniGameResultStatus.NoReward });

        _factory = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.Remove(services.Single(d => d.ImplementationType == typeof(PersonalizationBackgroundWorker)));
            services.Remove(services.Single(d => d.ImplementationType == typeof(NotificationBackgroundWorker)));
            services.AddScoped(_ => _service.Object);
        }));
    }

    private HttpClient CreateClient(long? userId)
    {
        var client = _factory.CreateClient();
        if (userId is not null)
        {
            using var scope = _factory.Services.CreateScope();
            var tokenService = scope.ServiceProvider.GetRequiredService<ITokenService>();
            var (token, _) = tokenService.GenerateAccessToken(new ApplicationUser { Id = userId.Value, Email = "u@test.local", Role = UserRole.User });
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
        return client;
    }

    [Theory]
    [InlineData("/api/mini-games/dumbbell-dash/sessions")]
    [InlineData("/api/mini-games/dumbbell-dash/sessions/1/finish")]
    public async Task AnonymousRequests_AreRejectedWith401(string url)
    {
        var response = await CreateClient(null).PostAsync(url, JsonContent.Create(new { engineVersion = 1 }));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Finish_UsesAuthenticatedUserId_IgnoringClientSuppliedUserId()
    {
        var response = await CreateClient(42).PostAsync("/api/mini-games/dumbbell-dash/sessions/9/finish",
            JsonContent.Create(new { userId = 7, score = 3, ticks = 100, flapTicks = new[] { 0, 10 }, awardedPoints = 999 }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        _service.Verify(s => s.FinishSessionAsync(42, 9, It.Is<FinishMiniGameSessionRequest>(r => r.Score == 3 && r.FlapTicks.Count == 2), It.IsAny<CancellationToken>()), Times.Once);
        _service.Verify(s => s.FinishSessionAsync(7, It.IsAny<long>(), It.IsAny<FinishMiniGameSessionRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Start_DailyLimit_Returns429()
    {
        _service.Setup(s => s.StartSessionAsync(It.IsAny<long>(), It.IsAny<StartMiniGameSessionRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TooManyRequestsException("limit"));

        var response = await CreateClient(42).PostAsync("/api/mini-games/dumbbell-dash/sessions", JsonContent.Create(new { engineVersion = 1 }));

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
    }

    [Fact]
    public async Task Finish_TooManyInputs_IsRejectedByModelValidation()
    {
        var response = await CreateClient(42).PostAsync("/api/mini-games/dumbbell-dash/sessions/9/finish",
            JsonContent.Create(new { score = 1, ticks = 10, flapTicks = Enumerable.Range(0, 36_001).ToArray() }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
