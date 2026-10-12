using MerfitCustomerApi.Business.Services.Subscriptions;
using MerfitCustomerApi.Business.Tests.TestHelpers;
using MerfitCustomerApi.Domain.Entities;
using MerfitCustomerApi.Domain.Entities.Enums;
using MerfitCustomerApi.Domain.Interfaces;
using Moq;
using Xunit;

namespace MerfitCustomerApi.Business.Tests.Services;

/// <summary>GET /api/subscription'in kullanicinin gercek abonelik kaydini dondurdugunu dogrular.</summary>
public class SubscriptionServiceTests
{
    private const long UserId = 7;

    private readonly List<Subscription> _subscriptions = new();
    private readonly List<SubscriptionProduct> _products = new()
    {
        new() { Id = 1, Code = "plus_monthly", Name = "Plus Aylık", BillingPeriod = BillingPeriod.Monthly, Price = 9.99m, Currency = "USD", IsActive = true },
        new() { Id = 2, Code = "plus_yearly", Name = "Plus Yıllık", BillingPeriod = BillingPeriod.Yearly, Price = 79.99m, Currency = "USD", IsActive = true },
        new() { Id = 3, Code = "legacy", Name = "Eski Plan", BillingPeriod = BillingPeriod.Monthly, Price = 4.99m, Currency = "USD", IsActive = false },
    };
    private readonly List<Feature> _features = new() { new() { Id = 1, Code = "premium_workouts", Name = "Premium antrenmanlar" } };
    private readonly List<SubscriptionPlanFeature> _planFeatures = new() { new() { SubscriptionProductId = 2, FeatureId = 1 } };

    private SubscriptionService CreateService()
    {
        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(u => u.Repository<Subscription>()).Returns(MockRepositoryFactory.Create(_subscriptions).Object);
        unitOfWork.Setup(u => u.Repository<SubscriptionProduct>()).Returns(MockRepositoryFactory.Create(_products).Object);
        unitOfWork.Setup(u => u.Repository<Feature>()).Returns(MockRepositoryFactory.Create(_features).Object);
        unitOfWork.Setup(u => u.Repository<SubscriptionPlanFeature>()).Returns(MockRepositoryFactory.Create(_planFeatures).Object);
        return new SubscriptionService(unitOfWork.Object);
    }

    [Fact]
    public async Task FreeUser_IsNotPlus_AndSeesActiveProductsOnly()
    {
        var result = await CreateService().GetMySubscriptionAsync(UserId);

        Assert.False(result.IsPlus);
        Assert.Null(result.Current);
        Assert.Equal(new[] { "plus_monthly", "plus_yearly" }, result.Products.Select(p => p.Code));
        Assert.Equal(new[] { "Premium antrenmanlar" }, result.Products[1].Features);
    }

    [Fact]
    public async Task ActiveSubscription_IsPlus_WithProductAndRenewalInfo()
    {
        var expires = DateTime.UtcNow.AddDays(20);
        _subscriptions.Add(new Subscription { UserId = UserId, SubscriptionProductId = 2, Provider = SubscriptionProvider.Apple, Status = SubscriptionStatus.Active, StartedAt = DateTime.UtcNow.AddDays(-10), ExpiresAt = expires, AutoRenew = true });
        _subscriptions.Add(new Subscription { UserId = 99, SubscriptionProductId = 1, Status = SubscriptionStatus.Active, StartedAt = DateTime.UtcNow });

        var result = await CreateService().GetMySubscriptionAsync(UserId);

        Assert.True(result.IsPlus);
        Assert.Equal("Active", result.Current!.Status);
        Assert.Equal("Apple", result.Current.Provider);
        Assert.Equal(expires, result.Current.ExpiresAt);
        Assert.True(result.Current.AutoRenew);
        Assert.Equal("plus_yearly", result.Current.Product!.Code);
    }

    [Fact]
    public async Task ExpiredSubscription_IsShown_ButNotPlus()
    {
        _subscriptions.Add(new Subscription { UserId = UserId, SubscriptionProductId = 3, Provider = SubscriptionProvider.Google, Status = SubscriptionStatus.Active, StartedAt = DateTime.UtcNow.AddMonths(-2), ExpiresAt = DateTime.UtcNow.AddDays(-1) });

        var result = await CreateService().GetMySubscriptionAsync(UserId);

        Assert.False(result.IsPlus);
        Assert.Equal("legacy", result.Current!.Product!.Code);
        Assert.DoesNotContain(result.Products, p => p.Code == "legacy");
    }
}
