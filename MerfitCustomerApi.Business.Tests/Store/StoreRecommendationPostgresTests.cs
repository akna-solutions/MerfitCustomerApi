using MerfitCustomerApi.Business.Dtos.Customer.Store;
using MerfitCustomerApi.Business.Services.Store;
using MerfitCustomerApi.Business.Tests.Notifications;
using MerfitCustomerApi.Domain.Entities;
using MerfitCustomerApi.Domain.Entities.Enums;
using MerfitCustomerApi.Domain.Exceptions;
using MerfitCustomerApi.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace MerfitCustomerApi.Business.Tests.Store;

/// <summary>
/// Yalnizca MERFIT_TEST_POSTGRES tanimliysa calisir. Oneri verisinin gercek siparis/favori/sepet/gizlilik kayitlarindan
/// toplandigini dogrular. Veritabani diger testlerle paylasildigi icin yalnizca bu testin urunleri hakkinda iddiada bulunulur.
/// </summary>
public class StoreRecommendationPostgresTests
{
    private readonly StoreTestData _data = new();

    private static async Task<StoreProductRecommendationsDto> Get(long userId, long productId, int limit = 20)
    {
        await using var db = StoreTestData.CreateContext();
        return await new StoreRecommendationService(new UnitOfWork(db)).GetForProductAsync(userId, productId, limit);
    }

