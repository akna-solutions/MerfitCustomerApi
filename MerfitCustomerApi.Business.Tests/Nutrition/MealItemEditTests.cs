using MerfitCustomerApi.Business.Dtos.Customer.Nutrition;
using MerfitCustomerApi.Business.Interfaces.Services;
using MerfitCustomerApi.Business.Services.Nutrition;
using MerfitCustomerApi.Business.Tests.TestHelpers;
using MerfitCustomerApi.Domain.Entities;
using MerfitCustomerApi.Domain.Entities.Enums;
using MerfitCustomerApi.Domain.Exceptions;
using Moq;
using Xunit;

namespace MerfitCustomerApi.Business.Tests.Nutrition;

/// <summary>Gunlukteki bir kaydin detayi, miktarinin degistirilmesi ve silinmesi.</summary>
public class MealItemEditTests
{
    private const long UserId = 7;
    private const long OtherUserId = 8;

    private readonly InMemoryUnitOfWork _uow = new();
    private readonly Mock<IMerfitScoreService> _score = new();
    private readonly NutritionService _service;

    public MealItemEditTests()
    {
        _uow.Set<Food>().AddRange(new[]
        {
            // Katalogdaki deger sonradan degismis: kayit 100 g = 130 kcal ile yapilmisti.
            new Food { Id = 1, Name = "Pirinç", ServingSize = 100, ServingUnit = "g", Calories = 999 },
            new Food { Id = 2, Name = "Yumurta", ServingSize = 1, ServingUnit = "adet", Calories = 78, Protein = 6 },
        });
        _uow.Set<Meal>().AddRange(new[]
        {
            new Meal { Id = 10, UserId = UserId, MealType = MealType.Lunch },
            new Meal { Id = 11, UserId = OtherUserId, MealType = MealType.Breakfast },
        });
        _uow.Set<MealItem>().AddRange(new[]
        {
            new MealItem { Id = 100, MealId = 10, FoodId = 1, Quantity = 1.5m, ServingSize = 100, Calories = 195, Protein = 4.5m, Carbs = 42, Fat = 0.45m },
            new MealItem { Id = 101, MealId = 10, FoodId = 2, Quantity = 2, ServingSize = 1, Calories = 156, Protein = 12 },
            new MealItem { Id = 200, MealId = 11, FoodId = 2, Quantity = 1, ServingSize = 1, Calories = 78 },
        });
        _service = new NutritionService(_uow.Object, Mock.Of<INutritionCalculator>(), _score.Object);
    }

    [Fact]
    public async Task Get_ReturnsAmountAndSnapshotPerServingValues()
    {
        var detail = await _service.GetMealItemAsync(UserId, 100);

        Assert.Equal("Öğle Yemeği", detail.Type);
        Assert.Equal("Pirinç", detail.Name);
        Assert.Equal(150m, detail.Amount);
        Assert.Equal(130, detail.Food.Calories);
        Assert.Equal(100m, detail.Food.ServingSize);
        Assert.Equal("g", detail.Food.ServingUnit);
    }

    [Fact]
    public async Task Get_CountableFood_HasNoAmount()
    {
        var detail = await _service.GetMealItemAsync(UserId, 101);

        Assert.Null(detail.Amount);
        Assert.Equal(2m, detail.Quantity);
        Assert.Equal(78, detail.Food.Calories);
    }

    [Fact]
    public async Task Update_WithAmount_RescalesFromSnapshot_AndRefreshesScore()
    {
        var entry = await _service.UpdateMealItemAsync(UserId, 100, new UpdateMealItemRequest { Amount = 200 });

        var item = _uow.Set<MealItem>().Single(i => i.Id == 100);
        Assert.Equal(2m, item.Quantity);
        Assert.Equal(260m, item.Calories);
        Assert.Equal(6m, item.Protein);
        Assert.Equal(260, entry.Calories);
        _score.Verify(s => s.TryRefreshUserAsync(UserId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Update_WithQuantity_ForCountableFood()
    {
        await _service.UpdateMealItemAsync(UserId, 101, new UpdateMealItemRequest { Quantity = 3 });

        var item = _uow.Set<MealItem>().Single(i => i.Id == 101);
        Assert.Equal(234m, item.Calories);
        Assert.Equal(18m, item.Protein);
    }

    [Fact]
    public async Task Update_WithAmount_RejectsCountableFood()
    {
        await Assert.ThrowsAsync<AppValidationException>(() =>
            _service.UpdateMealItemAsync(UserId, 101, new UpdateMealItemRequest { Amount = 50 }));
    }

    [Fact]
    public async Task Update_WithoutAmountOrQuantity_IsRejected()
    {
        await Assert.ThrowsAsync<AppValidationException>(() =>
            _service.UpdateMealItemAsync(UserId, 101, new UpdateMealItemRequest()));
    }

    [Fact]
    public async Task Delete_RemovesItem_AndRefreshesScore()
    {
        await _service.DeleteMealItemAsync(UserId, 101);

        Assert.DoesNotContain(_uow.Set<MealItem>(), i => i.Id == 101);
        _score.Verify(s => s.TryRefreshUserAsync(UserId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task OtherUsersItem_IsNotFound_ForAllOperations()
    {
        await Assert.ThrowsAsync<NotFoundException>(() => _service.GetMealItemAsync(UserId, 200));
        await Assert.ThrowsAsync<NotFoundException>(() =>
            _service.UpdateMealItemAsync(UserId, 200, new UpdateMealItemRequest { Quantity = 2 }));
        await Assert.ThrowsAsync<NotFoundException>(() => _service.DeleteMealItemAsync(UserId, 200));

        Assert.Contains(_uow.Set<MealItem>(), i => i.Id == 200);
        Assert.Equal(78m, _uow.Set<MealItem>().Single(i => i.Id == 200).Calories);
    }

    [Fact]
    public async Task MissingItem_IsNotFound()
    {
        await Assert.ThrowsAsync<NotFoundException>(() => _service.DeleteMealItemAsync(UserId, 999));
    }
}
