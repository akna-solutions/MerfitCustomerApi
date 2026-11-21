using MerfitCustomerApi.Business.Common;
using MerfitCustomerApi.Business.Dtos.Customer.Store;
using MerfitCustomerApi.Business.Services.Store;
using MerfitCustomerApi.Business.Tests.Notifications;
using MerfitCustomerApi.Domain.Entities;
using MerfitCustomerApi.Domain.Entities.Enums;
using MerfitCustomerApi.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace MerfitCustomerApi.Business.Tests.Store;

/// <summary>
/// Yalnizca MERFIT_TEST_POSTGRES tanimliysa calisir. Rozetlerin gercek siparis, yorum, stok ve tarih verisinden
/// turetildigini ve tum urun listelerinde (katalog, detay, favori) dondugunu dogrular.
/// </summary>
public class StoreBadgePostgresTests
{
    // Paylasilan test veritabaninda baska testlerin siparisleri de oldugu icin "cok satan" ilk N siniri genis tutulur.
    private static readonly StoreOptions Options = new()
    {
        FreeShippingThreshold = 500m,
        Badges = new StoreBadgeOptions { BestsellerTopCount = 100000, BestsellerMinQuantity = 3, TopRatedMinReviews = 2 },
    };

    private static async Task<T> Run<T>(Func<UnitOfWork, StoreCatalogService, Task<T>> action)
    {
        await using var db = StoreTestData.CreateContext();
        var uow = new UnitOfWork(db);
        var catalog = new StoreCatalogService(uow, null, new StoreBadgeService(uow, StoreTestData.Options(Options)));
        return await action(uow, catalog);
    }

    private static async Task SetHighlightAsync(long productId, string? code)
    {
        await using var db = StoreTestData.CreateContext();
        await db.Products.Where(p => p.Id == productId).ExecuteUpdateAsync(s => s.SetProperty(p => p.HighlightBadge, code));
    }

    [PostgresFact]
    public async Task Catalog_DerivesBadgesFromRealData()
    {
        var data = new StoreTestData();
        var category = await data.CategoryAsync();
        var plain = await data.ProductAsync(category, 100m, 50, createdAt: DateTime.UtcNow.AddDays(-120));
        var fresh = await data.ProductAsync(category, 100m, 50);
        var lastUnits = await data.ProductAsync(category, 100m, 2, createdAt: DateTime.UtcNow.AddDays(-120));
        var premium = await data.ProductAsync(category, 900m, 50, discountPrice: 600m, createdAt: DateTime.UtcNow.AddDays(-120));
        var picked = await data.ProductAsync(category, 100m, 50, createdAt: DateTime.UtcNow.AddDays(-120));
        await SetHighlightAsync(picked.Id, StoreBadgeCodes.Advantageous);

        var page = await Run((_, c) => c.GetProductsAsync(new StoreProductQuery { Brands = data.Brand, PageSize = 20 }));
        List<string> Codes(long id) => page.Items.Single(i => i.Id == id).Badges.Select(b => b.Code).ToList();

        Assert.Empty(Codes(plain.Id));
        Assert.Equal(new[] { "new" }, Codes(fresh.Id));
        Assert.Equal(new[] { "low_stock" }, Codes(lastUnits.Id));
        Assert.Equal(2, page.Items.Single(i => i.Id == lastUnits.Id).Badges[0].Value);
        Assert.Equal(new[] { "free_shipping" }, Codes(premium.Id));
        Assert.Equal(new[] { "advantageous" }, Codes(picked.Id));
    }