    private async Task<Product> ProductAsync(long category, decimal price, string name, string? brand = null, int stock = 10, bool active = true)
    {
        var p = await _data.ProductAsync(category, price, stock, name: name, isActive: active);
        if (brand is not null)
        {
            await using var db = StoreTestData.CreateContext();
            await db.Products.Where(x => x.Id == p.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.Brand, brand + _data.Tag));
        }
        return p;
    }

    private static async Task OrderAsync(long userId, StoreOrderStatus status, StorePaymentStatus payment, params long[] productIds)
    {
        await using var db = StoreTestData.CreateContext();
        var order = new StoreOrder
        {
            OrderNumber = "RC-" + Guid.NewGuid().ToString("N")[..12], UserId = userId, Status = status, PaymentStatus = payment,
            PaymentMethod = StorePaymentMethod.BankTransfer, Subtotal = 100, TotalAmount = 100, ShippingAddressSnapshot = "{}",
            IdempotencyKey = Guid.NewGuid().ToString("N"), CreatedAt = DateTime.UtcNow,
        };
        foreach (var id in productIds)
        {
            order.Items.Add(new StoreOrderItem
            {
                ProductId = id, ProductNameSnapshot = "Urun", SkuSnapshot = "SKU", BrandSnapshot = "Marka", UnitListPrice = 100, UnitPrice = 100,
                Quantity = 1, LineTotal = 100, CreatedAt = DateTime.UtcNow,
            });
        }
        db.StoreOrders.Add(order);
        await db.SaveChangesAsync();
    }

    private static List<long> Ids(List<StoreProductListItemDto> list) => list.Select(p => p.Id).ToList();

    [PostgresFact]
    public async Task Similar_RanksSameCategoryAndNameFirst_ExcludesSelfInactiveAndUnrelated()
    {
        var user = await _data.UserAsync();
        var protein = await _data.CategoryAsync();
        var bars = await _data.CategoryAsync(parentId: protein);
        var clothes = await _data.CategoryAsync();
        var target = await ProductAsync(protein, 1000, "Whey Protein Çikolata");
        var twin = await ProductAsync(protein, 950, "Whey Protein Vanilya");
        var sameCatFar = await ProductAsync(protein, 3000, "Kreatin Monohidrat", brand: "X");
        var bar = await ProductAsync(bars, 60, "Fıstıklı Bar", brand: "Y");
        var shirt = await ProductAsync(clothes, 300, "Antrenman Tişörtü", brand: "Z");
        var inactive = await ProductAsync(protein, 1000, "Whey Protein Muz", active: false);

        var result = await Get(user, target.Id);
        var similar = Ids(result.Similar);

        Assert.Equal(twin.Id, similar[0]);
        Assert.Contains(sameCatFar.Id, similar);
        Assert.Contains(bar.Id, similar);
        Assert.True(similar.IndexOf(sameCatFar.Id) < similar.IndexOf(bar.Id));
        Assert.DoesNotContain(target.Id, similar);
        Assert.DoesNotContain(inactive.Id, similar);
        Assert.DoesNotContain(shirt.Id, similar);
        Assert.Empty(Ids(result.Recommended).Intersect(similar));
    }

    [PostgresFact]
    public async Task BoughtTogether_FromRealOrders_CancelledAndRefundedIgnored()
    {
        var buyer = await _data.UserAsync();
        var viewer = await _data.UserAsync();
        var supplements = await _data.CategoryAsync();
        var gear = await _data.CategoryAsync();
        var target = await ProductAsync(supplements, 500, "Pre Workout");
        var shaker = await ProductAsync(gear, 150, "Shaker Şişe", brand: "S");
        var belt = await ProductAsync(gear, 400, "Ağırlık Kemeri", brand: "B");

        for (var i = 0; i < 3; i++) await OrderAsync(buyer, StoreOrderStatus.Delivered, StorePaymentStatus.Paid, target.Id, shaker.Id);
        await OrderAsync(buyer, StoreOrderStatus.Cancelled, StorePaymentStatus.Cancelled, target.Id, belt.Id);
        await OrderAsync(buyer, StoreOrderStatus.Delivered, StorePaymentStatus.Refunded, target.Id, belt.Id);

        var result = await Get(viewer, target.Id);

        // Farkli kategoride olsa da birlikte alinan urun one cikar; iptal/iade siparisler sinyal sayilmaz.
        Assert.Contains(shaker.Id, Ids(result.Similar).Concat(Ids(result.Recommended)));
        Assert.DoesNotContain(belt.Id, Ids(result.Similar));
    }

    [PostgresFact]
    public async Task Recommended_UsesFavoritesAndSkipsCart_UnlessPersonalizationDisabled()
    {
        var user = await _data.UserAsync();
        var protein = await _data.CategoryAsync();
        var yoga = await _data.CategoryAsync();
        var target = await ProductAsync(protein, 800, "Whey İzole");
        var favorite = await ProductAsync(yoga, 400, "Yoga Matı", brand: "Y");
        var sameAsFavorite = await ProductAsync(yoga, 350, "Yoga Bloğu", brand: "Y2");
        var inCart = await ProductAsync(yoga, 300, "Yoga Kayışı", brand: "Y3");

        await using (var db = StoreTestData.CreateContext())
        {
            db.FavoriteProducts.Add(new FavoriteProduct { UserId = user, ProductId = favorite.Id, CreatedAt = DateTime.UtcNow });
            var cart = new Cart { UserId = user, CreatedAt = DateTime.UtcNow };
            cart.Items.Add(new CartItem { ProductId = inCart.Id, Quantity = 1, CreatedAt = DateTime.UtcNow });
            db.Carts.Add(cart);
            await db.SaveChangesAsync();
        }

        var result = await Get(user, target.Id);
        Assert.True(result.Personalized);
        var recommended = Ids(result.Recommended);
        Assert.Contains(sameAsFavorite.Id, recommended);
        Assert.DoesNotContain(inCart.Id, recommended);

        await using (var db = StoreTestData.CreateContext())
        {
            db.UserPrivacySettings.Add(new UserPrivacySetting { UserId = user, PersonalizedRecommendations = false, CreatedAt = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }
        var anonymous = await Get(user, target.Id);
        Assert.False(anonymous.Personalized);
        Assert.DoesNotContain(sameAsFavorite.Id, Ids(anonymous.Recommended));
    }

    [PostgresFact]
    public async Task OutOfStockNotRecommended_LimitApplied_UnknownProduct404()
    {
        var user = await _data.UserAsync();
        var category = await _data.CategoryAsync();
        var target = await ProductAsync(category, 100, "Kettlebell 8 kg");
        var empty = await ProductAsync(category, 100, "Kettlebell 12 kg", stock: 0);
        for (var i = 0; i < 6; i++) await ProductAsync(category, 100 + i, $"Kettlebell Set {i}", brand: $"K{i}");

        var result = await Get(user, target.Id, limit: 3);
        Assert.Equal(3, result.Similar.Count);
        Assert.DoesNotContain(empty.Id, Ids(result.Recommended));
        Assert.True(result.Recommended.Count <= 3);

        var inactiveCategory = await _data.CategoryAsync(isActive: false);
        var hidden = await ProductAsync(inactiveCategory, 100, "Gizli Ürün");
        await Assert.ThrowsAsync<NotFoundException>(() => Get(user, hidden.Id));
    }
}
