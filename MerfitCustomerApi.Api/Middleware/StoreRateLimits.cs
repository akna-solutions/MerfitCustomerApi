using System.Text.Json;
using System.Threading.RateLimiting;
using MerfitCustomerApi.Api.Extensions;
using MerfitCustomerApi.Business.Common;
using MerfitCustomerApi.Business.Common.Responses;
using Microsoft.AspNetCore.RateLimiting;

namespace MerfitCustomerApi.Api.Middleware;

/// <summary>
/// MB Shop yazma uclarinin kullanici basina (JWT) dakikalik hiz siniri: siparis olusturma ayri ve daha dar bir
/// pencereye, sepet/favori/adres yazmalari ortak bir pencereye sahiptir. Okuma uclari sinirlanmaz. Sinir sunucu
/// ornegi basinadir (bkz. NutritionAssistantRateLimits).
/// </summary>
public static class StoreRateLimits
{
    public const string OrdersPath = "/api/store/orders";
    private const string StorePrefix = "/api/store/";

    private static readonly JsonSerializerOptions SerializerOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public static PartitionedRateLimiter<HttpContext> Create(StoreOptions options) =>
        PartitionedRateLimiter.Create<HttpContext, string>(context =>
        {
            var kind = Classify(context);
            var permits = kind switch
            {
                "order" => options.OrderRequestsPerMinute,
                "write" => options.WriteRequestsPerMinute,
                _ => 0,
            };
            var userId = context.User.GetUserId();
            if (kind is null || permits <= 0 || userId is null)
            {
                return RateLimitPartition.GetNoLimiter("none");
            }

            return RateLimitPartition.GetSlidingWindowLimiter($"store-{kind}:{userId}", _ => new SlidingWindowRateLimiterOptions
            {
                PermitLimit = permits,
                Window = TimeSpan.FromMinutes(1),
                SegmentsPerWindow = 6,
                QueueLimit = 0,
                AutoReplenishment = true,
            });
        });

    public static bool IsLimitedPath(HttpContext context) => Classify(context) is not null;

    public static async ValueTask OnRejectedAsync(OnRejectedContext context, CancellationToken cancellationToken)
    {
        var response = context.HttpContext.Response;
        response.StatusCode = StatusCodes.Status429TooManyRequests;
        var retryAfter = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var wait) ? wait : TimeSpan.FromSeconds(10);
        response.Headers.RetryAfter = Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds)).ToString();
        response.ContentType = "application/json";

        var body = ApiResponse.Fail("Çok hızlı işlem yapıyorsun. Lütfen birkaç saniye bekleyip tekrar dene.");
        body.TraceId = context.HttpContext.TraceIdentifier;
        await response.WriteAsync(JsonSerializer.Serialize(body, body.GetType(), SerializerOptions), cancellationToken);
    }

    /// <summary>"order" (siparis olusturma), "write" (diger store yazmalari) veya null (sinirsiz).</summary>
    private static string? Classify(HttpContext context)
    {
        if (HttpMethods.IsGet(context.Request.Method) || HttpMethods.IsHead(context.Request.Method)) return null;
        var path = context.Request.Path.Value?.TrimEnd('/');
        if (path is null || !path.StartsWith(StorePrefix, StringComparison.OrdinalIgnoreCase)) return null;
        if (HttpMethods.IsPost(context.Request.Method) && string.Equals(path, OrdersPath, StringComparison.OrdinalIgnoreCase)) return "order";
        return "write";
    }
}
