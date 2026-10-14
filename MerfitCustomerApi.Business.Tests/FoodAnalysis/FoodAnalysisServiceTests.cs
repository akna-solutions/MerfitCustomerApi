using System.Text.Json;
using MerfitCustomerApi.Business.Common;
using MerfitCustomerApi.Business.Dtos.Customer.FoodAnalysis;
using MerfitCustomerApi.Business.Interfaces.Services;
using MerfitCustomerApi.Business.Services.FoodAnalysis;
using MerfitCustomerApi.Business.Services.Foods;
using MerfitCustomerApi.Business.Tests.TestHelpers;
using MerfitCustomerApi.Domain.Entities;
using MerfitCustomerApi.Domain.Entities.Enums;
using MerfitCustomerApi.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;
using static MerfitCustomerApi.Business.Tests.FoodAnalysis.FoodAnalysisTestData;

namespace MerfitCustomerApi.Business.Tests.FoodAnalysis;

/// <summary>
/// Analiz ve onay akisi: dosya dogrulama, kota, saglayici hatalari, gecersiz model yaniti, katalog eslestirmesi,
/// otomatik kayit olmamasi, sahiplik, mukerrer kayit korumasi. Saglayici sahtedir; gercek API cagrilmaz.
/// </summary>
public class FoodAnalysisServiceTests
{
    private const long UserId = 42;
    private const long OtherUserId = 7;
    private const long RiceFoodId = 10;

    // 2026-10-14 08:00 UTC = 11:00 Istanbul.
    private readonly FakeClock _clock = new(new DateTimeOffset(2026, 10, 14, 8, 0, 0, TimeSpan.Zero));
    private readonly InMemoryUnitOfWork _uow = new();
    private readonly FakeImageProvider _provider = new();
    private readonly Mock<IMerfitScoreService> _score = new();
    private readonly FoodAnalysisOptions _options = new() { DailyAnalysisLimitPerUser = 3, MaxRetries = 1 };
    private FoodAnalysisGate? _gate;

    public FoodAnalysisServiceTests()
    {
        _uow.Register<AiGenerationRequest>().Register<AiGenerationResult>().Register<Meal>().Register<MealItem>();
        _uow.Set<Food>().AddRange(new[]
        {
            new Food { Id = RiceFoodId, Name = "Pirinç Pilavı", ServingSize = 100, ServingUnit = "g", Calories = 130, Protein = 2.7m, Carbs = 28, Fat = 0.3m },
            // Barkodlu urunler ve adet birimli besinler eslestirmeye aday degildir.
            new Food { Id = 11, Name = "Pirinç Pilavı", Barcode = "8690000000001", ServingSize = 100, ServingUnit = "g", Calories = 160, Protein = 3, Carbs = 30, Fat = 3 },
            new Food { Id = 12, Name = "Mantı", ServingSize = 1, ServingUnit = "porsiyon", Calories = 400, Protein = 15, Carbs = 50, Fat = 15 },
        });
    }

    private FoodAnalysisService CreateService() => new(
        _uow.Object,
        _provider,
        _gate ??= new FoodAnalysisGate(Options.Create(_options), _clock),
        Options.Create(_options),
        _score.Object,
        _clock,
        NullLogger<FoodAnalysisService>.Instance);

    private Task<FoodAnalysisResultDto> Analyze(byte[]? image = null, long userId = UserId) =>
        CreateService().AnalyzeAsync(userId, new FoodImageUpload(image ?? Jpeg(), "image/jpeg"));

    private static ConfirmFoodAnalysisRequest Confirm(params (int ItemId, decimal Grams)[] items) => new()
    {
        MealType = "Lunch",
        Date = new DateOnly(2026, 10, 14),
        Items = items.Select(i => new ConfirmFoodAnalysisItem { ItemId = i.ItemId, Grams = i.Grams }).ToList(),
    };

