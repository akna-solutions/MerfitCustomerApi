using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using MerfitCustomerApi.Business.Common;
using MerfitCustomerApi.Business.Interfaces.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MerfitCustomerApi.Business.Services.Notifications.Engine;

/// <summary>
/// Expo Push Service (https://docs.expo.dev/push-notifications/sending-notifications/) uygulamasi.
/// Mesajlar 100'luk parcalar halinde gonderilir; her mesaj icin Expo "ticket" sonucu ayni sirayla
/// doner. Ticket "ok" yalnizca Expo'nun mesaji kabul ettigini gosterir; APNs/FCM sonucu daha sonra
/// makbuz (receipt) ile ogrenilir.
/// </summary>
public partial class ExpoPushNotificationService : IPushNotificationService
{
    private const int SendChunkSize = 100;
    private const int ReceiptChunkSize = 1000;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly HttpClient _httpClient;
    private readonly ExpoPushOptions _options;
    private readonly ILogger<ExpoPushNotificationService> _logger;

    public ExpoPushNotificationService(HttpClient httpClient, IOptions<NotificationOptions> options, ILogger<ExpoPushNotificationService> logger)
    {
        _httpClient = httpClient;
        _options = options.Value.Expo;
        _logger = logger;
    }

    [GeneratedRegex(@"^Expo(nent)?PushToken\[[A-Za-z0-9_\-]{10,200}\]$")]
    private static partial Regex TokenRegex();

    public bool IsValidToken(string token) => !string.IsNullOrWhiteSpace(token) && TokenRegex().IsMatch(token.Trim());

    public async Task<IReadOnlyList<PushSendResult>> SendAsync(IReadOnlyList<PushMessage> messages, CancellationToken cancellationToken = default)
    {
        var results = new List<PushSendResult>(messages.Count);
        foreach (var chunk in messages.Chunk(SendChunkSize))
        {
            results.AddRange(await SendChunkAsync(chunk, cancellationToken));
        }
        return results;
    }

    private async Task<IReadOnlyList<PushSendResult>> SendChunkAsync(PushMessage[] chunk, CancellationToken cancellationToken)
    {
        var payload = chunk.Select(m => new ExpoMessage
        {
            To = m.To,
            Title = m.Title,
            Body = m.Body,
            Data = m.Data,
            Sound = "default",
            ChannelId = m.ChannelId ?? _options.AndroidChannelId,
        }).ToList();

        HttpResponseMessage response;
        try
        {
            using var request = CreateRequest("/--/api/v2/push/send", payload);
            response = await _httpClient.SendAsync(request, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("Expo push request failed: {Message}", ex.Message);
            return Repeat(chunk.Length, new PushSendResult(PushSendOutcome.Transient, ErrorCode: PushErrorCodes.Network, ErrorMessage: Truncate(ex.Message)));
        }

        using (response)
        {
            var status = (int)response.StatusCode;
            if (response.StatusCode == HttpStatusCode.TooManyRequests || status >= 500)
            {
                return Repeat(chunk.Length, new PushSendResult(PushSendOutcome.Transient, ErrorCode: $"HTTP{status}", ErrorMessage: "Expo geçici olarak isteği kabul etmedi."));
            }

            ExpoSendResponse? body = null;
            try
            {
                body = await response.Content.ReadFromJsonAsync<ExpoSendResponse>(JsonOptions, cancellationToken);
            }
            catch (JsonException)
            {
                // Asagida isleniyor.
            }

            if (!response.IsSuccessStatusCode || body?.Data is null || body.Data.Count != chunk.Length)
            {
                var code = body?.Errors?.FirstOrDefault()?.Code ?? $"HTTP{status}";
                var message = body?.Errors?.FirstOrDefault()?.Message ?? "Expo beklenmeyen bir yanıt döndürdü.";
                _logger.LogWarning("Expo push request rejected: {Status} {Code}", status, code);
                return Repeat(chunk.Length, new PushSendResult(PushSendOutcome.Rejected, ErrorCode: Truncate(code, 100), ErrorMessage: Truncate(message)));
            }

            return body.Data.Select(ticket => ticket.Status == "ok"
                    ? new PushSendResult(PushSendOutcome.Accepted, ProviderMessageId: ticket.Id)
                    : new PushSendResult(
                        IsTransientTicketError(ticket.Details?.Error) ? PushSendOutcome.Transient : PushSendOutcome.Rejected,
                        ErrorCode: Truncate(ticket.Details?.Error ?? "Error", 100),
                        ErrorMessage: Truncate(ticket.Message)))
                .ToList();
        }
    }

    public async Task<IReadOnlyDictionary<string, PushReceiptResult>> GetReceiptsAsync(IReadOnlyCollection<string> providerMessageIds, CancellationToken cancellationToken = default)
    {
        var results = new Dictionary<string, PushReceiptResult>();
        foreach (var chunk in providerMessageIds.Chunk(ReceiptChunkSize))
        {
            try
            {
                using var request = CreateRequest("/--/api/v2/push/getReceipts", new { ids = chunk });
                using var response = await _httpClient.SendAsync(request, cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("Expo receipt request failed: {Status}", (int)response.StatusCode);
                    continue;
                }

                var body = await response.Content.ReadFromJsonAsync<ExpoReceiptResponse>(JsonOptions, cancellationToken);
                foreach (var (id, receipt) in body?.Data ?? new Dictionary<string, ExpoTicket>())
                {
                    results[id] = receipt.Status == "ok"
                        ? new PushReceiptResult(true)
                        : new PushReceiptResult(false, Truncate(receipt.Details?.Error ?? "Error", 100), Truncate(receipt.Message));
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning("Expo receipt request failed: {Message}", ex.Message);
            }
        }
        return results;
    }

    private HttpRequestMessage CreateRequest(string path, object payload)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = JsonContent.Create(payload, options: JsonOptions),
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (!string.IsNullOrWhiteSpace(_options.AccessToken))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.AccessToken);
        }
        return request;
    }

    /// <summary>Expo'nun yeniden denenebilir olarak belgeledigi ticket hatalari.</summary>
    private static bool IsTransientTicketError(string? error) => error is "MessageRateExceeded";

    private static IReadOnlyList<PushSendResult> Repeat(int count, PushSendResult result) => Enumerable.Repeat(result, count).ToList();

    private static string? Truncate(string? value, int max = 500) => value is null || value.Length <= max ? value : value[..max];

    private sealed class ExpoMessage
    {
        public string To { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Body { get; set; } = string.Empty;
        public IReadOnlyDictionary<string, string>? Data { get; set; }
        public string? Sound { get; set; }
        public string? ChannelId { get; set; }
    }

    private sealed class ExpoSendResponse
    {
        public List<ExpoTicket>? Data { get; set; }
        public List<ExpoError>? Errors { get; set; }
    }

    private sealed class ExpoReceiptResponse
    {
        public Dictionary<string, ExpoTicket>? Data { get; set; }
    }

    private sealed class ExpoTicket
    {
        public string? Status { get; set; }
        public string? Id { get; set; }
        public string? Message { get; set; }
        public ExpoDetails? Details { get; set; }
    }

    private sealed class ExpoDetails
    {
        public string? Error { get; set; }
    }

    private sealed class ExpoError
    {
        public string? Code { get; set; }
        public string? Message { get; set; }
    }
}
