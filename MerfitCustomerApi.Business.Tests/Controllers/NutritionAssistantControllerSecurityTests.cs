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
using MerfitCustomerApi.Domain.Exceptions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace MerfitCustomerApi.Business.Tests.Controllers;

/// <summary>
/// Beslenme asistani uc noktalari kimlik dogrulamasi ister, kullanici kimligini yalnizca JWT'den alir;
/// sahiplik, limit ve saglayici hatalari dogru HTTP durumlariyla ve anlasilir mesajla doner.
/// </summary>
public class NutritionAssistantControllerSecurityTests : IClassFixture<WebApplicationFactory<NutritionAssistantController>>
{
    private readonly WebApplicationFactory<NutritionAssistantController> _factory;
    private readonly Mock<INutritionAssistantService> _service = new();

    public NutritionAssistantControllerSecurityTests(WebApplicationFactory<NutritionAssistantController> factory)
    {
        _service.Setup(s => s.SendMessageAsync(It.IsAny<long>(), It.IsAny<NutritionAssistantChatRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new NutritionAssistantChatResponse { ConversationId = 3, Message = new() { Role = "assistant", Content = "ok" } });
        _service.Setup(s => s.GetConversationAsync(It.IsAny<long>(), It.IsAny<DateOnly?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new NutritionAssistantConversationDto());

        _factory = factory.WithWebHostBuilder(builder => builder
            // Bu siniftaki testler hiz sinirina takilmasin; hiz siniri ayri test sinifinda dogrulanir.
            .UseSetting("NutritionAssistant:PerUserRequestsPerMinute", "1000")
            .UseSetting("NutritionAssistant:PerIpRequestsPerMinute", "1000")
            .ConfigureTestServices(services =>
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

    [Fact]
    public async Task AnonymousRequests_AreRejectedWith401()
    {
        var client = CreateClient(null);

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsync("/api/nutrition-assistant/chat", JsonContent.Create(new { message = "x" }))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/nutrition-assistant/conversation")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.DeleteAsync("/api/nutrition-assistant/conversation")).StatusCode);
        _service.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Chat_UsesAuthenticatedUserId_IgnoringClientSuppliedUserId()
    {
        var response = await CreateClient(42).PostAsync("/api/nutrition-assistant/chat",
            JsonContent.Create(new { userId = 7, message = "Bugün proteinim düşük mü?", conversationId = 3, date = "2026-10-13" }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        _service.Verify(s => s.SendMessageAsync(42, It.Is<NutritionAssistantChatRequest>(r =>
            r.ConversationId == 3 && r.Date == new DateOnly(2026, 10, 13) && r.Message == "Bugün proteinim düşük mü?"), It.IsAny<CancellationToken>()), Times.Once);
        _service.Verify(s => s.SendMessageAsync(7, It.IsAny<NutritionAssistantChatRequest>(), It.IsAny<CancellationToken>()), Times.Never);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("ok", body.RootElement.GetProperty("message").GetProperty("content").GetString());
    }

    [Fact]
    public async Task ForeignConversation_Returns404()
    {
        _service.Setup(s => s.SendMessageAsync(42, It.IsAny<NutritionAssistantChatRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new NotFoundException("Sohbet bulunamadı."));

        var response = await CreateClient(42).PostAsync("/api/nutrition-assistant/chat", JsonContent.Create(new { message = "x", conversationId = 999 }));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ProviderDown_Returns503WithReadableMessage()
    {
        _service.Setup(s => s.SendMessageAsync(42, It.IsAny<NutritionAssistantChatRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ExternalServiceUnavailableException("Beslenme asistanı şu an yanıt veremiyor."));

        var response = await CreateClient(42).PostAsync("/api/nutrition-assistant/chat", JsonContent.Create(new { message = "x" }));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.False(body.RootElement.GetProperty("isSuccess").GetBoolean());
        Assert.Equal("Beslenme asistanı şu an yanıt veremiyor.", body.RootElement.GetProperty("errorMessage").GetString());
    }

    [Fact]
    public async Task Limit_Returns429()
    {
        _service.Setup(s => s.SendMessageAsync(42, It.IsAny<NutritionAssistantChatRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TooManyRequestsException("limit"));

        var response = await CreateClient(42).PostAsync("/api/nutrition-assistant/chat", JsonContent.Create(new { message = "x" }));

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
    }

    [Fact]
    public async Task OversizedBody_IsRejectedByModelValidation()
    {
        var response = await CreateClient(42).PostAsync("/api/nutrition-assistant/chat", JsonContent.Create(new { message = new string('a', 5000) }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        _service.Verify(s => s.SendMessageAsync(It.IsAny<long>(), It.IsAny<NutritionAssistantChatRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Conversation_UsesAuthenticatedUserAndDate()
    {
        var response = await CreateClient(42).GetAsync("/api/nutrition-assistant/conversation?date=2026-10-13");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        _service.Verify(s => s.GetConversationAsync(42, new DateOnly(2026, 10, 13), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ProviderQuota_Returns503_WithRetryAfterHeader()
    {
        _service.Setup(s => s.SendMessageAsync(42, It.IsAny<NutritionAssistantChatRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ExternalServiceUnavailableException("Beslenme asistanı şu an çok yoğun.", retryAfter: TimeSpan.FromSeconds(12.2)));

        var response = await CreateClient(42).PostAsync("/api/nutrition-assistant/chat", JsonContent.Create(new { message = "x" }));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(TimeSpan.FromSeconds(13), response.Headers.RetryAfter!.Delta);
    }

    [Fact]
    public async Task DeleteConversation_UsesAuthenticatedUser()
    {
        _service.Setup(s => s.DeleteConversationAsync(It.IsAny<long>(), It.IsAny<DateOnly?>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var response = await CreateClient(42).DeleteAsync("/api/nutrition-assistant/conversation?date=2026-10-13&userId=7");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        _service.Verify(s => s.DeleteConversationAsync(42, new DateOnly(2026, 10, 13), It.IsAny<CancellationToken>()), Times.Once);
        _service.Verify(s => s.DeleteConversationAsync(7, It.IsAny<DateOnly?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ResponsesNeverContainProviderKeys()
    {
        var response = await CreateClient(42).PostAsync("/api/nutrition-assistant/chat", JsonContent.Create(new { message = "x" }));
        var body = await response.Content.ReadAsStringAsync();

        Assert.DoesNotContain("gsk_", body);
        Assert.DoesNotContain("apiKey", body, StringComparison.OrdinalIgnoreCase);
    }
}
