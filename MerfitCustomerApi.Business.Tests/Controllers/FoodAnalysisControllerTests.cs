using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using MerfitCustomerApi.Api.BackgroundJobs;
using MerfitCustomerApi.Api.Controllers;
using MerfitCustomerApi.Business.Dtos.Customer.FoodAnalysis;
using MerfitCustomerApi.Business.Interfaces.Services;
using MerfitCustomerApi.Business.Tests.FoodAnalysis;
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
/// Fotograf analizi uclari: kimlik dogrulama, kullanici kimliginin yalnizca JWT'den alinmasi, multipart dosya
/// dogrulamasi, hata durumlarinin ApiResponse zarfi ve dogru HTTP koduyla donmesi, hiz siniri.
/// </summary>
public class FoodAnalysisControllerTests : IClassFixture<WebApplicationFactory<FoodAnalysisController>>
{
    private readonly WebApplicationFactory<FoodAnalysisController> _factory;
    private readonly Mock<IFoodAnalysisService> _service = new();

    public FoodAnalysisControllerTests(WebApplicationFactory<FoodAnalysisController> factory)
    {
        _service.Setup(s => s.AnalyzeAsync(It.IsAny<long>(), It.IsAny<FoodImageUpload>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FoodAnalysisResultDto
            {
                AnalysisId = 55,
                FoodName = "Pilav",
                Items = { new FoodAnalysisItemDto { ItemId = 1, FoodName = "Pilav", EstimatedGrams = 180, Calories = 234, Confidence = 0.9m } },
            });
        _factory = factory.WithWebHostBuilder(builder => builder
            .UseSetting("FoodAnalysis:PerUserRequestsPerMinute", "1000")
            .UseSetting("FoodAnalysis:PerIpRequestsPerMinute", "1000")
            .UseSetting("FoodAnalysis:MaxImageBytes", "4096")
            .ConfigureTestServices(services =>
            {
                services.Remove(services.Single(d => d.ImplementationType == typeof(PersonalizationBackgroundWorker)));
                services.Remove(services.Single(d => d.ImplementationType == typeof(NotificationBackgroundWorker)));
                services.AddScoped(_ => _service.Object);
            }));
    }

    private HttpClient CreateClient(long? userId, WebApplicationFactory<FoodAnalysisController>? factory = null)
    {
        factory ??= _factory;
        var client = factory.CreateClient();
        if (userId is not null)
        {
            using var scope = factory.Services.CreateScope();
            var tokenService = scope.ServiceProvider.GetRequiredService<ITokenService>();
            var (token, _) = tokenService.GenerateAccessToken(new ApplicationUser { Id = userId.Value, Email = "u@test.local", Role = UserRole.User });
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
        return client;
    }

    private static MultipartFormDataContent Photo(byte[] bytes, string field = "photo", string contentType = "image/jpeg")
    {
        var content = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        content.Add(file, field, "meal.jpg");
        content.Add(new StringContent("7"), "userId");
        return content;
    }

    private static async Task<JsonElement> Body(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.Clone();
    }

    [Fact]
    public async Task AnonymousRequests_AreRejectedWith401()
    {
        var client = CreateClient(null);

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsync("/api/food-analysis/analyze", Photo(FoodAnalysisTestData.Jpeg()))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsync("/api/food-analysis/5/confirm", JsonContent.Create(new { mealType = "Lunch" }))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/food-analysis/status")).StatusCode);
        _service.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Status_ReturnsAuthenticatedUsersQuotaAndNotice()
    {
        _service.Setup(s => s.GetStatusAsync(42, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FoodAnalysisStatusDto { Enabled = true, DataNotice = "Google", DailyLimit = 15, RemainingAnalyses = 9 });

        var response = await CreateClient(42).GetAsync("/api/food-analysis/status");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await Body(response);
        Assert.True(body.GetProperty("enabled").GetBoolean());
        Assert.Equal(9, body.GetProperty("remainingAnalyses").GetInt32());
        Assert.Equal("Google", body.GetProperty("dataNotice").GetString());
    }

    [Fact]
    public async Task Analyze_RequestOverSizeLimit_Returns413ApiResponse_WithoutCallingService()
    {
        var response = await CreateClient(42).PostAsync("/api/food-analysis/analyze", Photo(new byte[FoodAnalysisController.MaxAnalyzeRequestBytes + 1024]));

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.False((await Body(response)).GetProperty("isSuccess").GetBoolean());
        _service.Verify(s => s.AnalyzeAsync(It.IsAny<long>(), It.IsAny<FoodImageUpload>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Analyze_PassesFileBytes_AndAuthenticatedUserId_IgnoringFormUserId()
    {
        var image = FoodAnalysisTestData.Jpeg();

        var response = await CreateClient(42).PostAsync("/api/food-analysis/analyze", Photo(image));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        _service.Verify(s => s.AnalyzeAsync(42, It.Is<FoodImageUpload>(u => u.Content.ToArray().SequenceEqual(image) && u.ContentType == "image/jpeg"), It.IsAny<CancellationToken>()), Times.Once);
        _service.Verify(s => s.AnalyzeAsync(7, It.IsAny<FoodImageUpload>(), It.IsAny<CancellationToken>()), Times.Never);
        var body = await Body(response);
        Assert.Equal(55, body.GetProperty("analysisId").GetInt64());
        var item = body.GetProperty("items")[0];
        foreach (var field in new[] { "foodName", "estimatedGrams", "calories", "protein", "carbs", "fat", "confidence", "isEstimate" })
        {
            Assert.True(item.TryGetProperty(field, out _), field);
        }
    }

    [Fact]
    public async Task Analyze_MissingOrEmptyPhoto_Returns400WithApiResponse()
    {
        var client = CreateClient(42);

        var missing = await client.PostAsync("/api/food-analysis/analyze", Photo(FoodAnalysisTestData.Jpeg(), field: "image"));
        var empty = await client.PostAsync("/api/food-analysis/analyze", Photo(Array.Empty<byte>()));

        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);
        var body = await Body(empty);
        Assert.False(body.GetProperty("isSuccess").GetBoolean());
        Assert.Contains("Fotoğraf", body.GetProperty("errorMessage").GetString());
        _service.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Analyze_FileOverConfiguredLimit_IsRejectedBeforeService()
    {
        var response = await CreateClient(42).PostAsync("/api/food-analysis/analyze", Photo(new byte[5000]));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        _service.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Analyze_JsonBody_IsRejected()
    {
        var response = await CreateClient(42).PostAsync("/api/food-analysis/analyze", JsonContent.Create(new { photo = "base64..." }));

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
        _service.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Analyze_ServiceErrors_MapToStatusCodes()
    {
        var client = CreateClient(42);

        _service.Setup(s => s.AnalyzeAsync(42, It.IsAny<FoodImageUpload>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AppValidationException("Photo", "Dosya bozuk ya da desteklenmeyen bir biçimde."));
        var invalid = await client.PostAsync("/api/food-analysis/analyze", Photo(FoodAnalysisTestData.Jpeg()));
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Equal("Dosya bozuk ya da desteklenmeyen bir biçimde.", (await Body(invalid)).GetProperty("errorMessage").GetString());

        _service.Setup(s => s.AnalyzeAsync(42, It.IsAny<FoodImageUpload>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ExternalServiceUnavailableException("Analiz sonucu okunamadı.", retryAfter: TimeSpan.FromSeconds(9.5)));
        var unavailable = await client.PostAsync("/api/food-analysis/analyze", Photo(FoodAnalysisTestData.Jpeg()));
        Assert.Equal(HttpStatusCode.ServiceUnavailable, unavailable.StatusCode);
        Assert.Equal(TimeSpan.FromSeconds(10), unavailable.Headers.RetryAfter!.Delta);

        _service.Setup(s => s.AnalyzeAsync(42, It.IsAny<FoodImageUpload>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TooManyRequestsException("Bugünlük fotoğraf analizi hakkın doldu."));
        Assert.Equal(HttpStatusCode.TooManyRequests, (await client.PostAsync("/api/food-analysis/analyze", Photo(FoodAnalysisTestData.Jpeg()))).StatusCode);
    }

    [Fact]
    public async Task Confirm_UsesAuthenticatedUser_AndReturns201ThenOkForRepeat()
    {
        _service.SetupSequence(s => s.ConfirmAsync(42, 55, It.IsAny<ConfirmFoodAnalysisRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ConfirmFoodAnalysisResponse { AnalysisId = 55, Entries = { new() { Id = 1, Name = "Pilav", Calories = 234, Type = "Öğle Yemeği" } } })
            .ReturnsAsync(new ConfirmFoodAnalysisResponse { AnalysisId = 55, AlreadyLogged = true });
        var client = CreateClient(42);
        var body = new { userId = 7, mealType = "Lunch", date = "2026-10-14", items = new[] { new { itemId = 1, grams = 200 } } };

        var first = await client.PostAsync("/api/food-analysis/55/confirm", JsonContent.Create(body));
        var second = await client.PostAsync("/api/food-analysis/55/confirm", JsonContent.Create(body));

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.True((await Body(second)).GetProperty("alreadyLogged").GetBoolean());
        _service.Verify(s => s.ConfirmAsync(42, 55, It.Is<ConfirmFoodAnalysisRequest>(r =>
            r.MealType == "Lunch" && r.Date == new DateOnly(2026, 10, 14) && r.Items.Single().ItemId == 1 && r.Items.Single().Grams == 200), It.IsAny<CancellationToken>()), Times.Exactly(2));
        _service.Verify(s => s.ConfirmAsync(7, It.IsAny<long>(), It.IsAny<ConfirmFoodAnalysisRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Confirm_InvalidBody_Returns400_WithoutCallingService()
    {
        var client = CreateClient(42);

        var noItems = await client.PostAsync("/api/food-analysis/55/confirm", JsonContent.Create(new { mealType = "Lunch", items = Array.Empty<object>() }));
        var badGrams = await client.PostAsync("/api/food-analysis/55/confirm", JsonContent.Create(new { mealType = "Lunch", items = new[] { new { itemId = 1, grams = 0 } } }));

        Assert.Equal(HttpStatusCode.BadRequest, noItems.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, badGrams.StatusCode);
        _service.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Confirm_ForeignAnalysis_Returns404()
    {
        _service.Setup(s => s.ConfirmAsync(42, 999, It.IsAny<ConfirmFoodAnalysisRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new NotFoundException("Analiz bulunamadı."));

        var response = await CreateClient(42).PostAsync("/api/food-analysis/999/confirm",
            JsonContent.Create(new { mealType = "Lunch", items = new[] { new { itemId = 1, grams = 100 } } }));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Analyze_PerUserRateLimit_Returns429WithRetryAfter()
    {
        var limited = _factory.WithWebHostBuilder(builder => builder
            .UseSetting("FoodAnalysis:PerUserRequestsPerMinute", "2")
            .UseSetting("FoodAnalysis:PerIpRequestsPerMinute", "1000"));
        var client = CreateClient(77, limited);
        var other = CreateClient(78, limited);

        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync("/api/food-analysis/analyze", Photo(FoodAnalysisTestData.Jpeg()))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync("/api/food-analysis/analyze", Photo(FoodAnalysisTestData.Jpeg()))).StatusCode);
        var third = await client.PostAsync("/api/food-analysis/analyze", Photo(FoodAnalysisTestData.Jpeg()));

        Assert.Equal(HttpStatusCode.TooManyRequests, third.StatusCode);
        Assert.NotNull(third.Headers.RetryAfter);
        Assert.False((await Body(third)).GetProperty("isSuccess").GetBoolean());
        // Baska kullanici ve diger uclar etkilenmez.
        Assert.Equal(HttpStatusCode.OK, (await other.PostAsync("/api/food-analysis/analyze", Photo(FoodAnalysisTestData.Jpeg()))).StatusCode);
    }

    [Fact]
    public async Task ResponsesNeverContainProviderKeys()
    {
        var response = await CreateClient(42).PostAsync("/api/food-analysis/analyze", Photo(FoodAnalysisTestData.Jpeg()));
        var body = await response.Content.ReadAsStringAsync();

        Assert.DoesNotContain("AIza", body);
        Assert.DoesNotContain("apiKey", body, StringComparison.OrdinalIgnoreCase);
    }
}
