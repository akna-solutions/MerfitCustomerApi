using System.Net;
using System.Text.Json;
using MerfitCustomerApi.Business.Common;
using MerfitCustomerApi.Business.Interfaces.Services;
using MerfitCustomerApi.Business.Services.FoodAnalysis;
using MerfitCustomerApi.Business.Tests.NutritionAssistant;
using Microsoft.Extensions.Options;
using Xunit;

namespace MerfitCustomerApi.Business.Tests.FoodAnalysis;

/// <summary>Gemini goruntu istegi bicimi (inline_data + JSON sema), anahtarin yeri ve hata siniflandirmasi. Gercek API cagrilmaz.</summary>
public class GeminiFoodImageAnalysisProviderTests
{
    private const string Key = "AIza_food_test_secret";
    private readonly StubHttpHandler _http = new();

    private GeminiFoodImageAnalysisProvider Provider(string? key = Key)
    {
        var options = new FoodAnalysisOptions { TimeoutSeconds = 5 };
        options.Gemini.ApiKey = key;
        options.Gemini.Model = "gemini-vision-test";
        return new GeminiFoodImageAnalysisProvider(_http.Client("https://gemini.test/v1beta/"), Options.Create(options));
    }

    private static FoodImageAnalysisProviderRequest Request() =>
        new(FoodAnalysisTestData.Jpeg(), "image/jpeg", FoodAnalysisPrompt.Instructions, FoodAnalysisPrompt.ResponseSchema);

    [Fact]
    public async Task SendsImageInline_WithJsonSchema_AndKeyOnlyInHeader()
    {
        _http.Respond(HttpStatusCode.OK, """
            {"candidates":[{"content":{"role":"model","parts":[{"text":"{\"containsFood\":false,","thought":false},{"text":"\"items\":[]}"}]},"finishReason":"STOP"}],
             "usageMetadata":{"promptTokenCount":1300,"candidatesTokenCount":40,"thoughtsTokenCount":10},"modelVersion":"gemini-vision-x"}
            """);

        var result = await Provider().AnalyzeAsync(Request());

        Assert.Equal("{\"containsFood\":false,\"items\":[]}", result.Json);
        Assert.Equal((1300, 50, "Gemini", "gemini-vision-x", false), (result.InputTokens, result.OutputTokens, result.Provider, result.Model, result.Refused));

        var (request, body) = Assert.Single(_http.Requests);
        Assert.Equal("https://gemini.test/v1beta/models/gemini-vision-test:generateContent", request.RequestUri!.ToString());
        Assert.Equal(Key, request.Headers.GetValues("x-goog-api-key").Single());
        Assert.DoesNotContain(Key, request.RequestUri.ToString());
        Assert.DoesNotContain(Key, body);

        using var json = JsonDocument.Parse(body);
        var root = json.RootElement;
        var parts = root.GetProperty("contents")[0].GetProperty("parts");
        var inline = parts[0].GetProperty("inline_data");
        Assert.Equal("image/jpeg", inline.GetProperty("mime_type").GetString());
        Assert.Equal(FoodAnalysisTestData.Jpeg(), Convert.FromBase64String(inline.GetProperty("data").GetString()!));
        var config = root.GetProperty("generationConfig");
        Assert.Equal("application/json", config.GetProperty("responseMimeType").GetString());
        Assert.Equal("OBJECT", config.GetProperty("responseSchema").GetProperty("type").GetString());
        Assert.True(config.GetProperty("responseSchema").GetProperty("properties").TryGetProperty("items", out _));
    }

    [Fact]
    public async Task SafetyBlock_IsReportedAsRefused()
    {
        _http.Respond(HttpStatusCode.OK, """{"candidates":[{"finishReason":"IMAGE_SAFETY"}]}""");

        var result = await Provider().AnalyzeAsync(Request());

        Assert.True(result.Refused);
        Assert.Equal(string.Empty, result.Json);
    }

    [Fact]
    public async Task QuotaExceeded_IsRateLimitedWithRetryDelay()
    {
        _http.Respond(HttpStatusCode.TooManyRequests, """
            {"error":{"code":429,"status":"RESOURCE_EXHAUSTED","details":[{"@type":"type.googleapis.com/google.rpc.RetryInfo","retryDelay":"21s"}]}}
            """);

        var ex = await Assert.ThrowsAsync<NutritionAiProviderException>(() => Provider().AnalyzeAsync(Request()));

        Assert.Equal((NutritionAiFailureKind.RateLimited, TimeSpan.FromSeconds(21)), (ex.Kind, ex.RetryAfter));
        Assert.DoesNotContain(Key, ex.Message);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError, NutritionAiFailureKind.Transient)]
    [InlineData(HttpStatusCode.BadRequest, NutritionAiFailureKind.Rejected)]
    [InlineData(HttpStatusCode.Forbidden, NutritionAiFailureKind.Rejected)]
    public async Task HttpErrors_AreClassified(HttpStatusCode status, NutritionAiFailureKind kind)
    {
        _http.Respond(status, """{"error":{"code":1,"message":"echo of request content","status":"X"}}""");

        var ex = await Assert.ThrowsAsync<NutritionAiProviderException>(() => Provider().AnalyzeAsync(Request()));

        Assert.Equal(kind, ex.Kind);
        Assert.DoesNotContain("echo of request content", ex.Message);
    }

    [Fact]
    public async Task SlowResponse_TimesOut()
    {
        var options = new FoodAnalysisOptions { TimeoutSeconds = 5 };
        options.Gemini.ApiKey = Key;
        _http.Respond(HttpStatusCode.OK, "{}", delay: TimeSpan.FromSeconds(30));
        var provider = new GeminiFoodImageAnalysisProvider(_http.Client("https://gemini.test/v1beta/"), Options.Create(options));

        var ex = await Assert.ThrowsAsync<NutritionAiProviderException>(() => provider.AnalyzeAsync(Request()));

        Assert.Equal(NutritionAiFailureKind.Timeout, ex.Kind);
    }

    [Fact]
    public async Task ClientCancellation_IsNotConvertedToProviderError()
    {
        _http.Respond(HttpStatusCode.OK, "{}", delay: TimeSpan.FromSeconds(30));
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Provider().AnalyzeAsync(Request(), cts.Token));
    }

    [Fact]
    public async Task MissingKey_IsNotConfigured_AndNoRequestIsSent()
    {
        var previous = Environment.GetEnvironmentVariable("GEMINI_API_KEY");
        Environment.SetEnvironmentVariable("GEMINI_API_KEY", null);
        try
        {
            var provider = Provider(key: null);
            Assert.False(provider.IsConfigured);
            await Assert.ThrowsAsync<NutritionAiProviderException>(() => provider.AnalyzeAsync(Request()));
            Assert.Empty(_http.Requests);
        }
        finally
        {
            Environment.SetEnvironmentVariable("GEMINI_API_KEY", previous);
        }
    }
}
