using System.Text.Json;
using System.Threading.RateLimiting;
using MerfitCustomerApi.Api.Extensions;
using MerfitCustomerApi.Business.Common;
using MerfitCustomerApi.Business.Common.Responses;
using Microsoft.AspNetCore.RateLimiting;

namespace MerfitCustomerApi.Api.Middleware;

/// <summary>
/// Fotograf analizi ucunun (POST /api/food-analysis/analyze) HTTP hiz siniri: kullanici basina (JWT'deki kimlik)
/// ve IP basina kayan pencere. Veritabanindaki gunluk analiz kotasindan once calisir ve saglayicinin ucretsiz
/// kotasini ani yuklenmelere karsi korur. Sinir sunucu ornegi basinadir (bkz. NutritionAssistantRateLimits).
/// </summary>
public static class FoodAnalysisRateLimits
{
    public const string AnalyzePath = "/api/food-analysis/analyze";

    private static readonly JsonSerializerOptions SerializerOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public static PartitionedRateLimiter<HttpContext> Create(FoodAnalysisOptions options)
    {
        var perUser = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        {
            if (!IsLimitedPath(context) || options.PerUserRequestsPerMinute <= 0)
            {
                return RateLimitPartition.GetNoLimiter("none");
            }
            var userId = context.User.GetUserId();
            return userId is null
                ? RateLimitPartition.GetNoLimiter("anonymous")
                : RateLimitPartition.GetSlidingWindowLimiter("food-user:" + userId, _ => Window(options.PerUserRequestsPerMinute));
        });

        var perIp = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        {
            if (!IsLimitedPath(context) || options.PerIpRequestsPerMinute <= 0)
            {
                return RateLimitPartition.GetNoLimiter("none");
            }
            var ip = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            return RateLimitPartition.GetSlidingWindowLimiter("food-ip:" + ip, _ => Window(options.PerIpRequestsPerMinute));
        });

        return PartitionedRateLimiter.CreateChained(perUser, perIp);
    }

    public static bool IsLimitedPath(HttpContext context) =>
        HttpMethods.IsPost(context.Request.Method)
        && context.Request.Path.Equals(AnalyzePath, StringComparison.OrdinalIgnoreCase);

    public static async ValueTask OnRejectedAsync(OnRejectedContext context, CancellationToken cancellationToken)
    {
        var response = context.HttpContext.Response;
        response.StatusCode = StatusCodes.Status429TooManyRequests;
        var retryAfter = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var wait) ? wait : TimeSpan.FromSeconds(15);
        response.Headers.RetryAfter = Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds)).ToString();
        response.ContentType = "application/json";

        var body = ApiResponse.Fail("Çok sık fotoğraf analizi yapıyorsun. Lütfen biraz bekleyip tekrar dene.");
        body.TraceId = context.HttpContext.TraceIdentifier;
        await response.WriteAsync(JsonSerializer.Serialize(body, body.GetType(), SerializerOptions), cancellationToken);
    }

    private static SlidingWindowRateLimiterOptions Window(int permitsPerMinute) => new()
    {
        PermitLimit = permitsPerMinute,
        Window = TimeSpan.FromMinutes(1),
        SegmentsPerWindow = 6,
        QueueLimit = 0,
        AutoReplenishment = true,
    };
}
