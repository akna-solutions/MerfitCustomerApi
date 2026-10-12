using System.Net;
using System.Text;
using MerfitCustomerApi.Business.Dtos.Customer.Nutrition;
using MerfitCustomerApi.Business.Interfaces.Services;
using MerfitCustomerApi.Business.Services.Foods;
using MerfitCustomerApi.Business.Services.Nutrition;
using MerfitCustomerApi.Business.Tests.TestHelpers;
using MerfitCustomerApi.Domain.Entities;
using MerfitCustomerApi.Domain.Exceptions;
using MerfitCustomerApi.Domain.Interfaces;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace MerfitCustomerApi.Business.Tests.Foods;

/// <summary>Barkodla besin sorgusu (Open Food Facts) ve gram/ml ile ogun kaydi.</summary>
public class BarcodeFoodTests
{
    private const long UserId = 7;

    // ---- OpenFoodFactsProductLookup ----

    [Fact]
    public async Task Lookup_ParsesPer100Values_AndConvertsSodiumToMilligrams()
    {
        var lookup = CreateLookup(HttpStatusCode.OK, """
            {"status":1,"product":{"product_name":"Chocolate","product_name_tr":"Çikolata","brands":"Marka A, Marka B",
             "nutriments":{"energy-kcal_100g":535,"proteins_100g":7.3,"carbohydrates_100g":"56.5","fat_100g":30.9,
             "fiber_100g":3.1,"sugars_100g":51,"sodium_100g":0.04},"image_front_small_url":"https://img/x.jpg"}}
            """);

        var product = await lookup.FindByBarcodeAsync("8690504012011");

        Assert.NotNull(product);
        Assert.Equal("Çikolata", product!.Name);
        Assert.Equal("Marka A", product.Brand);
        Assert.Equal("g", product.Unit);
        Assert.Equal(535m, product.CaloriesPer100);
        Assert.Equal(7.3m, product.ProteinPer100);
        Assert.Equal(56.5m, product.CarbsPer100);
        Assert.Equal(30.9m, product.FatPer100);
        Assert.Equal(40m, product.SodiumMgPer100);
        Assert.Equal("https://img/x.jpg", product.ImageUrl);
    }

    [Fact]
    public async Task Lookup_UsesKilojoules_WhenKcalMissing_AndMlForLiquids()
    {
        var lookup = CreateLookup(HttpStatusCode.OK, """
            {"status":1,"product":{"product_name":"Ayran","product_quantity_unit":"ml",
             "nutriments":{"energy_100g":159,"proteins_100g":1.7,"carbohydrates_100g":2.4,"fat_100g":1.8}}}
            """);

        var product = await lookup.FindByBarcodeAsync("86900000");

        Assert.NotNull(product);
        Assert.Equal("ml", product!.Unit);
        Assert.Equal(38m, Math.Round(product.CaloriesPer100));
    }

    [Theory]
    [InlineData("""{"status":0,"status_verbose":"product not found"}""")]
    [InlineData("""{"status":1,"product":{"product_name":"Enerjisiz","nutriments":{"proteins_100g":3}}}""")]
    [InlineData("""{"status":1,"product":{"nutriments":{"energy-kcal_100g":100}}}""")]
    [InlineData("""{"status":1,"product":{"product_name":"Hatali","nutriments":{"energy-kcal_100g":2500}}}""")]
    public async Task Lookup_ReturnsNull_ForMissingOrUnusableData(string body)
    {
        var lookup = CreateLookup(HttpStatusCode.OK, body);

        Assert.Null(await lookup.FindByBarcodeAsync("12345678"));
    }

    [Fact]
    public async Task Lookup_ReturnsNull_On404()
    {
        var lookup = CreateLookup(HttpStatusCode.NotFound, """{"status":0}""");

        Assert.Null(await lookup.FindByBarcodeAsync("12345678"));
    }

    [Fact]
    public async Task Lookup_Throws503Exception_WhenServiceUnreachable()
    {
        var handler = new StubHandler(_ => throw new HttpRequestException("dns"));
        var lookup = new OpenFoodFactsProductLookup(
            new HttpClient(handler) { BaseAddress = new Uri("https://off.test/") },
            NullLogger<OpenFoodFactsProductLookup>.Instance);

        await Assert.ThrowsAsync<ExternalServiceUnavailableException>(() => lookup.FindByBarcodeAsync("12345678"));
    }

    // ---- FoodService.GetByBarcodeAsync ----

    [Fact]
    public async Task GetByBarcode_ReturnsCatalogFood_WithoutCallingExternalService()
    {
        var foods = new List<Food> { new() { Id = 3, Name = "Yogurt", Barcode = "8690000000017", ServingSize = 100, ServingUnit = "g", Calories = 61 } };
        var lookup = new Mock<IFoodProductLookup>(MockBehavior.Strict);
        var service = new FoodService(CreateUnitOfWork(foods).Object, lookup.Object);

        var dto = await service.GetByBarcodeAsync(UserId, " 8690000000017 ");

        Assert.Equal(3, dto.Id);
        Assert.Equal(61, dto.Calories);
    }

