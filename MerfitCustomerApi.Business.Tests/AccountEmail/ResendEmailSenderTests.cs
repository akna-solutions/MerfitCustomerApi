using System.Net;
using MerfitCustomerApi.Business.Common;
using MerfitCustomerApi.Business.Interfaces.Services;
using MerfitCustomerApi.Business.Services.Email;
using MerfitCustomerApi.Domain.Exceptions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Resend;
using Xunit;

namespace MerfitCustomerApi.Business.Tests.AccountEmail;

/// <summary>
/// Resend saglayicisinin hata yonetimi: eksik anahtar, gecici hatada ayni idempotency anahtariyla yeniden
/// deneme, kalici/yapilandirma hatasinda yeniden denememe ve hicbir durumda basari gibi gorunmeme.
/// </summary>
public class ResendEmailSenderTests
{
    private static readonly OutgoingEmail Email = new("user@example.com", "Konu", "<p>html</p>", "metin");

    private static (ResendEmailSender Sender, Mock<IResend> Resend) Create(string? apiKey = "re_test_key", int maxAttempts = 2)
    {
        var resend = new Mock<IResend>();
        var services = new ServiceCollection().AddSingleton(resend.Object).BuildServiceProvider();
        var options = Options.Create(new ResendEmailOptions { ApiKey = apiKey, MaxSendAttempts = maxAttempts, RetryDelay = TimeSpan.Zero });
        return (new ResendEmailSender(services, options, NullLogger<ResendEmailSender>.Instance), resend);
    }

    private static ResendException Error(HttpStatusCode? status, ErrorType type) => new(status, type, "hata", null);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task MissingApiKey_ThrowsConfigurationError_WithoutCallingResend(string? apiKey)
    {
        var (sender, resend) = Create(apiKey);

        Assert.False(sender.IsConfigured);
        var ex = await Assert.ThrowsAsync<EmailDeliveryException>(() => sender.SendAsync(Email, "key-1"));

        Assert.True(ex.IsConfigurationError);
        Assert.Equal(ResendEmailSender.NotConfiguredMessage, ex.Message);
        resend.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Success_SendsWithConfiguredSenderAndIdempotencyKey()
    {
        var (sender, resend) = Create();
        EmailMessage? captured = null;
        resend.Setup(r => r.EmailSendAsync("key-1", It.IsAny<EmailMessage>(), It.IsAny<CancellationToken>()))
            .Callback<string, EmailMessage, CancellationToken>((_, m, _) => captured = m)
            .ReturnsAsync(new ResendResponse<Guid>(Guid.NewGuid(), null));

        await sender.SendAsync(Email, "key-1");

        Assert.NotNull(captured);
        Assert.Equal(ResendEmailOptions.DefaultFrom, captured!.From);
        Assert.Equal("Konu", captured.Subject);
        Assert.Equal("<p>html</p>", captured.HtmlBody);
        Assert.Equal("metin", captured.TextBody);
    }

    [Fact]
    public async Task TransientError_IsRetriedWithSameIdempotencyKey()
    {
        var (sender, resend) = Create();
        resend.SetupSequence(r => r.EmailSendAsync(It.IsAny<string>(), It.IsAny<EmailMessage>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(Error(HttpStatusCode.InternalServerError, ErrorType.InternalServerError))
            .ReturnsAsync(new ResendResponse<Guid>(Guid.NewGuid(), null));

        await sender.SendAsync(Email, "key-1");

        resend.Verify(r => r.EmailSendAsync("key-1", It.IsAny<EmailMessage>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task TransientError_ThatPersists_FailsAsUnavailable()
    {
        var (sender, resend) = Create(maxAttempts: 3);
        resend.Setup(r => r.EmailSendAsync(It.IsAny<string>(), It.IsAny<EmailMessage>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(Error((HttpStatusCode)429, ErrorType.RateLimitExceeded));

        var ex = await Assert.ThrowsAsync<EmailDeliveryException>(() => sender.SendAsync(Email, "key-1"));

        Assert.False(ex.IsConfigurationError);
        Assert.Equal(ResendEmailSender.UnavailableMessage, ex.Message);
        resend.Verify(r => r.EmailSendAsync(It.IsAny<string>(), It.IsAny<EmailMessage>(), It.IsAny<CancellationToken>()), Times.Exactly(3));
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, ErrorType.InvalidApiKey)]
    [InlineData(HttpStatusCode.Unauthorized, ErrorType.RestrictedApiKey)]
    [InlineData(HttpStatusCode.UnprocessableEntity, ErrorType.InvalidFromAddress)]
    [InlineData(HttpStatusCode.Forbidden, ErrorType.ValidationError)] // gonderici domain'i dogrulanmamis
    public async Task ConfigurationErrors_AreNotRetried(HttpStatusCode status, ErrorType type)
    {
        var (sender, resend) = Create(maxAttempts: 3);
        resend.Setup(r => r.EmailSendAsync(It.IsAny<string>(), It.IsAny<EmailMessage>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(Error(status, type));

        var ex = await Assert.ThrowsAsync<EmailDeliveryException>(() => sender.SendAsync(Email, "key-1"));

        Assert.True(ex.IsConfigurationError);
        resend.Verify(r => r.EmailSendAsync(It.IsAny<string>(), It.IsAny<EmailMessage>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task QuotaExceeded_FailsWithoutRetry()
    {
        var (sender, resend) = Create(maxAttempts: 3);
        resend.Setup(r => r.EmailSendAsync(It.IsAny<string>(), It.IsAny<EmailMessage>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(Error((HttpStatusCode)429, ErrorType.DailyQuotaExceeded));

        var ex = await Assert.ThrowsAsync<EmailDeliveryException>(() => sender.SendAsync(Email, "key-1"));

        Assert.False(ex.IsConfigurationError);
        resend.Verify(r => r.EmailSendAsync(It.IsAny<string>(), It.IsAny<EmailMessage>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task NetworkFailure_IsRetried_ThenReportedAsUnavailable()
    {
        var (sender, resend) = Create();
        resend.Setup(r => r.EmailSendAsync(It.IsAny<string>(), It.IsAny<EmailMessage>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("baglanti yok"));

        var ex = await Assert.ThrowsAsync<EmailDeliveryException>(() => sender.SendAsync(Email, "key-1"));

        Assert.False(ex.IsConfigurationError);
        resend.Verify(r => r.EmailSendAsync(It.IsAny<string>(), It.IsAny<EmailMessage>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }
}
