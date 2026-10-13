using System.Net;
using System.Text.Json;
using MerfitCustomerApi.Business.Common;
using MerfitCustomerApi.Business.Interfaces.Services;
using MerfitCustomerApi.Business.Services.NutritionAssistant;
using MerfitCustomerApi.Domain.Entities.Enums;
using Microsoft.Extensions.Options;
using Xunit;

namespace MerfitCustomerApi.Business.Tests.NutritionAssistant;

/// <summary>Groq saglayicisinin istek bicimi, yanit ayristirma ve hata siniflandirmasi (gercek anahtar gerekmez).</summary>
public class GroqNutritionAiProviderTests
{
    private const string Key = "gsk_test_secret_key";
    private readonly StubHttpHandler _http = new();

    private GroqNutritionAiProvider Provider(Action<NutritionAssistantOptions>? configure = null)
    {
        var options = new NutritionAssistantOptions { TimeoutSeconds = 5, MaxOutputTokens = 700 };
        options.Groq.ApiKey = Key;
        configure?.Invoke(options);
        return new GroqNutritionAiProvider(_http.Client("https://api.groq.test/openai/v1/"), Options.Create(options));
    }

    internal static NutritionAssistantModelRequest Request() => new(
        "talimatlar",
        "baglam: protein 65 / 130 g",
        new[]
        {
            new NutritionAssistantModelMessage(NutritionAssistantMessageRole.User, "Tavuk yemek istemiyorum."),
            new NutritionAssistantModelMessage(NutritionAssistantMessageRole.Assistant, "Anlaşıldı."),
            new NutritionAssistantModelMessage(NutritionAssistantMessageRole.User, "Akşam ne yiyebilirim?"),
        });

    private const string Ok = """
        {"id":"chatcmpl-1","model":"openai/gpt-oss-120b",
         "choices":[{"index":0,"message":{"role":"assistant","content":" Mercimek çorbası ve yoğurt deneyebilirsin. ","reasoning":null},"finish_reason":"stop"}],
         "usage":{"prompt_tokens":812,"completion_tokens":96,"total_tokens":908}}
        """;

