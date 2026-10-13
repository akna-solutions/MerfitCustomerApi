using System.Text.Json;
using System.Threading.RateLimiting;
using MerfitCustomerApi.Api.Extensions;
using MerfitCustomerApi.Business.Common;
using MerfitCustomerApi.Business.Common.Responses;
using Microsoft.AspNetCore.RateLimiting;

namespace MerfitCustomerApi.Api.Middleware;

/// <summary>
/// Beslenme asistani sohbet ucunun (POST /api/nutrition-assistant/chat) HTTP hiz siniri: kullanici basina
/// (JWT'deki kimlik) ve IP basina kayan pencere. Diger uclar sinirlanmaz. Bu sinir veritabanindaki gunluk
/// mesaj limitinden once calisir ve saglayici kotasini ani yuklenmelere karsi korur. Sinir sunucu ornegi
/// basinadir; vekil sunucu arkasinda ForwardedHeaders yapilandirilmazsa IP siniri tum istemcilere ortak uygulanir.
/// </summary>
public static class NutritionAssistantRateLimits
{
    public const string ChatPath = "/api/nutrition-assistant/chat";

    private static readonly JsonSerializerOptions SerializerOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public static PartitionedRateLimiter<HttpContext> Create(NutritionAssistantOptions options)
    {
        var perUser = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        {
            if (!IsChat(context) || options.PerUserRequestsPerMinute <= 0)
            {
                return RateLimitPartition.GetNoLimiter("none");
            }
            // Kimligi dogrulanmamis istek zaten 401 alir; kimlik yoksa IP siniri yeterlidir.
            var userId = context.User.GetUserId();
            return userId is null
                ? RateLimitPartition.GetNoLimiter("anonymous")
                : RateLimitPartition.GetSlidingWindowLimiter("user:" + userId, _ => Window(options.PerUserRequestsPerMinute));
        });

        var perIp = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        {
            if (!IsChat(context) || options.PerIpRequestsPerMinute <= 0)
            {
                return RateLimitPartition.GetNoLimiter("none");
            }
            var ip = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            return RateLimitPartition.GetSlidingWindowLimiter("ip:" + ip, _ => Window(options.PerIpRequestsPerMinute));
        });

        return PartitionedRateLimiter.CreateChained(perUser, perIp);
    }

    public static async ValueTask OnRejectedAsync(OnRejectedContext context, CancellationToken cancellationToken)
    {
        var response = context.HttpContext.Response;
        response.StatusCode = StatusCodes.Status429TooManyRequests;
        var retryAfter = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var wait) ? wait : TimeSpan.FromSeconds(10);
        response.Headers.RetryAfter = Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds)).ToString();
        response.ContentType = "application/json";

        var body = ApiResponse.Fail("Çok hızlı mesaj gönderiyorsun. Lütfen birkaç saniye bekleyip tekrar dene.");
        body.TraceId = context.HttpContext.TraceIdentifier;
        await response.WriteAsync(JsonSerializer.Serialize(body, body.GetType(), SerializerOptions), cancellationToken);
    }

    private static bool IsChat(HttpContext context) =>
        HttpMethods.IsPost(context.Request.Method)
        && context.Request.Path.Equals(ChatPath, StringComparison.OrdinalIgnoreCase);

    private static SlidingWindowRateLimiterOptions Window(int permitsPerMinute) => new()
    {
        PermitLimit = permitsPerMinute,
        Window = TimeSpan.FromMinutes(1),
        SegmentsPerWindow = 6,
        QueueLimit = 0,
        AutoReplenishment = true,
    };
}