    [PostgresFact]
    public async Task Bestseller_CountsOnlyNonCancelledRecentOrders_TopRatedUsesPublishedReviews()
    {
        var data = new StoreTestData();
        var category = await data.CategoryAsync();
        var sold = await data.ProductAsync(category, 100m, 50, createdAt: DateTime.UtcNow.AddDays(-120));
        var cancelled = await data.ProductAsync(category, 100m, 50, createdAt: DateTime.UtcNow.AddDays(-120));
        var old = await data.ProductAsync(category, 100m, 50, createdAt: DateTime.UtcNow.AddDays(-120));
        var rated = await data.ProductAsync(category, 100m, 50, createdAt: DateTime.UtcNow.AddDays(-120));
        var hiddenRated = await data.ProductAsync(category, 100m, 50, createdAt: DateTime.UtcNow.AddDays(-120));
        var user = await data.UserAsync();

        await using (var db = StoreTestData.CreateContext())
        {
            StoreOrder Order(StoreOrderStatus status, DateTime createdAt, long productId, int qty)
            {
                var order = new StoreOrder
                {
                    UserId = user, OrderNumber = $"T{Guid.NewGuid():N}"[..20], IdempotencyKey = Guid.NewGuid().ToString("N"),
                    Status = status, PaymentStatus = StorePaymentStatus.Pending, PaymentMethod = StorePaymentMethod.CashOnDelivery,
                    Subtotal = 100m * qty, TotalAmount = 100m * qty, Currency = "TRY",
                    ShippingAddressSnapshot = "{}",
                    CreatedAt = createdAt,
                };
                order.Items.Add(new StoreOrderItem
                {
                    ProductId = productId, ProductNameSnapshot = "Urun", SkuSnapshot = "SKU", BrandSnapshot = data.Brand, Quantity = qty,
                    UnitPrice = 100m, UnitListPrice = 100m, LineTotal = 100m * qty, CreatedAt = createdAt,
                });
                return order;
            }

            db.Set<StoreOrder>().AddRange(
                Order(StoreOrderStatus.Delivered, DateTime.UtcNow.AddDays(-3), sold.Id, 3),
                Order(StoreOrderStatus.Cancelled, DateTime.UtcNow.AddDays(-3), cancelled.Id, 9),
                Order(StoreOrderStatus.Delivered, DateTime.UtcNow.AddDays(-60), old.Id, 9));

            var reviewers = new List<long> { await data.UserAsync(), await data.UserAsync() };
            foreach (var reviewer in reviewers)
            {
                db.Set<ProductReview>().Add(new ProductReview { ProductId = rated.Id, UserId = reviewer, Rating = 5, Comment = "Harika ürün, tavsiye ederim.", CreatedAt = DateTime.UtcNow });
                db.Set<ProductReview>().Add(new ProductReview { ProductId = hiddenRated.Id, UserId = reviewer, Rating = 5, Comment = "Harika ürün, tavsiye ederim.", Status = ProductReviewStatus.Hidden, CreatedAt = DateTime.UtcNow });
            }
            await db.SaveChangesAsync();
        }

        var page = await Run((_, c) => c.GetProductsAsync(new StoreProductQuery { Brands = data.Brand, PageSize = 20 }));
        List<string> Codes(long id) => page.Items.Single(i => i.Id == id).Badges.Select(b => b.Code).ToList();

        Assert.Equal(new[] { "bestseller" }, Codes(sold.Id));
        Assert.Empty(Codes(cancelled.Id));
        Assert.Empty(Codes(old.Id));
        Assert.Equal(new[] { "top_rated" }, Codes(rated.Id));
        Assert.Empty(Codes(hiddenRated.Id));

        var detail = await Run((_, c) => c.GetProductAsync(sold.Id));
        Assert.Equal("Çok Satan", Assert.Single(detail.Badges).Label);
    }

    [PostgresFact]
    public async Task Favorites_IncludeBadges()
    {
        var data = new StoreTestData();
        var category = await data.CategoryAsync();
        var product = await data.ProductAsync(category, 100m, 1, createdAt: DateTime.UtcNow.AddDays(-120));
        var user = await data.UserAsync();

        await using var db = StoreTestData.CreateContext();
        var favorites = StoreTestData.Favorites(new UnitOfWork(db));
        await favorites.AddAsync(user, product.Id);
        var item = Assert.Single(await favorites.GetAsync(user));
        Assert.Equal("low_stock", Assert.Single(item.Badges).Code);
    }
}
