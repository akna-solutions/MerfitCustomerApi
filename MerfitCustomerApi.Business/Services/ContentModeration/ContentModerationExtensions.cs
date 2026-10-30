using MerfitCustomerApi.Business.Interfaces.Services;
using MerfitCustomerApi.Domain.Exceptions;

namespace MerfitCustomerApi.Business.Services.ContentModeration;

/// <summary>Servis katmaninin veritabanina yazmadan once cagirdigi moderasyon kontrolu.</summary>
public static class ContentModerationExtensions
{
    /// <summary>
    /// Verilen alanlari kontrol eder; uygunsuz olan varsa hepsini alan adiyla birlikte tek bir
    /// <see cref="AppValidationException"/> (400, standart ApiResponse) olarak firlatir. Null alanlar atlanir.
    /// </summary>
    public static void EnsureAllowed(
        this IContentModerationService moderation,
        params (string Field, string? Value, ContentModerationType Type)[] fields)
    {
        Dictionary<string, string[]>? errors = null;
        foreach (var (field, value, type) in fields)
        {
            var result = moderation.Check(value, type);
            if (!result.IsAllowed)
            {
                errors ??= new Dictionary<string, string[]>();
                errors[field] = [result.RejectionReason!];
            }
        }

        if (errors is not null)
        {
            throw new AppValidationException(errors);
        }
    }
}
