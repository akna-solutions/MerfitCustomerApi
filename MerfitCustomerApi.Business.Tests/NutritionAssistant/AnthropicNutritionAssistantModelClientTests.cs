using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using MerfitCustomerApi.Business.Common;
using MerfitCustomerApi.Business.Interfaces.Services;
using MerfitCustomerApi.Business.Services.NutritionAssistant;
using MerfitCustomerApi.Domain.Entities.Enums;
using MerfitCustomerApi.Domain.Exceptions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace MerfitCustomerApi.Business.Tests.NutritionAssistant;

/// <summary>
/// Anthropic istemcisinin gercek SDK ile urettigi HTTP istegini yerel sahte bir sunucuya karsi dogrular:
/// model/effort/sistem bloklari/geri donus (fallback) ayari, anahtarin yalnizca header'da gitmesi,
/// yanit ayristirma ve hata/zaman asimi esleme.
/// </summary>
public sealed class AnthropicNutritionAssistantModelClientTests : IDisposable
{
    private readonly HttpListener _listener = new();
    private readonly string _baseUrl;
    private readonly List<(string Path, Dictionary<string, string> Headers, string Body)> _requests = new();

    public AnthropicNutritionAssistantModelClientTests()
    {
        var port = FreePort();
        _baseUrl = $"http://127.0.0.1:{port}";
        _listener.Prefixes.Add(_baseUrl + "/");
        _listener.Start();
    }

    public void Dispose() => _listener.Close();

    private static int FreePort()
    {
        var socket = new TcpListener(IPAddress.Loopback, 0);
        socket.Start();
        var port = ((IPEndPoint)socket.LocalEndpoint).Port;
        socket.Stop();
        return port;
    }

    private Task Serve(int status, string body, int times = 1, TimeSpan? delay = null) => Task.Run(async () =>
    {
        for (var i = 0; i < times; i++)
        {
            var ctx = await _listener.GetContextAsync();
            using var reader = new StreamReader(ctx.Request.InputStream, Encoding.UTF8);
            var headers = ctx.Request.Headers.AllKeys.Where(k => k is not null).ToDictionary(k => k!.ToLowerInvariant(), k => ctx.Request.Headers[k]!);
            var requestBody = await reader.ReadToEndAsync();
            lock (_requests) _requests.Add((ctx.Request.Url!.AbsolutePath, headers, requestBody));
            if (delay is { } d) await Task.Delay(d);
            try
            {
                ctx.Response.StatusCode = status;
                ctx.Response.ContentType = "application/json";
                var bytes = Encoding.UTF8.GetBytes(body);
                await ctx.Response.OutputStream.WriteAsync(bytes);
                ctx.Response.Close();
            }
            catch (Exception)
            {
                // Istemci zaman asiminda baglantiyi kapatmis olabilir.
            }
        }
    });

    private AnthropicNutritionAssistantModelClient Client(Action<NutritionAssistantOptions>? configure = null)
    {
        var options = new NutritionAssistantOptions { ApiKey = "test-key", BaseUrl = _baseUrl, TimeoutSeconds = 5 };
        configure?.Invoke(options);
        return new AnthropicNutritionAssistantModelClient(Options.Create(options), NullLogger<AnthropicNutritionAssistantModelClient>.Instance);
    }

    private static NutritionAssistantModelRequest Request() => new(
        "talimatlar",
        "baglam: protein 65 g",
        new[]
        {
            new NutritionAssistantModelMessage(NutritionAssistantMessageRole.User, "Bugün proteinim düşük mü?"),
            new NutritionAssistantModelMessage(NutritionAssistantMessageRole.Assistant, "Evet."),
            new NutritionAssistantModelMessage(NutritionAssistantMessageRole.User, "Akşam ne yiyebilirim?"),
        });

    private const string OkResponse = """
        {"id":"msg_1","type":"message","role":"assistant","model":"claude-opus-5-5",
         "content":[{"type":"thinking","thinking":"","signature":"s"},{"type":"text","text":"Yoğurt ve yumurta deneyebilirsin."}],
         "stop_reason":"end_turn","stop_sequence":null,
         "usage":{"input_tokens":900,"output_tokens":45,"cache_read_input_tokens":300,"cache_creation_input_tokens":0}}
        """;

