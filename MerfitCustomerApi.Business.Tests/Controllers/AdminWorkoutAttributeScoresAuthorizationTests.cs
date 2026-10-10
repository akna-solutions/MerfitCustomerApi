using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Reflection;
using MerfitCustomerApi.Api.BackgroundJobs;
using MerfitCustomerApi.Api.Controllers;
using MerfitCustomerApi.Business.Dtos.Admin.Workouts;
using MerfitCustomerApi.Business.Interfaces.Services;
using MerfitCustomerApi.Domain.Entities;
using MerfitCustomerApi.Domain.Entities.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Moq;
using Xunit;

namespace MerfitCustomerApi.Business.Tests.Controllers;

/// <summary>
/// Admin puan yonetimi uc noktalarinin yalnizca Admin/SuperAdmin rolune acik oldugunu, gercek
/// ASP.NET Core pipeline'i (JWT dogrulama + Program.cs'teki "AdminOnly" policy) uzerinden dogrular.
/// Veritabanina erisilmez: servis mock'lanir ve arka plan calisani devre disi birakilir.
/// </summary>
public class AdminWorkoutAttributeScoresAuthorizationTests : IClassFixture<WebApplicationFactory<AdminWorkoutAttributeScoresController>>
{
    private const string Url = "/api/admin/workouts/5/attribute-scores";

    private readonly WebApplicationFactory<AdminWorkoutAttributeScoresController> _factory;
    private readonly Mock<IWorkoutAttributeScoreService> _service = new();

    public AdminWorkoutAttributeScoresAuthorizationTests(WebApplicationFactory<AdminWorkoutAttributeScoresController> factory)
    {
        _service
            .Setup(s => s.GetAsync(5, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AdminWorkoutAttributeScoresDto { WorkoutId = 5 });
        _service
            .Setup(s => s.SetAsync(5, It.IsAny<AdminSetWorkoutAttributeScoresRequest>(), It.IsAny<long?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AdminWorkoutAttributeScoresDto { WorkoutId = 5 });

        _factory = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            var worker = services.Single(d => d.ImplementationType == typeof(PersonalizationBackgroundWorker));
            services.Remove(worker);
            services.AddScoped(_ => _service.Object);
        }));
    }

    private HttpClient CreateClient(UserRole? role)
    {
        var client = _factory.CreateClient();
        if (role is not null)
        {
            using var scope = _factory.Services.CreateScope();
            var tokenService = scope.ServiceProvider.GetRequiredService<ITokenService>();
            var (token, _) = tokenService.GenerateAccessToken(new ApplicationUser { Id = 42, Email = "admin@test.local", Role = role.Value });
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return client;
    }

    private static HttpContent ValidBody() => JsonContent.Create(new
    {
        scores = new[] { new { attributeType = (int)WorkoutAttributeType.Hypertrophy, score = 80 } },
    });

    [Fact]
    public async Task AnonymousRequests_AreRejectedWith401()
    {
        var client = CreateClient(role: null);

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(Url)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PutAsync(Url, ValidBody())).StatusCode);
    }

    [Fact]
    public async Task RegularMobileUser_IsForbidden_AndServiceIsNeverCalled()
    {
        var client = CreateClient(UserRole.User);

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(Url)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PutAsync(Url, ValidBody())).StatusCode);
        _service.Verify(
            s => s.SetAsync(It.IsAny<long>(), It.IsAny<AdminSetWorkoutAttributeScoresRequest>(), It.IsAny<long?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Theory]
    [InlineData(UserRole.Admin)]
    [InlineData(UserRole.SuperAdmin)]
    public async Task AdminRoles_CanReadAndUpdateScores(UserRole role)
    {
        var client = CreateClient(role);

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(Url)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsync(Url, ValidBody())).StatusCode);
        _service.Verify(
            s => s.SetAsync(5, It.IsAny<AdminSetWorkoutAttributeScoresRequest>(), 42, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task OutOfRangeScore_IsRejectedWith400_BeforeReachingService()
    {
        var client = CreateClient(UserRole.Admin);
        var body = JsonContent.Create(new
        {
            scores = new[] { new { attributeType = (int)WorkoutAttributeType.Cardio, score = 150 } },
        });

        var response = await client.PutAsync(Url, body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        _service.Verify(
            s => s.SetAsync(It.IsAny<long>(), It.IsAny<AdminSetWorkoutAttributeScoresRequest>(), It.IsAny<long?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public void Controller_UsesAdminOnlyPolicy_AndNoActionAllowsAnonymous()
    {
        var authorize = typeof(AdminWorkoutAttributeScoresController).GetCustomAttribute<AuthorizeAttribute>();

        Assert.Equal("AdminOnly", authorize?.Policy);
        Assert.All(
            typeof(AdminWorkoutAttributeScoresController).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly),
            m => Assert.Null(m.GetCustomAttribute<AllowAnonymousAttribute>()));
    }
}
