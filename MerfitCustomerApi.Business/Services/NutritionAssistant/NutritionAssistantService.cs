using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using MerfitCustomerApi.Business.Common;
using MerfitCustomerApi.Business.Dtos.Customer.NutritionAssistant;
using MerfitCustomerApi.Business.Interfaces.Services;
using MerfitCustomerApi.Domain.Entities;
using MerfitCustomerApi.Domain.Entities.Enums;
using MerfitCustomerApi.Domain.Exceptions;
using MerfitCustomerApi.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MerfitCustomerApi.Business.Services.NutritionAssistant;

/// <summary>
/// INutritionAssistantService'in varsayilan implementasyonu.
///
/// Akis: gun cozumlenir (kullanicinin saat dilimine gore, gecerli aralik kontrol edilir) -> kullanim
/// sinirlari kontrol edilir -> sohbet kullanicinin kendi kayitlarindan bulunur (baskasinin sohbet kimligi
/// 404 doner) -> o gunun guncel beslenme baglami olusturulur -> son N mesajla birlikte modele gonderilir ->
/// basarili yanitta kullanici ve asistan mesajlari tek SaveChanges ile kaydedilir. Mesaj icerikleri
/// loglanmaz; kullanim (token, sure) AiGenerationRequest/Result tablolarina icerik olmadan yazilir.
/// </summary>
public class NutritionAssistantService : INutritionAssistantService
{
    private const string UnavailableMessage = "Beslenme asistanı şu an kullanılamıyor. Lütfen daha sonra tekrar deneyin.";
    private const int MaxStoredContentLength = 8000;

    private static readonly Regex MarkdownEmphasis = new(@"(\*\*|__)", RegexOptions.Compiled);
    private static readonly Regex MarkdownHeading = new(@"^\s{0,3}#{1,6}\s+", RegexOptions.Compiled | RegexOptions.Multiline);

    private readonly IUnitOfWork _unitOfWork;
    private readonly NutritionAssistantContextBuilder _contextBuilder;
    private readonly INutritionAssistantModelClient _modelClient;
    private readonly NutritionAssistantOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<NutritionAssistantService> _logger;