    [Fact]
    public async Task SendsExpectedRequest_AndParsesTextAndUsage()
    {
        var server = Serve(200, OkResponse);

        var result = await Client().CompleteAsync(Request());
        await server;

        Assert.Equal("Yoğurt ve yumurta deneyebilirsin.", result.Text);
        Assert.Equal(1200, result.InputTokens);
        Assert.Equal(45, result.OutputTokens);
        Assert.Equal("end_turn", result.StopReason);
        Assert.Equal("claude-opus-5-5", result.Model);
        Assert.False(result.Refused);

        var (path, headers, body) = Assert.Single(_requests);
        Assert.Equal("/v1/messages", path);
        Assert.Equal("test-key", headers["x-api-key"]);
        Assert.Contains("server-side-fallback-2026-07-01", headers["anthropic-beta"]);

        using var json = JsonDocument.Parse(body);
        var root = json.RootElement;
        Assert.Equal("claude-opus-5-5", root.GetProperty("model").GetString());
        Assert.Equal("default", root.GetProperty("fallbacks").GetString());
        Assert.Equal("low", root.GetProperty("output_config").GetProperty("effort").GetString());
        Assert.Equal(2048, root.GetProperty("max_tokens").GetInt32());
        Assert.False(root.TryGetProperty("thinking", out _));
        var system = root.GetProperty("system");
        Assert.Equal("talimatlar", system[0].GetProperty("text").GetString());
        Assert.Equal("ephemeral", system[0].GetProperty("cache_control").GetProperty("type").GetString());
        Assert.Equal("baglam: protein 65 g", system[1].GetProperty("text").GetString());
        var messages = root.GetProperty("messages");
        Assert.Equal(3, messages.GetArrayLength());
        Assert.Equal("assistant", messages[1].GetProperty("role").GetString());
        Assert.DoesNotContain("test-key", body);
    }

    [Fact]
    public async Task FallbackDisabled_SendsNoFallbackParameter()
    {
        var server = Serve(200, OkResponse);

        await Client(o => o.EnableRefusalFallback = false).CompleteAsync(Request());
        await server;

        var (_, headers, body) = Assert.Single(_requests);
        Assert.False(headers.ContainsKey("anthropic-beta") && headers["anthropic-beta"].Contains("server-side-fallback"));
        Assert.DoesNotContain("fallbacks", body);
    }

    [Fact]
    public async Task Refusal_IsReported()
    {
        var server = Serve(200, """
            {"id":"msg_2","type":"message","role":"assistant","model":"claude-opus-5-5","content":[],
             "stop_reason":"refusal","stop_sequence":null,"usage":{"input_tokens":10,"output_tokens":0}}
            """);

        var result = await Client().CompleteAsync(Request());
        await server;

        Assert.True(result.Refused);
        Assert.Equal(string.Empty, result.Text);
    }

    [Fact]
    public async Task ServerError_MapsToServiceUnavailable()
    {
        var server = Serve(500, """{"type":"error","error":{"type":"api_error","message":"boom"}}""", times: 2);

        var ex = await Assert.ThrowsAsync<ExternalServiceUnavailableException>(() => Client().CompleteAsync(Request()));
        await server;

        Assert.Contains("yanıt veremiyor", ex.Message);
    }

    [Fact]
    public async Task InvalidKey_MapsToServiceUnavailable()
    {
        var server = Serve(401, """{"type":"error","error":{"type":"authentication_error","message":"invalid x-api-key"}}""");

        await Assert.ThrowsAsync<ExternalServiceUnavailableException>(() => Client().CompleteAsync(Request()));
        await server;
    }

    [Fact]
    public async Task SlowProvider_TimesOut()
    {
        _ = Serve(200, OkResponse, times: 2, delay: TimeSpan.FromSeconds(12));

        var ex = await Assert.ThrowsAsync<ExternalServiceUnavailableException>(() => Client().CompleteAsync(Request()));

        Assert.Contains("gecikti", ex.Message);
    }

    [Fact]
    public async Task ClientCancellation_IsPropagatedAsCancellation()
    {
        _ = Serve(200, OkResponse, delay: TimeSpan.FromSeconds(10));
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Client().CompleteAsync(Request(), cts.Token));
    }

    [Fact]
    public void WithoutKey_IsNotConfigured()
    {
        var previous = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
        Environment.SetEnvironmentVariable("ANTHROPIC_API_KEY", null);
        try
        {
            Assert.False(Client(o => o.ApiKey = null).IsConfigured);
        }
        finally
        {
            Environment.SetEnvironmentVariable("ANTHROPIC_API_KEY", previous);
        }
    }
}