    [Fact]
    public async Task EmptyPhoto_IsRejected_WithoutCallingProvider()
    {
        var ex = await Assert.ThrowsAsync<AppValidationException>(() => Analyze(Array.Empty<byte>()));

        Assert.Contains("boş", ex.Message);
        Assert.Empty(_provider.Requests);
        Assert.Empty(_uow.Set<AiGenerationRequest>());
    }

    [Fact]
    public async Task CorruptedOrUnsupportedFile_IsRejected_WithoutCallingProvider()
    {
        await Assert.ThrowsAsync<AppValidationException>(() => Analyze(Jpeg(withEnd: false)));
        await Assert.ThrowsAsync<AppValidationException>(() => Analyze("GIF89a and some more bytes to pass size check"u8.ToArray()));
        // Content-Type "image/jpeg" olsa da icerik metin.
        await Assert.ThrowsAsync<AppValidationException>(() => Analyze("{\"not\":\"an image\", \"padding\": \"..........\"}"u8.ToArray()));

        Assert.Empty(_provider.Requests);
    }

    [Fact]
    public async Task OversizedOrTinyPhoto_IsRejected()
    {
        _options.MaxImageBytes = 200;
        await Assert.ThrowsAsync<AppValidationException>(() => Analyze(Jpeg()));

        _options.MaxImageBytes = 4 * 1024 * 1024;
        var ex = await Assert.ThrowsAsync<AppValidationException>(() => Analyze(Jpeg(120, 90)));
        Assert.Contains("küçük", ex.Message);
        Assert.Empty(_provider.Requests);
    }

    [Fact]
    public async Task ProviderNotConfigured_Returns503()
    {
        _provider.Configured = false;

        await Assert.ThrowsAsync<ExternalServiceUnavailableException>(() => Analyze());
    }

    [Fact]
    public async Task Success_UsesCatalogValuesWhenMatched_AndModelEstimateOtherwise_WithoutLoggingAMeal()
    {
        _provider.Returns(ValidModelJson);

        var result = await Analyze();

        Assert.True(result.AnalysisId > 0);
        Assert.Equal("Pilav ve mantı", result.FoodName);
        Assert.True(result.IsEstimate);

        var rice = result.Items[0];
        Assert.Equal((1, "Pirinç Pilavı", FoodAnalysisNutritionSources.Catalog, RiceFoodId), (rice.ItemId, rice.FoodName, rice.NutritionSource, rice.MatchedFoodId));
        // Katalog: 130 kcal / 100 g -> 180 g = 234 kcal (model 150 kcal/100 g dese de).
        Assert.Equal((180m, 234m, 4.9m, 50.4m, 0.5m), (rice.EstimatedGrams, rice.Calories, rice.Protein, rice.Carbs, rice.Fat));
        Assert.False(rice.NeedsPortionConfirmation);

        var manti = result.Items[1];
        Assert.Equal((FoodAnalysisNutritionSources.AiEstimate, (long?)null), (manti.NutritionSource, manti.MatchedFoodId));
        Assert.Equal((425m, 17.5m, 50m, 17.5m), (manti.Calories, manti.Protein, manti.Carbs, manti.Fat));
        Assert.Equal(("high", true), (manti.PortionUncertainty, manti.NeedsPortionConfirmation));
        Assert.Equal((150m, 350m), (manti.MinGrams, manti.MaxGrams));

        Assert.Equal((430m, 659m), (result.Totals.EstimatedGrams, result.Totals.Calories));
        Assert.True(result.NeedsPortionConfirmation);
        Assert.Contains(result.Warnings, w => w.Contains("Yoğurtlu mantı") && w.Contains("tahmini"));
        Assert.Contains(result.Warnings, w => w.Contains("Tanımlanamayan") && w.Contains("koyu renkli sos"));
        Assert.Equal(2, result.RemainingAnalyses);

        // Analiz otomatik kaydedilmez.
        Assert.Empty(_uow.Set<Meal>());
        Assert.Empty(_uow.Set<MealItem>());
        _score.VerifyNoOtherCalls();

        var usage = Assert.Single(_uow.Set<AiGenerationRequest>());
        Assert.Equal((UserId, AiRequestType.FoodPhotoAnalysis, AiGenerationStatus.Completed), (usage.UserId, usage.Type, usage.Status));
        Assert.DoesNotContain("/9j/", usage.Prompt); // Base64 JPEG baslangici
        var stored = Assert.Single(_uow.Set<AiGenerationResult>());
        Assert.True(stored.ResultJson.Length <= 2000);
        Assert.DoesNotContain(Convert.ToBase64String(Jpeg()), stored.ResultJson);
    }

