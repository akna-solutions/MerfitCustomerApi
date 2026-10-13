using System.Net;
using System.Text.Json;
using MerfitCustomerApi.Business.Common;
using MerfitCustomerApi.Business.Interfaces.Services;
using MerfitCustomerApi.Business.Services.NutritionAssistant;
using Microsoft.Extensions.Options;
using Xunit;

namespace MerfitCustomerApi.Business.Tests.NutritionAssistant;

/// <summary>Gemini saglayicisinin istek bicimi, yanit ayristirma ve hata siniflandirmasi.</summary>
public class GeminiNutritionAiProviderTests
{
    private const string Key = "AIza_test_secret_key";
    private readonly StubHttpHandler _http = new();

    private GeminiNutritionAiProvider Provider()
    {
        var options = new NutritionAssistantOptions { TimeoutSeconds = 5 };
        options.Gemini.ApiKey = Key;
        options.Gemini.Model = "gemini-flash-latest";
        return new GeminiNutritionAiProvider(_http.Client("https://gemini.test/v1beta/"), Options.Create(options));
    }

    [Fact]
    public async Task SendsGenerateContentRequest_WithKeyInHeaderNotUrl()
    {
        _http.Respond(HttpStatusCode.OK, """
            {"candidates":[{"content":{"role":"model","parts":[{"text":"düşünce","thought":true},{"text":"Mercimek köftesi iyi bir seçenek."}]},"finishReason":"STOP"}],
             "usageMetadata":{"promptTokenCount":700,"candidatesTokenCount":40,"thoughtsTokenCount":60},"modelVersion":"gemini-flash-x"}
            """);

        var result = await Provider().CompleteAsync(GroqNutritionAiProviderTests.Request());

        Assert.Equal("Mercimek köftesi iyi bir seçenek.", result.Text);
        Assert.Equal((700, 100, "Gemini", false), (result.InputTokens, result.OutputTokens, result.Provider, result.Refused));

        var (request, body) = Assert.Single(_http.Requests);
        Assert.Equal("https://gemini.test/v1beta/models/gemini-flash-latest:generateContent", request.RequestUri!.ToString());
        Assert.Equal(Key, request.Headers.GetValues("x-goog-api-key").Single());
        Assert.DoesNotContain(Key, request.RequestUri.ToString());
        Assert.DoesNotContain(Key, body);

        using var json = JsonDocument.Parse(body);
        var root = json.RootElement;
        Assert.Equal(2, root.GetProperty("systemInstruction").GetProperty("parts").GetArrayLength());
        Assert.Equal(new[] { "user", "model", "user" }, root.GetProperty("contents").EnumerateArray().Select(c => c.GetProperty("role").GetString()));
        Assert.Equal(1024, root.GetProperty("generationConfig").GetProperty("maxOutputTokens").GetInt32());
    }

    [Fact]
    public async Task QuotaExceeded_ReadsRetryDelayFromBody()
    {
        _http.Respond(HttpStatusCode.TooManyRequests, """
            {"error":{"code":429,"message":"quota","status":"RESOURCE_EXHAUSTED",
             "details":[{"@type":"type.googleapis.com/google.rpc.RetryInfo","retryDelay":"13s"}]}}
            """);

        var ex = await Assert.ThrowsAsync<NutritionAiProviderException>(() => Provider().CompleteAsync(GroqNutritionAiProviderTests.Request()));

        Assert.Equal(NutritionAiFailureKind.RateLimited, ex.Kind);
        Assert.Equal(TimeSpan.FromSeconds(13), ex.RetryAfter);
    }

    [Fact]
    public async Task SafetyBlock_IsReportedAsRefusal()
    {
        _http.Respond(HttpStatusCode.OK, """{"candidates":[{"finishReason":"SAFETY"}],"usageMetadata":{"promptTokenCount":5}}""");

        var result = await Provider().CompleteAsync(GroqNutritionAiProviderTests.Request());

        Assert.True(result.Refused);
        Assert.Equal(string.Empty, result.Text);
    }

    [Fact]
    public async Task ServerError_IsTransient()
    {
        _http.Respond(HttpStatusCode.InternalServerError, """{"error":{"code":500,"status":"INTERNAL"}}""");

        var ex = await Assert.ThrowsAsync<NutritionAiProviderException>(() => Provider().CompleteAsync(GroqNutritionAiProviderTests.Request()));

        Assert.Equal(NutritionAiFailureKind.Transient, ex.Kind);
    }
}
