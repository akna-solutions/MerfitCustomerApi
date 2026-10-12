using System.Net;
using System.Text;
using MerfitCustomerApi.Business.Common;
using MerfitCustomerApi.Business.Interfaces.Services;
using MerfitCustomerApi.Business.Services.Notifications.Engine;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace MerfitCustomerApi.Business.Tests.Notifications;

public class ExpoPushNotificationServiceTests
{
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;
        public List<(string Path, string Body, string? Auth)> Requests { get; } = new();

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) => _respond = respond;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add((request.RequestUri!.AbsolutePath, await request.Content!.ReadAsStringAsync(cancellationToken), request.Headers.Authorization?.ToString()));
            return _respond(request);
        }
    }

    private static (ExpoPushNotificationService Service, StubHandler Handler) Create(Func<HttpRequestMessage, HttpResponseMessage> respond, string? accessToken = null)
    {
        var handler = new StubHandler(respond);
        var client = new HttpClient(handler) { BaseAddress = new Uri("https://exp.host/") };
        var options = Options.Create(new NotificationOptions { Expo = new ExpoPushOptions { AccessToken = accessToken } });
        return (new ExpoPushNotificationService(client, options, NullLogger<ExpoPushNotificationService>.Instance), handler);
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string json) => new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static PushMessage Message(string to) => new(to, "t", "b", new Dictionary<string, string> { ["screen"] = "nutrition" });

    [Fact]
    public async Task Send_MapsTicketsInOrder_OkIsAccepted_DeviceNotRegisteredIsRejected()
    {
        var (service, handler) = Create(_ => Json(HttpStatusCode.OK,
            """{"data":[{"status":"ok","id":"t-1"},{"status":"error","message":"not registered","details":{"error":"DeviceNotRegistered"}}]}"""),
            accessToken: "secret");

        var results = await service.SendAsync(new[] { Message("ExponentPushToken[a]"), Message("ExponentPushToken[b]") });

        Assert.Equal(PushSendOutcome.Accepted, results[0].Outcome);
        Assert.Equal("t-1", results[0].ProviderMessageId);
        Assert.Equal(PushSendOutcome.Rejected, results[1].Outcome);
        Assert.True(results[1].IsDeviceUnregistered);
        Assert.Equal("/--/api/v2/push/send", handler.Requests[0].Path);
        Assert.Contains("\"channelId\":\"default\"", handler.Requests[0].Body);
        Assert.Equal("Bearer secret", handler.Requests[0].Auth);
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task Send_RateLimitedOrServerError_IsTransient(HttpStatusCode status)
    {
        var (service, _) = Create(_ => Json(status, "{}"));

        var results = await service.SendAsync(new[] { Message("ExponentPushToken[a]") });

        Assert.Equal(PushSendOutcome.Transient, Assert.Single(results).Outcome);
    }

    [Fact]
    public async Task Send_NetworkFailure_IsTransient()
    {
        var (service, _) = Create(_ => throw new HttpRequestException("boom"));

        var results = await service.SendAsync(new[] { Message("ExponentPushToken[a]") });

        Assert.Equal(PushSendOutcome.Transient, Assert.Single(results).Outcome);
    }

    [Fact]
    public async Task Send_RequestLevelError_IsRejectedForWholeChunk()
    {
        var (service, _) = Create(_ => Json(HttpStatusCode.BadRequest, """{"errors":[{"code":"VALIDATION_ERROR","message":"bad"}]}"""));

        var results = await service.SendAsync(new[] { Message("ExponentPushToken[a]"), Message("ExponentPushToken[b]") });

        Assert.All(results, r => Assert.Equal(PushSendOutcome.Rejected, r.Outcome));
        Assert.All(results, r => Assert.Equal("VALIDATION_ERROR", r.ErrorCode));
    }

    [Fact]
    public async Task Send_SplitsIntoChunksOf100()
    {
        var (service, handler) = Create(req =>
        {
            var count = req.Content!.ReadAsStringAsync().Result.Split("\"to\"").Length - 1;
            return Json(HttpStatusCode.OK, "{\"data\":[" + string.Join(",", Enumerable.Repeat("{\"status\":\"ok\",\"id\":\"x\"}", count)) + "]}");
        });

        var results = await service.SendAsync(Enumerable.Range(0, 150).Select(i => Message($"ExponentPushToken[{i}]")).ToList());

        Assert.Equal(150, results.Count);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task GetReceipts_ParsesOkAndErrors()
    {
        var (service, _) = Create(_ => Json(HttpStatusCode.OK,
            """{"data":{"a":{"status":"ok"},"b":{"status":"error","message":"gone","details":{"error":"DeviceNotRegistered"}}}}"""));

        var receipts = await service.GetReceiptsAsync(new[] { "a", "b", "c" });

        Assert.True(receipts["a"].IsOk);
        Assert.True(receipts["b"].IsDeviceUnregistered);
        Assert.False(receipts.ContainsKey("c"));
    }

    [Theory]
    [InlineData("ExponentPushToken[xxxxxxxxxxxxxxxxxxxxxx]", true)]
    [InlineData("ExpoPushToken[xxxxxxxxxxxxxxxxxxxxxx]", true)]
    [InlineData("fcm:abc", false)]
    [InlineData("ExponentPushToken[]", false)]
    public void IsValidToken(string token, bool expected)
    {
        var (service, _) = Create(_ => Json(HttpStatusCode.OK, "{}"));
        Assert.Equal(expected, service.IsValidToken(token));
    }
}
