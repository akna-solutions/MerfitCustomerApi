using System.Net;
using System.Text.Json;
using MerfitCustomerApi.Business.Common.Responses;
using MerfitCustomerApi.Domain.Exceptions;

namespace MerfitCustomerApi.Api.Middleware;

/// <summary>
/// Pipeline'da yakalanmayan tum istisnalari yakalayip, madde 38'de tanimlanan HTTP durum kodu
/// kurallarina gore tutarli bir <see cref="ApiResponse"/> govdesine ceviren global middleware.
/// Repository'de daha once herhangi bir global exception handler bulunmadigindan bu middleware
/// tamamen eklentiseldir (additive); mevcut davranisi bozmaz, sadece daha once ele alinmayan
/// hatalari da duzgun bir JSON govdesiyle dondurur.
/// </summary>
public class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            // Istemci istegi iptal etti (orn. mobilde sohbet penceresi kapandi); yanit okunmayacak.
            _logger.LogInformation("Istek istemci tarafindan iptal edildi. TraceId: {TraceId}", context.TraceIdentifier);
            if (!context.Response.HasStarted)
            {
                context.Response.StatusCode = 499;
            }
        }
        catch (Exception exception)
        {
            await HandleExceptionAsync(context, exception);
        }
    }

    private async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        var traceId = context.TraceIdentifier;

        var (statusCode, response) = exception switch
        {
            AppValidationException validationException => (
                HttpStatusCode.BadRequest,
                ApiResponse.Fail(
                    validationException.Message,
                    validationException.Errors.SelectMany(kvp => kvp.Value))),

            ForbiddenException forbiddenException => (
                HttpStatusCode.Forbidden,
                ApiResponse.Fail(forbiddenException.Message)),

            NotFoundException notFoundException => (
                HttpStatusCode.NotFound,
                ApiResponse.Fail(notFoundException.Message)),

            // Kullanici onayi gereken hedef uyarisi: istemci Errors icindeki kodla uyari ekranini acar.
            LowBodyFatConfirmationRequiredException lowBodyFatException => (
                HttpStatusCode.Conflict,
                ApiResponse.Fail(lowBodyFatException.Message, new[] { LowBodyFatConfirmationRequiredException.Code })),

            ConflictException conflictException => (
                HttpStatusCode.Conflict,
                ApiResponse.Fail(conflictException.Message, conflictException.Code is null ? null : new[] { conflictException.Code })),

            TooManyRequestsException tooManyRequestsException => (
                HttpStatusCode.TooManyRequests,
                ApiResponse.Fail(tooManyRequestsException.Message)),

            UnauthorizedException unauthorizedException => (
                HttpStatusCode.Unauthorized,
                ApiResponse.Fail(unauthorizedException.Message)),

            ExternalServiceUnavailableException externalServiceException => (
                HttpStatusCode.ServiceUnavailable,
                ApiResponse.Fail(externalServiceException.Message)),

            // Govde boyut siniri ([RequestSizeLimit]) asildi ya da istek govdesi okunamadi (orn. kesilmis yukleme).
            BadHttpRequestException badRequestException => (
                (HttpStatusCode)badRequestException.StatusCode,
                ApiResponse.Fail(badRequestException.StatusCode == StatusCodes.Status413PayloadTooLarge
                    ? "Gönderilen dosya çok büyük."
                    : "İstek gövdesi okunamadı.")),

            // Multipart form okunamadi (bozuk sinir, form limiti asimi).
            InvalidDataException => (
                HttpStatusCode.BadRequest,
                ApiResponse.Fail("İstek gövdesi okunamadı.")),

            _ => (HttpStatusCode.InternalServerError, HandleUnexpected(exception, traceId)),
        };

        response.TraceId = traceId;

        if (statusCode == HttpStatusCode.InternalServerError)
        {
            _logger.LogError(exception, "Islenmemis istisna. TraceId: {TraceId}", traceId);
        }
        else
        {
            _logger.LogWarning(exception, "Istek {StatusCode} ile sonuclandi. TraceId: {TraceId}", (int)statusCode, traceId);
        }

        context.Response.ContentType = "application/json";
        context.Response.StatusCode = (int)statusCode;
        var retryAfter = exception switch
        {
            ExternalServiceUnavailableException { RetryAfter: { } serviceRetryAfter } => serviceRetryAfter,
            TooManyRequestsException { RetryAfter: { } limitRetryAfter } => limitRetryAfter,
            _ => (TimeSpan?)null,
        };
        if (retryAfter is { } wait)
        {
            context.Response.Headers.RetryAfter = Math.Max(1, (int)Math.Ceiling(wait.TotalSeconds)).ToString();
        }

        await context.Response.WriteAsync(JsonSerializer.Serialize(response, response.GetType(), SerializerOptions));
    }

    /// <summary>
    /// Beklenmeyen (domain disi) istisnalarda istemciye dahili detay/stack trace sizdirmadan
    /// genel bir hata mesaji ve izlenebilirlik icin TraceId doner.
    /// </summary>
    private static ApiResponse HandleUnexpected(Exception exception, string traceId)
    {
        return ApiResponse.Fail("Beklenmeyen bir hata olustu. Lutfen daha sonra tekrar deneyin.");
    }
}
