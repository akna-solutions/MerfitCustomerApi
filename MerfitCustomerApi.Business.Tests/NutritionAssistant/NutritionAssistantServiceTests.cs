using MerfitCustomerApi.Business.Common;
using MerfitCustomerApi.Business.Dtos.Customer.Nutrition;
using MerfitCustomerApi.Business.Dtos.Customer.NutritionAssistant;
using MerfitCustomerApi.Business.Interfaces.Services;
using MerfitCustomerApi.Business.Services.NutritionAssistant;
using MerfitCustomerApi.Business.Tests.TestHelpers;
using MerfitCustomerApi.Domain.Entities;
using MerfitCustomerApi.Domain.Entities.Enums;
using MerfitCustomerApi.Domain.Exceptions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;
using static MerfitCustomerApi.Business.Tests.NutritionAssistant.NutritionAssistantTestData;

namespace MerfitCustomerApi.Business.Tests.NutritionAssistant;

/// <summary>
/// Sohbet akisi: gercek veriden baglam, onceki mesajlarin baglam olarak kullanilmasi, gun degisimi,
/// sahiplik kontrolu, kullanim sinirlari, saglayici hatalari ve kalicilik.
/// </summary>
public class NutritionAssistantServiceTests
{
    private const long UserId = 42;
    private const long OtherUserId = 7;

    // 2026-10-13 16:30 UTC = 19:30 Istanbul.
    private readonly FakeClock _clock = new(new DateTimeOffset(2026, 10, 13, 16, 30, 0, TimeSpan.Zero));
    private readonly InMemoryUnitOfWork _uow = new();
    private readonly Mock<INutritionService> _nutrition = new();
    private readonly FakeModelClient _model = new();
    private readonly NutritionAssistantOptions _options = new() { HistoryMessageLimit = 4, DailyMessageLimitPerUser = 5, MaxUserMessagesPerConversation = 30 };
    private static readonly DateOnly Today = new(2026, 10, 13);

