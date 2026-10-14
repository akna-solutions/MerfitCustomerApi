using MerfitCustomerApi.Business.Common;
using MerfitCustomerApi.Business.Dtos.Customer.FoodAnalysis;
using MerfitCustomerApi.Business.Interfaces.Services;
using MerfitCustomerApi.Business.Services.FoodAnalysis;
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

namespace MerfitCustomerApi.Business.Tests.FoodAnalysis;

/// <summary>
/// Gercek PostgreSQL'de (MERFIT_TEST_POSTGRES) uctan uca: analiz -> ayni analiz icin ayni anda gelen onaylar
/// tek ogun kaydi olusturur; baska kullanicinin analizi kaydedilemez. Saglayici sahtedir.
/// </summary>
public class FoodAnalysisPostgresTests
{
    private static AppDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(Environment.GetEnvironmentVariable(PostgresNotificationQueueTests.ConnectionVariable)).Options);

    private static readonly FoodAnalysisOptions Options_ = new() { DailyAnalysisLimitPerUser = 100 };
    private static readonly FoodAnalysisGate Gate = new(Options.Create(Options_), TimeProvider.System);

    private static (FoodAnalysisService Service, AppDbContext Db) CreateService(FakeImageProvider provider)
    {
        var db = CreateContext();
        var uow = new UnitOfWork(db);
        var service = new FoodAnalysisService(uow, provider, Gate, Options.Create(Options_), Mock.Of<IMerfitScoreService>(),
            TimeProvider.System, NullLogger<FoodAnalysisService>.Instance);
        return (service, db);
    }

    private static async Task<long> CreateUserAsync()
    {
        await using var db = CreateContext();
        var email = $"food-{Guid.NewGuid():N}@test.local";
        var user = new ApplicationUser
        {
            Email = email, NormalizedEmail = email.ToUpperInvariant(), UserName = email, NormalizedUserName = email.ToUpperInvariant(),
            PasswordHash = "x", IsActive = true, Role = UserRole.User, CreatedAt = DateTime.UtcNow,
        };
        db.ApplicationUsers.Add(user);
        await db.SaveChangesAsync();
        return user.Id;
    }

    private static async Task<long> AnalyzeAsync(long userId)
    {
        var provider = new FakeImageProvider().Returns(FoodAnalysisTestData.ValidModelJson);
        var (service, db) = CreateService(provider);
        await using (db)
        {
            return (await service.AnalyzeAsync(userId, new FoodImageUpload(FoodAnalysisTestData.Jpeg(), "image/jpeg"))).AnalysisId;
        }
    }

    private static ConfirmFoodAnalysisRequest Request() => new()
    {
        MealType = "Dinner",
        Items = { new ConfirmFoodAnalysisItem { ItemId = 1, Grams = 200 }, new ConfirmFoodAnalysisItem { ItemId = 2, Grams = 250 } },
    };

    [PostgresFact]
    public async Task ConcurrentConfirms_CreateMealItemsOnlyOnce()
    {
        var userId = await CreateUserAsync();
        var analysisId = await AnalyzeAsync(userId);
        int estimatedItems;
        await using (var lookup = CreateContext())
        {
            // Paylasilan test veritabaninda katalog satirlari olabilir; katalogla eslesmeyen kalemler kadar tahmini besin beklenir.
            var stored = await lookup.AiGenerationResults.Where(r => r.RequestId == analysisId).Select(r => r.ResultJson).FirstAsync();
            estimatedItems = System.Text.Json.JsonDocument.Parse(stored).RootElement.GetProperty("items").EnumerateArray()
                .Count(i => !i.TryGetProperty("f", out var f) || f.ValueKind == System.Text.Json.JsonValueKind.Null);
        }

        async Task<ConfirmFoodAnalysisResponse> ConfirmAsync()
        {
            var (service, db) = CreateService(new FakeImageProvider());
            await using (db)
            {
                return await service.ConfirmAsync(userId, analysisId, Request());
            }
        }

        var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => ConfirmAsync()));

        Assert.Single(results, r => !r.AlreadyLogged);
        var created = results.Single(r => !r.AlreadyLogged).Entries.Select(e => e.Id).OrderBy(id => id).ToList();
        Assert.All(results.Where(r => r.AlreadyLogged), r => Assert.Equal(created, r.Entries.Select(e => e.Id).OrderBy(id => id)));

        await using var check = CreateContext();
        var mealIds = await check.Meals.Where(m => m.UserId == userId).Select(m => m.Id).ToListAsync();
        Assert.Single(mealIds);
        Assert.Equal(2, await check.MealItems.CountAsync(i => mealIds.Contains(i.MealId)));
        // Kaybeden onaylardan ek tahmini besin olusmaz.
        Assert.Equal(estimatedItems, await check.Foods.CountAsync(f => f.CreatedUser == userId && f.Brand == FoodAnalysisService.EstimatedFoodBrand));
    }

    [PostgresFact]
    public async Task OtherUsersAnalysis_CannotBeConfirmed()
    {
        var owner = await CreateUserAsync();
        var intruder = await CreateUserAsync();
        var analysisId = await AnalyzeAsync(owner);

        var (service, db) = CreateService(new FakeImageProvider());
        await using (db)
        {
            await Assert.ThrowsAsync<NotFoundException>(() => service.ConfirmAsync(intruder, analysisId, Request()));
        }

        await using var check = CreateContext();
        Assert.False(await check.Meals.AnyAsync(m => m.UserId == intruder || m.UserId == owner));
        Assert.Null((await check.AiGenerationRequests.SingleAsync(r => r.Id == analysisId)).UpdatedAt);
    }
}