    [Fact]
    public async Task GetByBarcode_AddsExternalProductToCatalog_AsUnclassifiedPer100g()
    {
        var foods = new List<Food>();
        var lookup = new Mock<IFoodProductLookup>();
        lookup.Setup(l => l.FindByBarcodeAsync("8690504012011", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExternalFoodProduct
            {
                Barcode = "8690504012011", Name = "Çikolata", Brand = "Marka", Unit = "g",
                CaloriesPer100 = 535, ProteinPer100 = 7.3m, CarbsPer100 = 56.5m, FatPer100 = 30.9m, SodiumMgPer100 = 40,
            });
        var service = new FoodService(CreateUnitOfWork(foods).Object, lookup.Object);

        var dto = await service.GetByBarcodeAsync(UserId, "8690504012011");

        var food = Assert.Single(foods);
        Assert.Equal("8690504012011", food.Barcode);
        Assert.Equal(100m, food.ServingSize);
        Assert.Equal("g", food.ServingUnit);
        Assert.Equal(535m, food.Calories);
        Assert.Null(food.Category);
        Assert.Equal(UserId, food.CreatedUser);
        Assert.Equal(535, dto.Calories);
        Assert.Equal(100m, dto.ServingSize);
    }

    [Fact]
    public async Task GetByBarcode_ThrowsNotFound_WhenProductUnknown()
    {
        var lookup = new Mock<IFoodProductLookup>();
        lookup.Setup(l => l.FindByBarcodeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync((ExternalFoodProduct?)null);
        var foods = new List<Food>();
        var service = new FoodService(CreateUnitOfWork(foods).Object, lookup.Object);

        await Assert.ThrowsAsync<NotFoundException>(() => service.GetByBarcodeAsync(UserId, "12345678"));
        Assert.Empty(foods);
    }

    [Theory]
    [InlineData("1234567")]
    [InlineData("123456789012345")]
    [InlineData("12345abc")]
    [InlineData("")]
    public async Task GetByBarcode_RejectsInvalidBarcodes(string barcode)
    {
        var service = new FoodService(CreateUnitOfWork(new List<Food>()).Object, new Mock<IFoodProductLookup>(MockBehavior.Strict).Object);

        await Assert.ThrowsAsync<AppValidationException>(() => service.GetByBarcodeAsync(UserId, barcode));
    }

    // ---- NutritionService.LogMealItemAsync with Amount ----

    [Fact]
    public async Task LogMeal_WithAmountInGrams_ScalesPer100gValues()
    {
        var foods = new List<Food> { new() { Id = 1, Name = "Çikolata", ServingSize = 100, ServingUnit = "g", Calories = 535, Protein = 7.3m, Carbs = 56.5m, Fat = 30.9m } };
        var mealItems = new List<MealItem>();
        var service = CreateNutritionService(foods, mealItems);

        var entry = await service.LogMealItemAsync(UserId, new LogMealItemRequest { MealType = "Snack", FoodId = 1, Amount = 37 });

        var item = Assert.Single(mealItems);
        Assert.Equal(0.37m, item.Quantity);
        Assert.Equal(197.95m, item.Calories);
        Assert.Equal(2.7m, item.Protein);
        Assert.Equal(198, entry.Calories);
    }

    [Fact]
    public async Task LogMeal_WithAmount_UsesExactRatio_ForNonHundredServings()
    {
        var foods = new List<Food> { new() { Id = 1, Name = "Yulaf", ServingSize = 30, ServingUnit = "g", Calories = 117 } };
        var mealItems = new List<MealItem>();
        var service = CreateNutritionService(foods, mealItems);

        await service.LogMealItemAsync(UserId, new LogMealItemRequest { MealType = "Breakfast", FoodId = 1, Amount = 40 });

        Assert.Equal(156m, Assert.Single(mealItems).Calories);
    }

    [Fact]
    public async Task LogMeal_WithAmount_RejectsCountableUnits()
    {
        var foods = new List<Food> { new() { Id = 1, Name = "Yumurta", ServingSize = 1, ServingUnit = "adet", Calories = 78 } };
        var mealItems = new List<MealItem>();
        var service = CreateNutritionService(foods, mealItems);

        await Assert.ThrowsAsync<AppValidationException>(() =>
            service.LogMealItemAsync(UserId, new LogMealItemRequest { MealType = "Breakfast", FoodId = 1, Amount = 50 }));
        Assert.Empty(mealItems);
    }

    [Fact]
    public async Task LogMeal_WithoutAmount_KeepsPortionMultiplierBehavior()
    {
        var foods = new List<Food> { new() { Id = 1, Name = "Yumurta", ServingSize = 1, ServingUnit = "adet", Calories = 78 } };
        var mealItems = new List<MealItem>();
        var service = CreateNutritionService(foods, mealItems);

        await service.LogMealItemAsync(UserId, new LogMealItemRequest { MealType = "Breakfast", FoodId = 1, Quantity = 2 });

        Assert.Equal(156m, Assert.Single(mealItems).Calories);
    }

    // ---- helpers ----

    private static OpenFoodFactsProductLookup CreateLookup(HttpStatusCode status, string body)
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(status)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        });
        return new OpenFoodFactsProductLookup(
            new HttpClient(handler) { BaseAddress = new Uri("https://off.test/") },
            NullLogger<OpenFoodFactsProductLookup>.Instance);
    }

    private static Mock<IUnitOfWork> CreateUnitOfWork(List<Food> foods)
    {
        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(u => u.Repository<Food>()).Returns(MockRepositoryFactory.Create(foods).Object);
        return unitOfWork;
    }

    private static NutritionService CreateNutritionService(List<Food> foods, List<MealItem> mealItems)
    {
        var unitOfWork = CreateUnitOfWork(foods);
        unitOfWork.Setup(u => u.Repository<Meal>()).Returns(MockRepositoryFactory.Create(new List<Meal>()).Object);
        unitOfWork.Setup(u => u.Repository<MealItem>()).Returns(MockRepositoryFactory.Create(mealItems).Object);
        return new NutritionService(unitOfWork.Object, Mock.Of<INutritionCalculator>(), Mock.Of<IMerfitScoreService>());
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(respond(request));
    }
}
