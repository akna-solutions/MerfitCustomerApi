using System.Text.Json;
using System.Threading.RateLimiting;
using MerfitCustomerApi.Business.Common;
using MerfitCustomerApi.Business.Common.Responses;
using Microsoft.AspNetCore.RateLimiting;

namespace MerfitCustomerApi.Api.Middleware;

/// <summary>
/// E-posta dogrulama ve sifre sifirlama uclarinin IP basina HTTP hiz siniri (kaba kuvvet ve e-posta
/// bombardimanina karsi). Her uc kendi penceresine sahiptir; giris/kayit ve diger uclar etkilenmez.
/// Hesap basina bekleme suresi ve saatlik sinir ayrica veritabaninda uygulanir (bkz. AccountEmailService).
/// Sinir sunucu ornegi basinadir; vekil sunucu arkasinda ForwardedHeaders yapilandirilmazsa IP siniri tum
/// istemcilere ortak uygulanir.
/// </summary>
public static class AccountEmailRateLimits
{
    public static readonly string[] LimitedPaths =
    {
        "/api/auth/email-verification/resend",
        "/api/auth/email-verification/confirm",
        "/api/auth/password/forgot",
        "/api/auth/password/reset/validate",
        "/api/auth/password/reset",
    };

    private static readonly JsonSerializerOptions SerializerOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public static PartitionedRateLimiter<HttpContext> Create(AccountEmailOptions options) =>
        PartitionedRateLimiter.Create<HttpContext, string>(context =>
        {
            var path = MatchPath(context);
            if (path is null || options.PerIpRequestsPerWindow <= 0)
            {
                return RateLimitPartition.GetNoLimiter("none");
            }

            var ip = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            return RateLimitPartition.GetSlidingWindowLimiter($"account-email:{path}:{ip}", _ => new SlidingWindowRateLimiterOptions
            {
                PermitLimit = options.PerIpRequestsPerWindow,
                Window = options.RateLimitWindow,
                SegmentsPerWindow = 5,
                QueueLimit = 0,
                AutoReplenishment = true,
            });
        });

    public static bool IsLimitedPath(HttpContext context) => MatchPath(context) is not null;

    public static async ValueTask OnRejectedAsync(OnRejectedContext context, CancellationToken cancellationToken)
    {
        var response = context.HttpContext.Response;
        response.StatusCode = StatusCodes.Status429TooManyRequests;
        var retryAfter = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var wait) ? wait : TimeSpan.FromMinutes(1);
        response.Headers.RetryAfter = Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds)).ToString();
        response.ContentType = "application/json";

        var body = ApiResponse.Fail("Çok fazla deneme yaptın. Lütfen biraz bekleyip tekrar dene.");
        body.TraceId = context.HttpContext.TraceIdentifier;
        await response.WriteAsync(JsonSerializer.Serialize(body, body.GetType(), SerializerOptions), cancellationToken);
    }

    private static string? MatchPath(HttpContext context)
    {
        if (!HttpMethods.IsPost(context.Request.Method))
        {
            return null;
        }

        var requestPath = context.Request.Path.Value?.TrimEnd('/');
        return LimitedPaths.FirstOrDefault(p => string.Equals(p, requestPath, StringComparison.OrdinalIgnoreCase));
    }
}