    public NutritionAssistantService(
        IUnitOfWork unitOfWork,
        NutritionAssistantContextBuilder contextBuilder,
        INutritionAssistantModelClient modelClient,
        IOptions<NutritionAssistantOptions> options,
        TimeProvider timeProvider,
        ILogger<NutritionAssistantService> logger)
    {
        _unitOfWork = unitOfWork;
        _contextBuilder = contextBuilder;
        _modelClient = modelClient;
        _options = options.Value;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    private bool AssistantAvailable => _options.Enabled && _modelClient.IsConfigured;

    public async Task<NutritionAssistantConversationDto> GetConversationAsync(long userId, DateOnly? date, CancellationToken cancellationToken = default)
    {
        var (targetDate, localTime) = ResolveDate(date);
        var context = await _contextBuilder.BuildAsync(userId, targetDate, localTime, cancellationToken);

        var conversation = await _unitOfWork.Repository<NutritionAssistantConversation>()
            .FirstOrDefaultAsync(c => c.UserId == userId && c.Date == targetDate, cancellationToken);

        var messages = conversation is null
            ? new List<NutritionAssistantMessage>()
            : (await _unitOfWork.Repository<NutritionAssistantMessage>().GetQueryable()
                .Where(m => m.ConversationId == conversation.Id)
                .OrderByDescending(m => m.Id)
                .Take(Math.Max(1, _options.MaxReturnedMessages))
                .ToListAsync(cancellationToken))
                .AsEnumerable().Reverse().ToList();

        var askedQuestions = messages.Where(m => m.Role == NutritionAssistantMessageRole.User).Select(m => m.Content);

        return new NutritionAssistantConversationDto
        {
            ConversationId = conversation?.Id,
            Date = targetDate,
            AssistantAvailable = AssistantAvailable,
            Greeting = new NutritionAssistantMessageDto
            {
                Role = "assistant",
                Content = BuildGreeting(context),
                CreatedAt = conversation?.CreatedAt ?? _timeProvider.GetUtcNow().UtcDateTime,
            },
            Messages = messages.Select(ToDto).ToList(),
            Insight = context.Insight,
            SuggestedQuestions = NutritionInsightEvaluator.SuggestQuestions(context.Day, context.Insight, localTime?.Hour, context.WorkedOut, askedQuestions),
            RemainingMessages = await RemainingMessagesAsync(userId, cancellationToken),
        };
    }

    public async Task<NutritionAssistantChatResponse> SendMessageAsync(long userId, NutritionAssistantChatRequest request, CancellationToken cancellationToken = default)
    {
        if (!AssistantAvailable)
        {
            throw new ExternalServiceUnavailableException(UnavailableMessage);
        }

        var text = (request.Message ?? string.Empty).Trim();
        if (text.Length == 0)
        {
            throw new AppValidationException(nameof(request.Message), "Mesaj boş olamaz.");
        }
        if (text.Length > _options.MaxUserMessageLength)
        {
            throw new AppValidationException(nameof(request.Message), $"Mesaj en fazla {_options.MaxUserMessageLength} karakter olabilir.");
        }

        var (targetDate, localTime) = ResolveDate(request.Date);
        var now = _timeProvider.GetUtcNow().UtcDateTime;

        var remaining = await RemainingMessagesAsync(userId, cancellationToken);
        if (remaining <= 0)
        {
            throw new TooManyRequestsException("Bugünlük mesaj sınırına ulaştın. Daha sonra tekrar deneyebilirsin.");
        }

        var conversation = await ResolveConversationAsync(userId, request.ConversationId, targetDate, cancellationToken);

        var history = new List<NutritionAssistantMessage>();
        if (conversation is not null)
        {
            var userMessageCount = await _unitOfWork.Repository<NutritionAssistantMessage>().GetQueryable()
                .CountAsync(m => m.ConversationId == conversation.Id && m.Role == NutritionAssistantMessageRole.User, cancellationToken);
            if (userMessageCount >= _options.MaxUserMessagesPerConversation)
            {
                throw new TooManyRequestsException("Bu sohbet için mesaj sınırına ulaştın. Yarın yeni bir sohbet başlatabilirsin.");
            }

            history = (await _unitOfWork.Repository<NutritionAssistantMessage>().GetQueryable()
                .Where(m => m.ConversationId == conversation.Id)
                .OrderByDescending(m => m.Id)
                .Take(Math.Max(0, _options.HistoryMessageLimit))
                .ToListAsync(cancellationToken))
                .AsEnumerable().Reverse()
                // Pencere bir asistan mesajiyla baslamasin.
                .SkipWhile(m => m.Role != NutritionAssistantMessageRole.User)
                .ToList();
        }

        var context = await _contextBuilder.BuildAsync(userId, targetDate, localTime, cancellationToken);

        var modelMessages = history
            .Select(m => new NutritionAssistantModelMessage(m.Role, m.Content))
            .Append(new NutritionAssistantModelMessage(NutritionAssistantMessageRole.User, text))
            .ToList();

        var stopwatch = Stopwatch.StartNew();
        NutritionAssistantModelResult result;
        try
        {
            result = await _modelClient.CompleteAsync(
                new NutritionAssistantModelRequest(NutritionAssistantPrompt.Instructions, NutritionAssistantPrompt.ContextBlock(context), modelMessages),
                cancellationToken);
        }
        catch (ExternalServiceUnavailableException)
        {
            await TryRecordFailureAsync(userId, conversation?.Id, targetDate, stopwatch.ElapsedMilliseconds, now);
            throw;
        }

        var reply = result.Refused ? NutritionAssistantPrompt.RefusalMessage : ToPlainText(result.Text);
        if (reply.Length == 0)
        {
            await TryRecordFailureAsync(userId, conversation?.Id, targetDate, stopwatch.ElapsedMilliseconds, now);
            throw new ExternalServiceUnavailableException(UnavailableMessage);
        }

        conversation ??= await CreateConversationAsync(userId, targetDate, now, cancellationToken);

        var userMessage = new NutritionAssistantMessage
        {
            ConversationId = conversation.Id,
            Role = NutritionAssistantMessageRole.User,
            Content = text,
            CreatedAt = now,
        };
        var assistantMessage = new NutritionAssistantMessage
        {
            ConversationId = conversation.Id,
            Role = NutritionAssistantMessageRole.Assistant,
            Content = reply,
            InputTokens = result.InputTokens,
            OutputTokens = result.OutputTokens,
            // Sira Id ile belirlenir; CreatedAt da kullanici mesajindan sonra kalir.
            CreatedAt = now.AddTicks(1),
        };
        await _unitOfWork.Repository<NutritionAssistantMessage>().AddAsync(userMessage, cancellationToken);
        await _unitOfWork.Repository<NutritionAssistantMessage>().AddAsync(assistantMessage, cancellationToken);

        var tracked = await _unitOfWork.Repository<NutritionAssistantConversation>().GetQueryable(asNoTracking: false)
            .FirstAsync(c => c.Id == conversation.Id, cancellationToken);
        tracked.LastMessageAt = now;
        tracked.UpdatedAt = now;

        var usage = await AddUsageRecordAsync(userId, conversation.Id, targetDate, AiGenerationStatus.Completed, now, stopwatch.ElapsedMilliseconds, result, cancellationToken);

        // Model yaniti alindiysa istemci baglantisi kopsa bile kayit tamamlanir; boylece yeniden acilista gorunur.
        await _unitOfWork.SaveChangesAsync(CancellationToken.None);
        await AddUsageResultAsync(usage, stopwatch.ElapsedMilliseconds, result);

        _logger.LogInformation(
            "Beslenme asistani yaniti. UserId: {UserId}, ConversationId: {ConversationId}, Model: {Model}, InputTokens: {InputTokens}, OutputTokens: {OutputTokens}, StopReason: {StopReason}, DurationMs: {DurationMs}",
            userId, conversation.Id, result.Model, result.InputTokens, result.OutputTokens, result.StopReason, stopwatch.ElapsedMilliseconds);

        return new NutritionAssistantChatResponse
        {
            ConversationId = conversation.Id,
            Date = targetDate,
            UserMessage = ToDto(userMessage),
            Message = ToDto(assistantMessage),
            SuggestedQuestions = NutritionInsightEvaluator.SuggestQuestions(
                context.Day, context.Insight, localTime?.Hour, context.WorkedOut,
                history.Where(m => m.Role == NutritionAssistantMessageRole.User).Select(m => m.Content).Append(text)),
            RemainingMessages = Math.Max(0, remaining - 1),
        };
    }

    /// <summary>
    /// Istemcinin gonderdigi sohbet kimligini dogrular: yalnizca kullanicinin kendi sohbeti kabul edilir,
    /// aksi 404'tur (baskasinin sohbetinin varligi da sizdirilmaz). Sohbet baska bir gune aitse (gun
    /// degisti) o gunun sohbeti kullanilir.
    /// </summary>
    private async Task<NutritionAssistantConversation?> ResolveConversationAsync(long userId, long? conversationId, DateOnly date, CancellationToken cancellationToken)
    {
        var conversations = _unitOfWork.Repository<NutritionAssistantConversation>();
        if (conversationId is { } id)
        {
            var owned = await conversations.FirstOrDefaultAsync(c => c.Id == id && c.UserId == userId, cancellationToken)
                ?? throw new NotFoundException("Sohbet bulunamadı.");
            if (owned.Date == date)
            {
                return owned;
            }
        }

        return await conversations.FirstOrDefaultAsync(c => c.UserId == userId && c.Date == date, cancellationToken);
    }

    /// <summary>
    /// Gunun sohbetini olusturur. Ayni anda gelen iki ilk mesajda biri benzersiz index ihlali alir ve
    /// mevcut sohbeti kullanir. Yeni sohbet olusurken kullanicinin saklama suresi dolan eski sohbetleri silinir.
    /// </summary>
    private async Task<NutritionAssistantConversation> CreateConversationAsync(long userId, DateOnly date, DateTime now, CancellationToken cancellationToken)
    {
        var repository = _unitOfWork.Repository<NutritionAssistantConversation>();
        var expired = await repository.GetQueryable(asNoTracking: false)
            .Where(c => c.UserId == userId && c.LastMessageAt < now.AddDays(-Math.Max(1, _options.RetentionDays)))
            .ToListAsync(cancellationToken);
        if (expired.Count > 0)
        {
            repository.RemoveRange(expired);
        }

        var conversation = new NutritionAssistantConversation
        {
            UserId = userId,
            Date = date,
            LastMessageAt = now,
            CreatedAt = now,
        };
        await repository.AddAsync(conversation, cancellationToken);
        try
        {
            await _unitOfWork.SaveChangesAsync(CancellationToken.None);
            return conversation;
        }
        catch (DbUpdateException ex) when (DbExceptionClassifier.IsUniqueViolation(ex, out _))
        {
            _unitOfWork.ClearTracking();
            var existing = await repository.FirstOrDefaultAsync(c => c.UserId == userId && c.Date == date, cancellationToken);
            if (existing is null) throw;
            return existing;
        }
    }

    private async Task<int> RemainingMessagesAsync(long userId, CancellationToken cancellationToken)
    {
        var since = _timeProvider.GetUtcNow().UtcDateTime.AddHours(-24);
        var conversationIds = _unitOfWork.Repository<NutritionAssistantConversation>().GetQueryable()
            .Where(c => c.UserId == userId)
            .Select(c => c.Id);
        var sent = await _unitOfWork.Repository<NutritionAssistantMessage>().GetQueryable()
            .CountAsync(m => conversationIds.Contains(m.ConversationId)
                && m.Role == NutritionAssistantMessageRole.User
                && m.CreatedAt >= since, cancellationToken);
        return Math.Max(0, _options.DailyMessageLimitPerUser - sent);
    }

    /// <summary>
    /// Istenen gunu kullanicinin saat dilimine gore dogrular. Gun bugunse yerel saati de dondurur
    /// (hizli sorular ve baglam icin). Gecerli aralik: bugunden <see cref="NutritionAssistantOptions.MaxPastDays"/>
    /// gun once ile yarin arasi (saat dilimi farklari icin).
    /// </summary>
    private (DateOnly Date, TimeOnly? LocalTime) ResolveDate(DateOnly? requested)
    {
        var local = TimeZoneInfo.ConvertTime(_timeProvider.GetUtcNow(), ResolveTimeZone());
        var today = DateOnly.FromDateTime(local.DateTime);
        var date = requested ?? today;

        if (date > today.AddDays(1) || date < today.AddDays(-Math.Max(0, _options.MaxPastDays)))
        {
            throw new AppValidationException("Date", "Bu tarih için beslenme asistanı kullanılamaz.");
        }

        return (date, date == today ? TimeOnly.FromDateTime(local.DateTime) : null);
    }

    private TimeZoneInfo ResolveTimeZone()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(_options.TimeZone);
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return TimeZoneInfo.Utc;
        }
    }

    private static string BuildGreeting(NutritionAssistantContext context)
    {
        var hello = context.FirstName is null ? "Merhaba!" : $"Merhaba {context.FirstName}!";
        var follow = context.Day.Meals.Count == 0
            ? "İstersen hedefine uygun öğün önerileriyle başlayalım."
            : "Hedeflerin ve bugünkü tüketimin hakkında soru sorabilir ya da öğün önerisi isteyebilirsin.";
        return $"{hello} {context.Insight.Message} {follow}";
    }

    /// <summary>Model yanitini duz metne indirger (istemci HTML/Markdown yorumlamaz) ve boyutunu sinirlar.</summary>
    internal static string ToPlainText(string text)
    {
        var plain = MarkdownHeading.Replace(MarkdownEmphasis.Replace(text ?? string.Empty, string.Empty), string.Empty).Trim();
        return plain.Length > MaxStoredContentLength ? plain[..MaxStoredContentLength].TrimEnd() + "…" : plain;
    }

    private async Task<AiGenerationRequest> AddUsageRecordAsync(long userId, long? conversationId, DateOnly date, AiGenerationStatus status, DateTime startedAt, long durationMs, NutritionAssistantModelResult? result, CancellationToken cancellationToken)
    {
        var request = new AiGenerationRequest
        {
            UserId = userId,
            Type = AiRequestType.NutritionAssistant,
            // Kullanici mesaji burada saklanmaz; yalnizca hangi sohbete ait oldugu.
            Prompt = $"Beslenme asistanı sohbeti (sohbet #{conversationId?.ToString() ?? "yeni"}, gün {date:yyyy-MM-dd})",
            Status = status,
            Model = result?.Model ?? _modelClient.Model,
            StartedAt = startedAt,
            CompletedAt = startedAt.AddMilliseconds(durationMs),
            CreatedAt = startedAt,
        };
        await _unitOfWork.Repository<AiGenerationRequest>().AddAsync(request, cancellationToken);
        return request;
    }

    private async Task AddUsageResultAsync(AiGenerationRequest request, long durationMs, NutritionAssistantModelResult result)
    {
        try
        {
            await _unitOfWork.Repository<AiGenerationResult>().AddAsync(new AiGenerationResult
            {
                RequestId = request.Id,
                ResultJson = JsonSerializer.Serialize(new
                {
                    inputTokens = result.InputTokens,
                    outputTokens = result.OutputTokens,
                    stopReason = result.StopReason,
                    refused = result.Refused,
                    durationMs,
                }),
                CreatedAt = request.CreatedAt,
            });
            await _unitOfWork.SaveChangesAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            // Kullanim ozeti yazilamazsa sohbet bozulmaz.
            _logger.LogWarning(ex, "Beslenme asistani kullanim ozeti kaydedilemedi. RequestId: {RequestId}", request.Id);
        }
    }

    private async Task TryRecordFailureAsync(long userId, long? conversationId, DateOnly date, long durationMs, DateTime startedAt)
    {
        try
        {
            _unitOfWork.ClearTracking();
            await AddUsageRecordAsync(userId, conversationId, date, AiGenerationStatus.Failed, startedAt, durationMs, null, CancellationToken.None);
            await _unitOfWork.SaveChangesAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Beslenme asistani hata kaydi yazilamadi. UserId: {UserId}", userId);
        }
    }

    private static NutritionAssistantMessageDto ToDto(NutritionAssistantMessage message) => new()
    {
        Id = message.Id,
        Role = message.Role == NutritionAssistantMessageRole.User ? "user" : "assistant",
        Content = message.Content,
        CreatedAt = message.CreatedAt,
    };
}
