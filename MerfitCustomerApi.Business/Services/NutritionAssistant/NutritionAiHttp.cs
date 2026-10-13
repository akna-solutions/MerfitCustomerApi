using System.Globalization;
using System.Net;
using System.Text.Json;
using MerfitCustomerApi.Business.Common;
using MerfitCustomerApi.Business.Interfaces.Services;

namespace MerfitCustomerApi.Business.Services.NutritionAssistant;

/// <summary>HTTP tabanli saglayicilarin ortak yardimcilari: anahtar cozme, hata siniflandirma, Retry-After.</summary>
internal static class NutritionAiHttp
{
    internal static readonly JsonSerializerOptions Json = new()
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>Anahtar once yapilandirmadan (NutritionAssistant__Groq__ApiKey), yoksa ortam degiskeninden okunur.</summary>
    public static string? ResolveApiKey(NutritionAiProviderSettings settings, string environmentVariable)
    {
        var key = string.IsNullOrWhiteSpace(settings.ApiKey) ? Environment.GetEnvironmentVariable(environmentVariable) : settings.ApiKey;
        return string.IsNullOrWhiteSpace(key) ? null : key.Trim();
    }

    /// <summary>
    /// Basarisiz yaniti siniflandirir. Yanit govdesinden yalnizca hata kodu/tipi okunur; mesaj metni
    /// (istek icerigini yansitabilir) loglanmaz ve istisnaya konmaz.
    /// </summary>
    public static async Task<NutritionAiProviderException> ToExceptionAsync(string provider, HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var status = (int)response.StatusCode;
        string? errorCode = null;
        TimeSpan? bodyRetryDelay = null;
        try
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (body.Length is > 0 and < 64 * 1024)
            {
                using var document = JsonDocument.Parse(body);
                if (document.RootElement.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.Object)
                {
                    errorCode = ReadString(error, "code") ?? ReadString(error, "status") ?? ReadString(error, "type");
                    bodyRetryDelay = ReadGoogleRetryDelay(error);
                }
            }
        }
        catch (Exception ex) when (ex is JsonException or HttpRequestException or InvalidOperationException)
        {
            // Govde okunamazsa yalnizca durum kodu kullanilir.
        }

        var kind = response.StatusCode switch
        {
            HttpStatusCode.TooManyRequests => NutritionAiFailureKind.RateLimited,
            HttpStatusCode.RequestTimeout => NutritionAiFailureKind.Transient,
            _ when status >= 500 => NutritionAiFailureKind.Transient,
            _ => NutritionAiFailureKind.Rejected,
        };
        var retryAfter = kind == NutritionAiFailureKind.RateLimited || status == 503
            ? ParseRetryAfter(response) ?? bodyRetryDelay
            : null;
        return new NutritionAiProviderException(provider, kind, status, retryAfter, errorCode);
    }

    /// <summary>Retry-After: saniye ("2", "2.5") ya da HTTP tarihi.</summary>
    public static TimeSpan? ParseRetryAfter(HttpResponseMessage response)
    {
        if (response.Headers.RetryAfter is { } header)
        {
            if (header.Delta is { } delta) return delta;
            if (header.Date is { } date)
            {
                var wait = date - DateTimeOffset.UtcNow;
                return wait > TimeSpan.Zero ? wait : TimeSpan.Zero;
            }
        }
        if (response.Headers.TryGetValues("retry-after", out var values)
            && double.TryParse(values.FirstOrDefault(), NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds)
            && seconds >= 0)
        {
            return TimeSpan.FromSeconds(seconds);
        }
        return null;
    }

    /// <summary>Gemini 429 govdesindeki google.rpc.RetryInfo ("retryDelay": "13s").</summary>
    private static TimeSpan? ReadGoogleRetryDelay(JsonElement error)
    {
        if (!error.TryGetProperty("details", out var details) || details.ValueKind != JsonValueKind.Array) return null;
        foreach (var detail in details.EnumerateArray())
        {
            if (ReadString(detail, "retryDelay") is { } delay
                && delay.EndsWith('s')
                && double.TryParse(delay[..^1], NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds))
            {
                return TimeSpan.FromSeconds(seconds);
            }
        }
        return null;
    }

    private static string? ReadString(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value)
            ? value.ValueKind switch
            {
                JsonValueKind.String => value.GetString(),
                JsonValueKind.Number => value.GetRawText(),
                _ => null,
            }
            : null;
}
