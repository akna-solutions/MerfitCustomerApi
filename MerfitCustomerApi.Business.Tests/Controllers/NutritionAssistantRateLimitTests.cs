using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using MerfitCustomerApi.Api.BackgroundJobs;
using MerfitCustomerApi.Api.Controllers;
using MerfitCustomerApi.Business.Dtos.Customer.NutritionAssistant;
using MerfitCustomerApi.Business.Interfaces.Services;
using MerfitCustomerApi.Domain.Entities;
using MerfitCustomerApi.Domain.Entities.Enums;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace MerfitCustomerApi.Business.Tests.Controllers;

/// <summary>
/// Sohbet ucunun HTTP hiz siniri: kullanici basina dakikalik sinir asilinca 429 + Retry-After ve
/// ApiResponse govdesi doner; sinir kullanicilar arasinda paylasilmaz ve diger uclari etkilemez.
/// </summary>
public class NutritionAssistantRateLimitTests : IClassFixture<WebApplicationFactory<NutritionAssistantController>>
{
    private readonly WebApplicationFactory<NutritionAssistantController> _factory;
    private readonly Mock<INutritionAssistantService> _service = new();

    public NutritionAssistantRateLimitTests(WebApplicationFactory<NutritionAssistantController> factory)
    {
        _service.Setup(s => s.SendMessageAsync(It.IsAny<long>(), It.IsAny<NutritionAssistantChatRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new NutritionAssistantChatResponse { ConversationId = 3, Message = new() { Role = "assistant", Content = "ok" } });
        _service.Setup(s => s.GetConversationAsync(It.IsAny<long>(), It.IsAny<DateOnly?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new NutritionAssistantConversationDto());

        _factory = factory.WithWebHostBuilder(builder => builder
            .UseSetting("NutritionAssistant:PerUserRequestsPerMinute", "2")
            .UseSetting("NutritionAssistant:PerIpRequestsPerMinute", "1000")
            .UseSetting("NutritionAssistant:Groq:ApiKey", "gsk_wiring_test")
            .UseSetting("NutritionAssistant:FallbackProvider", "Gemini")
            .ConfigureTestServices(services =>
            {
                services.Remove(services.Single(d => d.ImplementationType == typeof(PersonalizationBackgroundWorker)));
                services.Remove(services.Single(d => d.ImplementationType == typeof(NotificationBackgroundWorker)));
                services.AddScoped(_ => _service.Object);
            }));
    }

    private HttpClient CreateClient(long userId)
    {
        var client = _factory.CreateClient();
        using var scope = _factory.Services.CreateScope();
        var tokenService = scope.ServiceProvider.GetRequiredService<ITokenService>();
        var (token, _) = tokenService.GenerateAccessToken(new ApplicationUser { Id = userId, Email = "u@test.local", Role = UserRole.User });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    [Fact]
    public async Task PerUserLimit_Returns429WithRetryAfter_AndDoesNotAffectOtherUsersOrEndpoints()
    {
        var client = CreateClient(501);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync("/api/nutrition-assistant/chat", JsonContent.Create(new { message = "1" }))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync("/api/nutrition-assistant/chat", JsonContent.Create(new { message = "2" }))).StatusCode);

        var limited = await client.PostAsync("/api/nutrition-assistant/chat", JsonContent.Create(new { message = "3" }));

        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.NotNull(limited.Headers.RetryAfter?.Delta);
        using var body = JsonDocument.Parse(await limited.Content.ReadAsStringAsync());
        Assert.False(body.RootElement.GetProperty("isSuccess").GetBoolean());
        Assert.Contains("hızlı", body.RootElement.GetProperty("errorMessage").GetString());
        _service.Verify(s => s.SendMessageAsync(501, It.IsAny<NutritionAssistantChatRequest>(), It.IsAny<CancellationToken>()), Times.Exactly(2));

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/nutrition-assistant/conversation")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await CreateClient(502).PostAsync("/api/nutrition-assistant/chat", JsonContent.Create(new { message = "1" }))).StatusCode);
    }

    [Fact]
    public void ProvidersAreWiredFromConfiguration_WithTypedHttpClients()
    {
        using var scope = _factory.Services.CreateScope();
        var router = Assert.IsType<MerfitCustomerApi.Business.Services.NutritionAssistant.NutritionAiProviderRouter>(
            scope.ServiceProvider.GetRequiredService<INutritionAssistantModelClient>());

        // Gemini anahtari olmadigi icin yedek zincire eklenmez; Anthropic (ucretli) hic eklenmez.
        Assert.True(router.IsConfigured);
        Assert.Equal("openai/gpt-oss-120b", router.Model);
        Assert.Equal("Groq", router.ProviderChain[0]);
        Assert.DoesNotContain("Anthropic", router.ProviderChain);
    }
}
