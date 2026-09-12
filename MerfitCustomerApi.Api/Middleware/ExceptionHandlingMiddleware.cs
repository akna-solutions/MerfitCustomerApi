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

            ConflictException conflictException => (
                HttpStatusCode.Conflict,
                ApiResponse.Fail(conflictException.Message)),

            UnauthorizedException unauthorizedException => (
                HttpStatusCode.Unauthorized,
                ApiResponse.Fail(unauthorizedException.Message)),

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