    [Fact]
    public async Task ImageIsSentToProvider_WithDetectedMimeType()
    {
        _provider.Returns(ValidModelJson);

        await CreateService().AnalyzeAsync(UserId, new FoodImageUpload(Png(), "application/octet-stream"));

        var request = Assert.Single(_provider.Requests);
        Assert.Equal("image/png", request.MimeType);
        Assert.Equal(Png(), request.Image.ToArray());
    }

    [Fact]
    public async Task ModelError_Returns503_RecordsFailure_AndDoesNotConsumeQuota()
    {
        _provider
            .Throws(new NutritionAiProviderException("Fake", NutritionAiFailureKind.Transient, 500))
            .Throws(new NutritionAiProviderException("Fake", NutritionAiFailureKind.Transient, 502));

        var ex = await Assert.ThrowsAsync<ExternalServiceUnavailableException>(() => Analyze());

        Assert.Equal(FoodAnalysisService.UnavailableMessage, ex.Message);
        Assert.Equal(2, _provider.Requests.Count); // 1 ek deneme
        var failure = Assert.Single(_uow.Set<AiGenerationRequest>());
        Assert.Equal(AiGenerationStatus.Failed, failure.Status);
        Assert.Empty(_uow.Set<MealItem>());

        // Basarisiz analiz kotadan dusmez.
        _provider.Returns(ValidModelJson);
        Assert.Equal(2, (await Analyze()).RemainingAnalyses);
    }

    [Fact]
    public async Task Timeout_Returns503WithTimeoutMessage_WithoutRetry()
    {
        _provider.Throws(new NutritionAiProviderException("Fake", NutritionAiFailureKind.Timeout));

        var ex = await Assert.ThrowsAsync<ExternalServiceUnavailableException>(() => Analyze());

        Assert.Equal(FoodAnalysisService.TimeoutMessage, ex.Message);
        Assert.Single(_provider.Requests);
    }

    [Fact]
    public async Task ProviderQuota_Returns503WithRetryAfter_AndPausesFurtherCalls()
    {
        _provider.Throws(new NutritionAiProviderException("Fake", NutritionAiFailureKind.RateLimited, 429, TimeSpan.FromSeconds(20)));

        var first = await Assert.ThrowsAsync<ExternalServiceUnavailableException>(() => Analyze());
        Assert.Equal(TimeSpan.FromSeconds(20), first.RetryAfter);

        // Bekleme suresi icinde saglayiciya yeni istek gitmez.
        var second = await Assert.ThrowsAsync<ExternalServiceUnavailableException>(() => Analyze());
        Assert.Equal(FoodAnalysisService.BusyMessage, second.Message);
        Assert.Single(_provider.Requests);

        _clock.Now = _clock.Now.AddSeconds(21);
        _provider.Returns(ValidModelJson);
        await Analyze();
        Assert.Equal(2, _provider.Requests.Count);
    }

    [Theory]
    [InlineData("tamamen serbest metin: tabakta pilav var")]
    [InlineData("{\"containsFood\": true, \"items\": [{\"name\": \"Pilav\"}]}")]
    [InlineData("")]
    public async Task InvalidModelJson_Returns503_AndCreatesNoAnalysisToConfirm(string json)
    {
        _provider.Returns(json);

        var ex = await Assert.ThrowsAsync<ExternalServiceUnavailableException>(() => Analyze());

        Assert.Equal(FoodAnalysisService.InvalidResultMessage, ex.Message);
        var failure = Assert.Single(_uow.Set<AiGenerationRequest>());
        Assert.Equal(AiGenerationStatus.Failed, failure.Status);
        Assert.DoesNotContain(_uow.Set<AiGenerationResult>(), r => r.ResultJson.Contains("pilav"));

        await Assert.ThrowsAsync<AppValidationException>(() => CreateService().ConfirmAsync(UserId, failure.Id, Confirm((1, 100))));
        Assert.Empty(_uow.Set<MealItem>());
    }

