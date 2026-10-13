using System.Text.RegularExpressions;
using MerfitCustomerApi.Business.Interfaces.Services;
using MerfitCustomerApi.Business.Services.Email;
using MerfitCustomerApi.Domain.Exceptions;

namespace MerfitCustomerApi.Business.Tests.AccountEmail;

/// <summary>Gonderilen e-postalari bellekte tutan sahte saglayici.</summary>
internal sealed class FakeEmailSender : IEmailSender
{
    public bool IsConfigured { get; set; } = true;

    /// <summary>Doluysa bir sonraki gonderimler bu hatayi firlatir.</summary>
    public Exception? FailWith { get; set; }

    public List<(OutgoingEmail Email, string IdempotencyKey)> Sent { get; } = new();

    public Task SendAsync(OutgoingEmail email, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        if (!IsConfigured)
        {
            throw new EmailDeliveryException(ResendEmailSender.NotConfiguredMessage, isConfigurationError: true);
        }

        if (FailWith is not null)
        {
            throw FailWith;
        }

        Sent.Add((email, idempotencyKey));
        return Task.CompletedTask;
    }

    /// <summary>E-postadaki 6 haneli kodu cikarir (kullanicinin e-postayi okumasi).</summary>
    public static string ExtractCode(OutgoingEmail email)
    {
        var match = Regex.Match(email.TextBody, @"^\s*(\d{6})\s*$", RegexOptions.Multiline);
        if (!match.Success)
        {
            throw new InvalidOperationException("E-postada kod bulunamadi.");
        }

        return match.Groups[1].Value;
    }
}

internal sealed class MutableTimeProvider : TimeProvider
{
    public DateTimeOffset Now { get; set; } = new(2026, 10, 13, 12, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => Now;

    public void Advance(TimeSpan by) => Now += by;
}
