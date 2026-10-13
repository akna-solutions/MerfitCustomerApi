using System.Text.Json;
using System.Threading.RateLimiting;
using MerfitCustomerApi.Business.Common;
using MerfitCustomerApi.Business.Common.Responses;
using Microsoft.AspNetCore.RateLimiting;

namespace MerfitCustomerApi.Api.Middleware;

/// <summary>
/// E-posta dogrulama, sifre sifirlama ve kullanici adi kontrolu uclarinin IP basina HTTP hiz siniri (kaba kuvvet,
/// e-posta bombardimani ve kullanici adi taramasina karsi). Her uc kendi penceresine sahiptir; giris/kayit ve
/// diger uclar etkilenmez.
/// Hesap basina bekleme suresi ve saatlik sinir ayrica veritabaninda uygulanir (bkz. AccountEmailService).
/// Sinir sunucu ornegi basinadir; vekil sunucu arkasinda ForwardedHeaders yapilandirilmazsa IP siniri tum
/// istemcilere ortak uygulanir.
/// </summary>
public static class AccountEmailRateLimits
{
    public const string UsernameAvailabilityPath = "/api/auth/username-availability";

    public static readonly string[] LimitedPaths =
    {
        "/api/auth/email-verification/resend",
        "/api/auth/email-verification/confirm",
        "/api/auth/password/forgot",
        "/api/auth/password/reset/verify-code",
        "/api/auth/password/reset",
    };

    private static readonly JsonSerializerOptions SerializerOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public static PartitionedRateLimiter<HttpContext> Create(AccountEmailOptions options) =>
        PartitionedRateLimiter.Create<HttpContext, string>(context =>
        {
            var path = MatchPath(context);
            var permitLimit = path == UsernameAvailabilityPath
                ? options.UsernameCheckRequestsPerWindow
                : options.PerIpRequestsPerWindow;
            if (path is null || permitLimit <= 0)
            {
                return RateLimitPartition.GetNoLimiter("none");
            }

            var ip = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            return RateLimitPartition.GetSlidingWindowLimiter($"account-email:{path}:{ip}", _ => new SlidingWindowRateLimiterOptions
            {
                PermitLimit = permitLimit,
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
        var requestPath = context.Request.Path.Value?.TrimEnd('/');
        if (HttpMethods.IsGet(context.Request.Method))
        {
            return string.Equals(requestPath, UsernameAvailabilityPath, StringComparison.OrdinalIgnoreCase)
                ? UsernameAvailabilityPath
                : null;
        }

        if (!HttpMethods.IsPost(context.Request.Method))
        {
            return null;
        }

        return LimitedPaths.FirstOrDefault(p => string.Equals(p, requestPath, StringComparison.OrdinalIgnoreCase));
    }
}