    [Fact]
    public async Task RefusedBySafetyFilter_Returns400()
    {
        _provider.Returns(string.Empty, refused: true);

        var ex = await Assert.ThrowsAsync<AppValidationException>(() => Analyze());

        Assert.Equal(FoodAnalysisService.RefusedMessage, ex.Message);
    }

    [Fact]
    public async Task NoFoodInPhoto_Returns400_AndCountsTowardQuota()
    {
        _provider.Returns("""{"containsFood": false, "mealName": "", "items": [], "unidentifiedItems": [], "overallConfidence": 0.1}""");

        var ex = await Assert.ThrowsAsync<AppValidationException>(() => Analyze());

        Assert.Equal(FoodAnalysisService.NoFoodMessage, ex.Message);
        Assert.Equal(AiGenerationStatus.Completed, Assert.Single(_uow.Set<AiGenerationRequest>()).Status);
        Assert.Empty(_uow.Set<AiGenerationResult>());
    }

    [Fact]
    public async Task DailyQuota_IsEnforcedPerUser()
    {
        for (var i = 0; i < 3; i++)
        {
            _provider.Returns(ValidModelJson);
            await Analyze();
        }

        var ex = await Assert.ThrowsAsync<TooManyRequestsException>(() => Analyze());
        Assert.NotNull(ex.RetryAfter);
        Assert.Equal(3, _provider.Requests.Count);

        // Baska kullanicinin kotasi etkilenmez.
        _provider.Returns(ValidModelJson);
        Assert.Equal(2, (await Analyze(userId: OtherUserId)).RemainingAnalyses);

        // 24 saat sonra hak yenilenir.
        _clock.Now = _clock.Now.AddHours(24).AddMinutes(1);
        _provider.Returns(ValidModelJson);
        await Analyze();
    }

    [Fact]
    public async Task Status_ReportsRemainingQuotaAndNotice_WithoutCallingProvider()
    {
        var status = await CreateService().GetStatusAsync(UserId);
        Assert.True(status.Enabled);
        Assert.Equal(3, status.DailyLimit);
        Assert.Equal(3, status.RemainingAnalyses);
        Assert.Contains("Google", status.DataNotice);

        _provider.Returns(ValidModelJson);
        await Analyze();
        Assert.Equal(2, (await CreateService().GetStatusAsync(UserId)).RemainingAnalyses);
        Assert.Equal(3, (await CreateService().GetStatusAsync(OtherUserId)).RemainingAnalyses);
        Assert.Single(_provider.Requests);

        _provider.Configured = false;
        Assert.False((await CreateService().GetStatusAsync(UserId)).Enabled);
    }

