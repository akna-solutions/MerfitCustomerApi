using MerfitCustomerApi.Business.Common;
using MerfitCustomerApi.Business.Dtos.Customer.NutritionAssistant;
using MerfitCustomerApi.Business.Interfaces.Services;
using MerfitCustomerApi.Business.Services.Nutrition;
using MerfitCustomerApi.Business.Services.NutritionAssistant;
using MerfitCustomerApi.Business.Tests.Notifications;
using MerfitCustomerApi.Domain.Entities;
using MerfitCustomerApi.Domain.Entities.Enums;
using MerfitCustomerApi.Domain.Exceptions;
using MerfitCustomerApi.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace MerfitCustomerApi.Business.Tests.NutritionAssistant;

/// <summary>
/// Gercek PostgreSQL'de (MERFIT_TEST_POSTGRES) uctan uca sohbet: gercek NutritionService verisi, ayni anda
/// gelen ilk mesajlarin tek sohbette toplanmasi ve baska kullanicinin sohbetine erisimin engellenmesi.
/// </summary>
public class NutritionAssistantPostgresTests
{
    private static AppDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(Environment.GetEnvironmentVariable(PostgresNotificationQueueTests.ConnectionVariable)).Options);

    private sealed class SlowModel : INutritionAssistantModelClient
    {
        public List<NutritionAssistantModelRequest> Requests { get; } = new();
        public bool IsConfigured => true;
        public string Model => "claude-opus-5-5";

        public async Task<NutritionAssistantModelResult> CompleteAsync(NutritionAssistantModelRequest request, CancellationToken cancellationToken = default)
        {
            lock (Requests) Requests.Add(request);
            await Task.Delay(150, cancellationToken);
            return new NutritionAssistantModelResult("tamam", 100, 5, "end_turn", Model, false);
        }
    }

    private static (NutritionAssistantService Service, AppDbContext Db) CreateService(SlowModel model)
    {
        var db = CreateContext();
        var uow = new UnitOfWork(db);
        var nutrition = new NutritionService(uow, Mock.Of<INutritionCalculator>(), Mock.Of<IMerfitScoreService>());
        var service = new NutritionAssistantService(uow, new NutritionAssistantContextBuilder(uow, nutrition), model,
            Options.Create(new NutritionAssistantOptions()), TimeProvider.System, NullLogger<NutritionAssistantService>.Instance);
        return (service, db);
    }

    private static async Task<long> CreateUserAsync()
    {
        await using var db = CreateContext();
        var email = $"assistant-{Guid.NewGuid():N}@test.local";
        var user = new ApplicationUser
        {
            Email = email, NormalizedEmail = email.ToUpperInvariant(), UserName = email, NormalizedUserName = email.ToUpperInvariant(),
            PasswordHash = "x", IsActive = true, Role = UserRole.User, CreatedAt = DateTime.UtcNow,
        };
        db.ApplicationUsers.Add(user);
        await db.SaveChangesAsync();
        db.UserProfiles.Add(new UserProfile { UserId = user.Id, FirstName = "Deniz", Username = email, Goal = FitnessGoal.LoseWeight, CreatedAt = DateTime.UtcNow });
        db.NutritionGoals.Add(new NutritionGoal { UserId = user.Id, DailyCalories = 2100, ProteinTarget = 130, CarbsTarget = 230, FatTarget = 70, WaterTargetMl = 2500, CreatedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();
        return user.Id;
    }

    private static DateOnly LocalToday() =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("Europe/Istanbul")).DateTime);

    [PostgresFact]
    public async Task ConcurrentFirstMessages_EndUpInOneConversation()
    {
        var userId = await CreateUserAsync();
        var model = new SlowModel();
        var today = LocalToday();

        async Task<NutritionAssistantChatResponse> SendAsync(string text)
        {
            var (service, db) = CreateService(model);
            await using (db)
            {
                return await service.SendMessageAsync(userId, new NutritionAssistantChatRequest { Message = text, Date = today });
            }
        }

        var results = await Task.WhenAll(SendAsync("bir"), SendAsync("iki"), SendAsync("üç"));

        Assert.Single(results.Select(r => r.ConversationId).Distinct());
        await using var check = CreateContext();
        var conversation = await check.NutritionAssistantConversations.SingleAsync(c => c.UserId == userId);
        Assert.Equal(6, await check.NutritionAssistantMessages.CountAsync(m => m.ConversationId == conversation.Id));
        Assert.Contains("Günlük hedefler: 2.100 kcal, protein 130 g", model.Requests[0].Context);
        Assert.Equal(3, await check.AiGenerationRequests.CountAsync(r => r.UserId == userId && r.Type == AiRequestType.NutritionAssistant));
    }

    [PostgresFact]
    public async Task OtherUsersConversation_IsNotAccessible()
    {
        var owner = await CreateUserAsync();
        var intruder = await CreateUserAsync();
        var model = new SlowModel();
        var today = LocalToday();

        long conversationId;
        var (ownerService, ownerDb) = CreateService(model);
        await using (ownerDb)
        {
            conversationId = (await ownerService.SendMessageAsync(owner, new NutritionAssistantChatRequest { Message = "gizli soru", Date = today })).ConversationId;
        }

        var (service, db) = CreateService(model);
        await using (db)
        {
            await Assert.ThrowsAsync<NotFoundException>(() =>
                service.SendMessageAsync(intruder, new NutritionAssistantChatRequest { Message = "x", ConversationId = conversationId, Date = today }));
            var own = await service.GetConversationAsync(intruder, today);
            Assert.Null(own.ConversationId);
            Assert.Empty(own.Messages);
        }
    }
}