    public NutritionAssistantServiceTests()
    {
        _uow.Register<NutritionAssistantConversation>().Register<NutritionAssistantMessage>()
            .Register<AiGenerationRequest>().Register<AiGenerationResult>()
            .Register<Food>().Register<WorkoutSession>();
        _uow.Set<UserProfile>().Add(new UserProfile
        {
            Id = 1, UserId = UserId, FirstName = "Ayşe", LastName = "Yılmaz", Goal = FitnessGoal.BuildMuscle,
            Gender = Gender.Female, DateOfBirth = new DateTime(1995, 5, 1), HeightCm = 168, WeightKg = 62,
            ActivityLevel = ActivityLevel.ModeratelyActive, Allergies = FoodAllergen.Milk,
        });
        _nutrition.Setup(n => n.GetDailyNutritionAsync(UserId, It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(SpecExample());
    }

    private NutritionAssistantService CreateService() => new(
        _uow.Object,
        new NutritionAssistantContextBuilder(_uow.Object, _nutrition.Object),
        _model,
        Options.Create(_options),
        _clock,
        NullLogger<NutritionAssistantService>.Instance);

    private Task<NutritionAssistantChatResponse> Send(string message, long? conversationId = null, DateOnly? date = null) =>
        CreateService().SendMessageAsync(UserId, new NutritionAssistantChatRequest { Message = message, ConversationId = conversationId, Date = date ?? Today });

    [Fact]
    public async Task OpenConversation_PreparesInitialAssessmentFromRealData()
    {
        var result = await CreateService().GetConversationAsync(UserId, Today);

        Assert.Null(result.ConversationId);
        Assert.True(result.AssistantAvailable);
        Assert.Empty(result.Messages);
        Assert.StartsWith("Merhaba Ayşe!", result.Greeting.Content);
        Assert.Contains("65 / 130 g", result.Greeting.Content);
        Assert.Equal(NutritionInsightEvaluator.Statuses.ProteinBehind, result.Insight.Status);
        Assert.Equal(NutritionInsightEvaluator.Questions.CompleteProtein, result.SuggestedQuestions[0]);
        Assert.Contains(NutritionInsightEvaluator.Questions.Dinner, result.SuggestedQuestions);
        Assert.Equal(5, result.RemainingMessages);
        Assert.Empty(_model.Requests);
    }

    [Fact]
    public async Task OpenConversation_WithoutMeals_ReturnsEmptyStateGreeting()
    {
        _nutrition.Setup(n => n.GetDailyNutritionAsync(UserId, Today, It.IsAny<CancellationToken>())).ReturnsAsync(Day(0, 0, 0, 0));

        var result = await CreateService().GetConversationAsync(UserId, Today);

        Assert.Equal(NutritionInsightEvaluator.Statuses.NoData, result.Insight.Status);
        Assert.Contains("henüz öğün kaydı yok", result.Greeting.Content);
    }

    [Fact]
    public async Task SendMessage_UsesCurrentNutritionContext_AndPersistsBothMessages()
    {
        var result = await Send("Bugün proteinim düşük mü?");

        var request = Assert.Single(_model.Requests);
        Assert.Contains("protein 130 g", request.Context);
        Assert.Contains("protein 65 g", request.Context);
        Assert.Contains("Kalan (hedef - kaydedilen, negatifse aşım): 640 kcal, protein 65 g", request.Context);
        Assert.Contains("Alerjiler: süt", request.Context);
        Assert.Contains("hedef kas kazanmak", request.Context);
        Assert.Contains("Kaydı olmayan öğünler: Akşam Yemeği", request.Context);
        Assert.Contains("bugün, kullanıcının yerel saati 19:30", request.Context);
        Assert.DoesNotContain("Yılmaz", request.Context); // soyadi gerekmiyor
        Assert.Equal(NutritionAssistantPrompt.Instructions, request.Instructions);
        Assert.Equal("Bugün proteinim düşük mü?", Assert.Single(request.Messages).Content);

        var conversation = Assert.Single(_uow.Set<NutritionAssistantConversation>());
        Assert.Equal(UserId, conversation.UserId);
        Assert.Equal(Today, conversation.Date);
        Assert.Equal(conversation.Id, result.ConversationId);
        var stored = _uow.Set<NutritionAssistantMessage>().OrderBy(m => m.CreatedAt).ToList();
        Assert.Equal(new[] { NutritionAssistantMessageRole.User, NutritionAssistantMessageRole.Assistant }, stored.Select(m => m.Role));
        Assert.Equal("yanıt 1", result.Message.Content);
        Assert.Equal(4, result.RemainingMessages);

        var usage = Assert.Single(_uow.Set<AiGenerationRequest>());
        Assert.Equal(AiRequestType.NutritionAssistant, usage.Type);
        Assert.Equal(AiGenerationStatus.Completed, usage.Status);
        Assert.DoesNotContain("protein", usage.Prompt); // kullanici mesaji kullanim kaydina yazilmaz
        Assert.Contains("\"outputTokens\":20", Assert.Single(_uow.Set<AiGenerationResult>()).ResultJson);
    }

    [Fact]
    public async Task FollowUpQuestions_SendPreviousMessagesAsContext()
    {
        var first = await Send("Bugün proteinim düşük mü?");
        await Send("Akşam ne yiyebilirim?", first.ConversationId);

        var request = _model.Requests[^1];
        Assert.Equal(new[] { "Bugün proteinim düşük mü?", "yanıt 1", "Akşam ne yiyebilirim?" }, request.Messages.Select(m => m.Content));
        Assert.Equal(new[] { NutritionAssistantMessageRole.User, NutritionAssistantMessageRole.Assistant, NutritionAssistantMessageRole.User }, request.Messages.Select(m => m.Role));
        Assert.Single(_uow.Set<NutritionAssistantConversation>());
    }

    [Fact]
    public async Task History_IsLimited_ToLastMessages()
    {
        var first = await Send("1");
        await Send("2", first.ConversationId);
        await Send("3", first.ConversationId);
        await Send("Tavuk yemek istemiyorum.", first.ConversationId);

        // HistoryMessageLimit = 4: yalnizca son iki soru-cevap ciftinin yanina yeni mesaj eklenir.
        Assert.Equal(new[] { "2", "yanıt 2", "3", "yanıt 3", "Tavuk yemek istemiyorum." }, _model.Requests[^1].Messages.Select(m => m.Content));
    }

    [Fact]
    public async Task History_NeverStartsWithAssistantMessage()
    {
        _options.HistoryMessageLimit = 3;
        var first = await Send("1");
        await Send("2", first.ConversationId);
        await Send("3", first.ConversationId);

        Assert.Equal(new[] { "2", "yanıt 2", "3" }, _model.Requests[^1].Messages.Select(m => m.Content));
    }

    [Fact]
    public async Task ReopenConversation_RestoresSameDayMessages()
    {
        var first = await Send("Bugün proteinim düşük mü?");

        var reopened = await CreateService().GetConversationAsync(UserId, Today);

        Assert.Equal(first.ConversationId, reopened.ConversationId);
        Assert.Equal(new[] { "user", "assistant" }, reopened.Messages.Select(m => m.Role));
        Assert.DoesNotContain("Bugün proteinim düşük mü?", reopened.SuggestedQuestions, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task NewDay_StartsNewConversation_WithThatDaysNutrition()
    {
        var yesterday = Today.AddDays(-1);
        _nutrition.Setup(n => n.GetDailyNutritionAsync(UserId, yesterday, It.IsAny<CancellationToken>())).ReturnsAsync(Day(2050, 128, 220, 68));
        var old = await Send("Dün nasıldım?", date: yesterday);

        var today = await Send("Bugün proteinim düşük mü?", old.ConversationId, Today);

        Assert.NotEqual(old.ConversationId, today.ConversationId);
        Assert.Equal(Today, today.Date);
        _nutrition.Verify(n => n.GetDailyNutritionAsync(UserId, Today, It.IsAny<CancellationToken>()), Times.Once);
        var request = _model.Requests[^1];
        Assert.Contains("protein 65 g", request.Context);
        Assert.DoesNotContain("protein 128 g", request.Context);
        Assert.Single(request.Messages); // onceki gunun mesajlari baglama girmez
        Assert.Contains("bugün değil", _model.Requests[0].Context);
    }

    [Fact]
    public async Task OtherUsersConversationId_IsRejected_WithoutCallingModel()
    {
        _uow.Set<NutritionAssistantConversation>().Add(new NutritionAssistantConversation { Id = 500, UserId = OtherUserId, Date = Today });
        _uow.Set<NutritionAssistantMessage>().Add(new NutritionAssistantMessage { Id = 501, ConversationId = 500, Role = NutritionAssistantMessageRole.User, Content = "gizli" });

        await Assert.ThrowsAsync<NotFoundException>(() => Send("merhaba", conversationId: 500));

        Assert.Empty(_model.Requests);
        Assert.Single(_uow.Set<NutritionAssistantMessage>());
        var mine = await CreateService().GetConversationAsync(UserId, Today);
        Assert.Null(mine.ConversationId);
        Assert.DoesNotContain(mine.Messages, m => m.Content == "gizli");
    }

    [Fact]
    public async Task DailyLimit_ReturnsTooManyRequests()
    {
        for (var i = 0; i < 5; i++) await Send($"soru {i}");

        await Assert.ThrowsAsync<TooManyRequestsException>(() => Send("bir soru daha"));
        Assert.Equal(5, _model.Requests.Count);
    }

    [Fact]
    public async Task DailyLimit_IsRolling24Hours()
    {
        for (var i = 0; i < 5; i++) await Send($"soru {i}");
        _clock.Now = _clock.Now.AddHours(25);

        var result = await Send("ertesi gün", date: Today.AddDays(1));

        Assert.Equal(4, result.RemainingMessages);
    }

    [Fact]
    public async Task ProviderFailure_ReturnsServiceUnavailable_AndStoresNoHalfConversation()
    {
        _model.Failure = new ExternalServiceUnavailableException("Beslenme asistanı şu an yanıt veremiyor.");

        var ex = await Assert.ThrowsAsync<ExternalServiceUnavailableException>(() => Send("Bugün proteinim düşük mü?"));

        Assert.Contains("yanıt veremiyor", ex.Message);
        Assert.Empty(_uow.Set<NutritionAssistantMessage>());
        Assert.Empty(_uow.Set<NutritionAssistantConversation>());
        Assert.Equal(AiGenerationStatus.Failed, Assert.Single(_uow.Set<AiGenerationRequest>()).Status);

        // Yeniden deneme calisir ve limit harcanmamistir.
        _model.Failure = null;
        var retry = await Send("Bugün proteinim düşük mü?");
        Assert.Equal(4, retry.RemainingMessages);
    }

    [Fact]
    public async Task ClientCancellation_PropagatesWithoutSaving()
    {
        _model.Failure = new OperationCanceledException();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Send("merhaba"));

        Assert.Empty(_uow.Set<NutritionAssistantMessage>());
    }

    [Fact]
    public async Task NotConfigured_ReturnsServiceUnavailable_ButConversationStillOpens()
    {
        _model.Configured = false;

        await Assert.ThrowsAsync<ExternalServiceUnavailableException>(() => Send("merhaba"));
        var conversation = await CreateService().GetConversationAsync(UserId, Today);

        Assert.False(conversation.AssistantAvailable);
        Assert.NotEmpty(conversation.Greeting.Content);
    }

    [Fact]
    public async Task Refusal_ReturnsSafeFixedMessage()
    {
        _model.NextResult = new NutritionAssistantModelResult(string.Empty, 100, 0, "refusal", "claude-opus-5-5", Refused: true);

        var result = await Send("...");

        Assert.Equal(NutritionAssistantPrompt.RefusalMessage, result.Message.Content);
    }

    [Fact]
    public async Task ModelMarkdown_IsReturnedAsPlainText()
    {
        _model.NextResult = new NutritionAssistantModelResult("## Öneri\n**Yoğurt** ve <b>yumurta</b>", 10, 10, "end_turn", "m", false);

        var result = await Send("ne yiyeyim");

        Assert.Equal("Öneri\nYoğurt ve <b>yumurta</b>", result.Message.Content); // HTML istemcide metin olarak gosterilir
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task EmptyMessage_IsRejected(string message)
    {
        await Assert.ThrowsAsync<AppValidationException>(() => Send(message));
        Assert.Empty(_model.Requests);
    }

    [Fact]
    public async Task TooLongMessage_IsRejected()
    {
        await Assert.ThrowsAsync<AppValidationException>(() => Send(new string('a', 1001)));
    }

    [Fact]
    public async Task FarFutureOrPastDate_IsRejected()
    {
        await Assert.ThrowsAsync<AppValidationException>(() => Send("x", date: Today.AddDays(3)));
        await Assert.ThrowsAsync<AppValidationException>(() => CreateService().GetConversationAsync(UserId, Today.AddDays(-40)));
    }

    [Fact]
    public async Task ExpiredConversations_AreDeleted_WhenNewOneStarts()
    {
        _uow.Set<NutritionAssistantConversation>().Add(new NutritionAssistantConversation { Id = 900, UserId = UserId, Date = Today.AddDays(-60), LastMessageAt = _clock.Now.UtcDateTime.AddDays(-60) });
        _uow.Set<NutritionAssistantConversation>().Add(new NutritionAssistantConversation { Id = 901, UserId = OtherUserId, Date = Today.AddDays(-60), LastMessageAt = _clock.Now.UtcDateTime.AddDays(-60) });

        await Send("merhaba");

        Assert.DoesNotContain(_uow.Set<NutritionAssistantConversation>(), c => c.Id == 900);
        Assert.Contains(_uow.Set<NutritionAssistantConversation>(), c => c.Id == 901);
    }

    [Fact]
    public async Task RapidSequentialMessages_AllLandInSameConversationInOrder()
    {
        var first = await Send("1");
        var tasks = new[] { "2", "3", "4" }.Select(m => Send(m, first.ConversationId)).ToList();
        await Task.WhenAll(tasks);

        Assert.Single(_uow.Set<NutritionAssistantConversation>());
        Assert.Equal(8, _uow.Set<NutritionAssistantMessage>().Count);
    }

    private sealed class FakeClock : TimeProvider
    {
        public FakeClock(DateTimeOffset now) => Now = now;
        public DateTimeOffset Now { get; set; }
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class FakeModelClient : INutritionAssistantModelClient
    {
        public List<NutritionAssistantModelRequest> Requests { get; } = new();
        public bool Configured { get; set; } = true;
        public Exception? Failure { get; set; }
        public NutritionAssistantModelResult? NextResult { get; set; }

        public bool IsConfigured => Configured;
        public string Model => "claude-opus-5-5";

        public Task<NutritionAssistantModelResult> CompleteAsync(NutritionAssistantModelRequest request, CancellationToken cancellationToken = default)
        {
            if (Failure is not null) throw Failure;
            Requests.Add(request);
            var result = NextResult ?? new NutritionAssistantModelResult($"yanıt {Requests.Count}", 1200, 20, "end_turn", Model, false);
            NextResult = null;
            return Task.FromResult(result);
        }
    }
}