    [Fact]
    public async Task Confirm_LogsSelectedItems_WithServerSideValues_AndConfirmedGrams()
    {
        _provider.Returns(ValidModelJson);
        var analysis = await Analyze();

        var response = await CreateService().ConfirmAsync(UserId, analysis.AnalysisId, Confirm((1, 200), (2, 300)));

        Assert.False(response.AlreadyLogged);
        Assert.Equal(2, response.Entries.Count);
        Assert.All(response.Entries, e => Assert.Equal("Öğle Yemeği", e.Type));

        var meal = Assert.Single(_uow.Set<Meal>());
        Assert.Equal((UserId, MealType.Lunch, new DateTime(2026, 10, 14, 0, 0, 0, DateTimeKind.Utc)), (meal.UserId, meal.MealType, meal.Date));

        var items = _uow.Set<MealItem>();
        Assert.Equal(2, items.Count);
        // Pilav: katalogdaki Food kullanilir (200 g = 2 porsiyon).
        var rice = items.Single(i => i.FoodId == RiceFoodId);
        Assert.Equal((2m, 260m, 5.4m, 56m, 0.6m), (rice.Quantity, rice.Calories, rice.Protein, rice.Carbs, rice.Fat));
        // Manti: katalogda yok; analizdeki 100 g degerleriyle kullaniciya ait tahmini Food olusturulur.
        var estimated = _uow.Set<Food>().Single(f => f.Brand == FoodAnalysisService.EstimatedFoodBrand);
        Assert.Equal(("Yoğurtlu mantı", 100m, "g", 170m, (long?)UserId, (FoodCategory?)null), (estimated.Name, estimated.ServingSize, estimated.ServingUnit, estimated.Calories, estimated.CreatedUser, estimated.Category));
        var manti = items.Single(i => i.FoodId == estimated.Id);
        Assert.Equal((3m, 510m), (manti.Quantity, manti.Calories));

        _score.Verify(s => s.TryRefreshUserAsync(UserId, It.IsAny<CancellationToken>()), Times.Once);
        _uow.Mock.Verify(u => u.BeginTransactionAsync(It.IsAny<CancellationToken>()), Times.Once);
        _uow.Mock.Verify(u => u.CommitTransactionAsync(It.IsAny<CancellationToken>()), Times.Once);
        Assert.NotNull(_uow.Set<AiGenerationRequest>().Single().UpdatedAt);
    }

    [Fact]
    public async Task Confirm_OnlySelectedItemsAreLogged()
    {
        _provider.Returns(ValidModelJson);
        var analysis = await Analyze();

        var response = await CreateService().ConfirmAsync(UserId, analysis.AnalysisId, Confirm((1, 150)));

        Assert.Single(response.Entries);
        Assert.Equal(RiceFoodId, Assert.Single(_uow.Set<MealItem>()).FoodId);
        Assert.DoesNotContain(_uow.Set<Food>(), f => f.Brand == FoodAnalysisService.EstimatedFoodBrand);
    }