    [Fact]
    public async Task SendsOpenAiCompatibleRequest_WithKeyOnlyInHeader()
    {
        _http.Respond(HttpStatusCode.OK, Ok);

        var result = await Provider().CompleteAsync(Request());

        Assert.Equal("Mercimek çorbası ve yoğurt deneyebilirsin.", result.Text);
        Assert.Equal((812, 96, "stop", "openai/gpt-oss-120b", "Groq", false),
            (result.InputTokens, result.OutputTokens, result.StopReason, result.Model, result.Provider, result.Refused));

        var (request, body) = Assert.Single(_http.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("https://api.groq.test/openai/v1/chat/completions", request.RequestUri!.ToString());
        Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
        Assert.Equal(Key, request.Headers.Authorization.Parameter);
        Assert.DoesNotContain(Key, body);
        Assert.DoesNotContain(Key, request.RequestUri.ToString());

        using var json = JsonDocument.Parse(body);
        var root = json.RootElement;
        Assert.Equal("openai/gpt-oss-120b", root.GetProperty("model").GetString());
        Assert.Equal(700, root.GetProperty("max_completion_tokens").GetInt32());
        Assert.Equal("low", root.GetProperty("reasoning_effort").GetString());
        Assert.False(root.GetProperty("include_reasoning").GetBoolean());
        Assert.False(root.GetProperty("stream").GetBoolean());
        var messages = root.GetProperty("messages");
        Assert.Equal(new[] { "system", "system", "user", "assistant", "user" }, messages.EnumerateArray().Select(m => m.GetProperty("role").GetString()));
        Assert.Equal("talimatlar", messages[0].GetProperty("content").GetString());
        Assert.Equal("baglam: protein 65 / 130 g", messages[1].GetProperty("content").GetString());
        Assert.Equal("Tavuk yemek istemiyorum.", messages[2].GetProperty("content").GetString());
    }

    [Fact]
    public async Task ModelWithoutReasoning_OmitsReasoningParameters()
    {
        _http.Respond(HttpStatusCode.OK, Ok);

        await Provider(o => { o.Groq.Model = "llama-3.3-70b-versatile"; o.Groq.ReasoningEffort = null; }).CompleteAsync(Request());

        var body = _http.Requests[0].Body;
        Assert.Contains("llama-3.3-70b-versatile", body);
        Assert.DoesNotContain("reasoning_effort", body);
        Assert.DoesNotContain("include_reasoning", body);
    }

    [Fact]
    public async Task RateLimit_ReadsRetryAfter()
    {
        _http.Respond(HttpStatusCode.TooManyRequests,
            """{"error":{"message":"Rate limit reached for model in organization on tokens per minute (TPM): Limit 8000","type":"tokens","code":"rate_limit_exceeded"}}""",
            r => r.Headers.TryAddWithoutValidation("retry-after", "7"));

        var ex = await Assert.ThrowsAsync<NutritionAiProviderException>(() => Provider().CompleteAsync(Request()));

        Assert.Equal(NutritionAiFailureKind.RateLimited, ex.Kind);
        Assert.Equal(TimeSpan.FromSeconds(7), ex.RetryAfter);
        Assert.Equal("rate_limit_exceeded", ex.ErrorCode);
        Assert.DoesNotContain("organization", ex.Message); // saglayici mesaj metni tasinmaz
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError, NutritionAiFailureKind.Transient)]
    [InlineData(HttpStatusCode.ServiceUnavailable, NutritionAiFailureKind.Transient)]
    [InlineData(HttpStatusCode.Unauthorized, NutritionAiFailureKind.Rejected)]
    [InlineData(HttpStatusCode.NotFound, NutritionAiFailureKind.Rejected)]
    public async Task HttpErrors_AreClassified(HttpStatusCode status, NutritionAiFailureKind kind)
    {
        _http.Respond(status, """{"error":{"message":"Invalid API Key","type":"invalid_request_error","code":"invalid_api_key"}}""");

        var ex = await Assert.ThrowsAsync<NutritionAiProviderException>(() => Provider().CompleteAsync(Request()));

        Assert.Equal(kind, ex.Kind);
        Assert.Equal((int)status, ex.StatusCode);
        Assert.DoesNotContain(Key, ex.ToString());
        Assert.Single(_http.Requests); // saglayici kendi icinde tekrar denemez
    }

    [Fact]
    public async Task SlowResponse_TimesOut()
    {
        _http.Respond(HttpStatusCode.OK, Ok, delay: TimeSpan.FromSeconds(30));

        var ex = await Assert.ThrowsAsync<NutritionAiProviderException>(() => Provider(o => o.TimeoutSeconds = 1).CompleteAsync(Request()));

        Assert.Equal(NutritionAiFailureKind.Timeout, ex.Kind);
    }

    [Fact]
    public async Task CallerCancellation_IsNotReportedAsProviderFailure()
    {
        _http.Respond(HttpStatusCode.OK, Ok, delay: TimeSpan.FromSeconds(30));
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Provider().CompleteAsync(Request(), cts.Token));
    }

    [Fact]
    public async Task ContentFilter_IsReportedAsRefusal()
    {
        _http.Respond(HttpStatusCode.OK, """{"model":"m","choices":[{"message":{"content":""},"finish_reason":"content_filter"}],"usage":{"prompt_tokens":1,"completion_tokens":0}}""");

        Assert.True((await Provider().CompleteAsync(Request())).Refused);
    }

    [Fact]
    public void WithoutKey_IsNotConfigured()
    {
        var previous = Environment.GetEnvironmentVariable(GroqNutritionAiProvider.ApiKeyEnvironmentVariable);
        Environment.SetEnvironmentVariable(GroqNutritionAiProvider.ApiKeyEnvironmentVariable, null);
        try
        {
            Assert.False(Provider(o => o.Groq.ApiKey = null).IsConfigured);
        }
        finally
        {
            Environment.SetEnvironmentVariable(GroqNutritionAiProvider.ApiKeyEnvironmentVariable, previous);
        }
    }
}