    [Fact]
    public async Task RepeatedConfirm_DoesNotCreateDuplicateMeals_AndReturnsFirstResult()
    {
        _provider.Returns(ValidModelJson);
        var analysis = await Analyze();
        var first = await CreateService().ConfirmAsync(UserId, analysis.AnalysisId, Confirm((1, 200), (2, 300)));

        var second = await CreateService().ConfirmAsync(UserId, analysis.AnalysisId, Confirm((1, 999), (2, 999)));

        Assert.True(second.AlreadyLogged);
        Assert.Equal(first.Entries.Select(e => (e.Id, e.Name, e.Calories)), second.Entries.Select(e => (e.Id, e.Name, e.Calories)));
        Assert.Equal(2, _uow.Set<MealItem>().Count);
        _score.Verify(s => s.TryRefreshUserAsync(UserId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ConcurrentConfirm_LosingRequestCreatesNothing()
    {
        _provider.Returns(ValidModelJson);
        var analysis = await Analyze();
        // Diger istek once kaydetti: bu istegin "kaydedildi" isareti eszamanlilik catismasina dusser.
        _uow.Set<AiGenerationResult>().Add(new AiGenerationResult
        {
            Id = 9001,
            RequestId = analysis.AnalysisId,
            ResultJson = """{"kind":"food_analysis_logged","mealItemIds":[],"mealType":"Lunch","date":"2026-10-14"}""",
        });
        _uow.Mock.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ThrowsAsync(new DbUpdateConcurrencyException("conflict"));

        var response = await CreateService().ConfirmAsync(UserId, analysis.AnalysisId, Confirm((1, 200)));

        Assert.True(response.AlreadyLogged);
        Assert.Empty(_uow.Set<MealItem>());
        _uow.Mock.Verify(u => u.RollbackTransactionAsync(It.IsAny<CancellationToken>()), Times.Once);
        _score.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Confirm_ForeignOrUnknownAnalysis_Returns404()
    {
        _provider.Returns(ValidModelJson);
        var analysis = await Analyze(userId: OtherUserId);

        await Assert.ThrowsAsync<NotFoundException>(() => CreateService().ConfirmAsync(UserId, analysis.AnalysisId, Confirm((1, 100))));
        await Assert.ThrowsAsync<NotFoundException>(() => CreateService().ConfirmAsync(UserId, 123456, Confirm((1, 100))));
        Assert.Empty(_uow.Set<MealItem>());
    }

    [Fact]
    public async Task Confirm_UnknownOrDuplicateItem_IsRejected()
    {
        _provider.Returns(ValidModelJson);
        var analysis = await Analyze();

        await Assert.ThrowsAsync<AppValidationException>(() => CreateService().ConfirmAsync(UserId, analysis.AnalysisId, Confirm((9, 100))));
        await Assert.ThrowsAsync<AppValidationException>(() => CreateService().ConfirmAsync(UserId, analysis.AnalysisId, Confirm((1, 100), (1, 50))));
        await Assert.ThrowsAsync<AppValidationException>(() => CreateService().ConfirmAsync(UserId, analysis.AnalysisId, new ConfirmFoodAnalysisRequest { MealType = "Brunch", Items = Confirm((1, 100)).Items }));
        Assert.Empty(_uow.Set<MealItem>());
        Assert.Null(_uow.Set<AiGenerationRequest>().Single().UpdatedAt);
    }

    [Fact]
    public async Task Confirm_ExpiredAnalysis_IsRejected()
    {
        _provider.Returns(ValidModelJson);
        var analysis = await Analyze();
        _clock.Now = _clock.Now.AddHours(25);

        var ex = await Assert.ThrowsAsync<AppValidationException>(() => CreateService().ConfirmAsync(UserId, analysis.AnalysisId, Confirm((1, 100))));

        Assert.Contains("süresi doldu", ex.Message);
        Assert.Empty(_uow.Set<MealItem>());
    }

    [Fact]
    public async Task Confirm_FailureWhileLogging_RollsBackAndAllowsRetry()
    {
        _provider.Returns(ValidModelJson);
        var analysis = await Analyze();
        _score.Setup(s => s.TryRefreshUserAsync(It.IsAny<long>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        _uow.Mock.Setup(u => u.CommitTransactionAsync(It.IsAny<CancellationToken>())).ThrowsAsync(new DbUpdateException("db down"));

        await Assert.ThrowsAsync<DbUpdateException>(() => CreateService().ConfirmAsync(UserId, analysis.AnalysisId, Confirm((1, 100))));

        _uow.Mock.Verify(u => u.RollbackTransactionAsync(It.IsAny<CancellationToken>()), Times.Once);
        _score.Verify(s => s.TryRefreshUserAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task EstimatedFoods_AreHiddenFromCatalogSearch()
    {
        _provider.Returns(ValidModelJson);
        var analysis = await Analyze();
        await CreateService().ConfirmAsync(UserId, analysis.AnalysisId, Confirm((2, 250)));

        var search = await new FoodService(_uow.Object, Mock.Of<IFoodProductLookup>())
            .SearchFoodsAsync(new() { Search = "mantı" });

        Assert.Equal(new[] { "Mantı" }, search.Items.Select(i => i.Name));
    }

    [Fact]
    public async Task OldResults_ArePurgedAfterRetention()
    {
        _options.ResultRetentionDays = 30;
        _provider.Returns(ValidModelJson);
        await Analyze();
        Assert.Single(_uow.Set<AiGenerationResult>());

        _clock.Now = _clock.Now.AddDays(31);
        _provider.Returns(ValidModelJson);
        await Analyze();

        // Eski analizin sonucu (yemek adlari) silinir, kullanim kaydi kalir.
        Assert.Single(_uow.Set<AiGenerationResult>());
        Assert.Equal(2, _uow.Set<AiGenerationRequest>().Count);
    }
}
