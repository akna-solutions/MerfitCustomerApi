using MerfitCustomerApi.Business.Dtos.Customer.Store;
using MerfitCustomerApi.Business.Services.Store;
using MerfitCustomerApi.Business.Tests.Notifications;
using MerfitCustomerApi.Domain.Exceptions;
using Xunit;

namespace MerfitCustomerApi.Business.Tests.Store;

/// <summary>
/// Yalnizca MERFIT_TEST_POSTGRES tanimliysa calisir. Gercek servisler ve gercek PostgreSQL ile katalog, sepet,
/// favori ve adres kurallari.
/// </summary>
public class StoreCatalogAndCartPostgresTests
{
    private static async Task<T> Run<T>(Func<Infrastructure.Persistence.UnitOfWork, Task<T>> action)
    {
        await using var db = StoreTestData.CreateContext();
        return await action(new Infrastructure.Persistence.UnitOfWork(db));
    }

    private static Task Run(Func<Infrastructure.Persistence.UnitOfWork, Task> action) => Run<bool>(async uow => { await action(uow); return true; });

    [PostgresFact]
    public async Task ProductList_ReturnsOnlyActiveProductsOfActiveCategories_PagedAndSorted()
    {
        var data = new StoreTestData();
        var category = await data.CategoryAsync();
        var hiddenCategory = await data.CategoryAsync(isActive: false);
        var cheap = await data.ProductAsync(category, 100m, 5);
        var discounted = await data.ProductAsync(category, 300m, 5, discountPrice: 150m);
        var expensive = await data.ProductAsync(category, 900m, 0);
        await data.ProductAsync(category, 50m, 5, isActive: false);
        await data.ProductAsync(hiddenCategory, 60m, 5);

        var page = await Run(uow => StoreTestData.Catalog(uow).GetProductsAsync(new StoreProductQuery { Brands = data.Brand, Sort = "price_asc", PageSize = 2 }));

        Assert.Equal(3, page.TotalCount);
        Assert.Equal(new[] { cheap.Id, discounted.Id }, page.Items.Select(i => i.Id));
        var second = page.Items[1];
        Assert.Equal(150m, second.Price);
        Assert.Equal(300m, second.OriginalPrice);
        Assert.Equal(50, second.DiscountPercentage);

        var page2 = await Run(uow => StoreTestData.Catalog(uow).GetProductsAsync(new StoreProductQuery { Brands = data.Brand, Sort = "price_asc", PageSize = 2, Page = 2 }));
        var last = Assert.Single(page2.Items);
        Assert.Equal(expensive.Id, last.Id);
        Assert.False(last.InStock);
    }

    [PostgresFact]
    public async Task ProductList_FiltersByCategorySearchPriceStockAndDiscount()
    {
        var data = new StoreTestData();
        var category = await data.CategoryAsync();
        var other = await data.CategoryAsync();
        var whey = await data.ProductAsync(category, 1000m, 3, discountPrice: 800m, name: $"Whey Protein {data.Tag}");
        await data.ProductAsync(category, 200m, 0, name: $"Shaker {data.Tag}");
        var band = await data.ProductAsync(other, 400m, 4, name: $"Direnç Bandı {data.Tag}");

        var byCategory = await Run(uow => StoreTestData.Catalog(uow).GetProductsAsync(new StoreProductQuery { CategoryId = other }));
        Assert.Equal(new[] { band.Id }, byCategory.Items.Select(i => i.Id));

        var search = await Run(uow => StoreTestData.Catalog(uow).GetProductsAsync(new StoreProductQuery { Q = $"whey {data.Tag}" }));
        Assert.Equal(new[] { whey.Id }, search.Items.Select(i => i.Id));

        var price = await Run(uow => StoreTestData.Catalog(uow).GetProductsAsync(new StoreProductQuery { Brands = data.Brand, MinPrice = 300, MaxPrice = 800 }));
        Assert.Equal(new[] { whey.Id, band.Id }.OrderBy(x => x), price.Items.Select(i => i.Id).OrderBy(x => x));

        var stock = await Run(uow => StoreTestData.Catalog(uow).GetProductsAsync(new StoreProductQuery { Brands = data.Brand, InStockOnly = true, DiscountedOnly = true }));
        Assert.Equal(new[] { whey.Id }, stock.Items.Select(i => i.Id));
    }

    [PostgresFact]
    public async Task ProductDetail_InactiveOrUnknownProduct_IsNotFound()
    {
        var data = new StoreTestData();
        var category = await data.CategoryAsync();
        var inactive = await data.ProductAsync(category, 100m, 5, isActive: false);
        var active = await data.ProductAsync(category, 100m, 5, imageUrl: "https://cdn.example.com/a.jpg");

        await Assert.ThrowsAsync<NotFoundException>(() => Run(uow => StoreTestData.Catalog(uow).GetProductAsync(inactive.Id)));
        await Assert.ThrowsAsync<NotFoundException>(() => Run(uow => StoreTestData.Catalog(uow).GetProductAsync(long.MaxValue)));
        var detail = await Run(uow => StoreTestData.Catalog(uow).GetProductAsync(active.Id));
        Assert.Equal(new[] { "https://cdn.example.com/a.jpg" }, detail.Images);
    }

    [PostgresFact]
    public async Task Categories_CountOnlyVisibleProducts_AndHideInactiveCategories()
    {
        var data = new StoreTestData();
        var category = await data.CategoryAsync();
        var inactive = await data.CategoryAsync(isActive: false);
        await data.ProductAsync(category, 10m, 1);
        await data.ProductAsync(category, 10m, 1, isActive: false);

        var categories = await Run(uow => StoreTestData.Catalog(uow).GetCategoriesAsync());

        Assert.Equal(1, categories.Single(c => c.Id == category).ProductCount);
        Assert.DoesNotContain(categories, c => c.Id == inactive);
    }

    [PostgresFact]
    public async Task ParentCategory_CountsBrandsAndListIncludeChildCategoryProducts()
    {
        var data = new StoreTestData();
        var parent = await data.CategoryAsync();
        var child = await data.CategoryAsync(parentId: parent);
        var own = await data.ProductAsync(parent, 10m, 1);
        var nested = await data.ProductAsync(child, 20m, 1);

        var categories = await Run(uow => StoreTestData.Catalog(uow).GetCategoriesAsync());
        Assert.Equal(2, categories.Single(c => c.Id == parent).ProductCount);
        Assert.Equal(1, categories.Single(c => c.Id == child).ProductCount);

        var list = await Run(uow => StoreTestData.Catalog(uow).GetProductsAsync(new StoreProductQuery { CategoryId = parent }));
        Assert.Equal(new[] { own.Id, nested.Id }.OrderBy(x => x), list.Items.Select(i => i.Id).OrderBy(x => x));

        var brands = await Run(uow => StoreTestData.Catalog(uow).GetBrandsAsync(parent));
        Assert.Equal(new[] { data.Brand }, brands);
    }

    [PostgresFact]
    public async Task Cart_AddMergesSameProductAndVariant_UpdateAndRemoveWork_AndTotalsComeFromServer()
    {
        var data = new StoreTestData();
        var user = await data.UserAsync();
        var category = await data.CategoryAsync();
        var variants = new List<StoreVariantGroup>
        {
            new() { Id = "size", Label = "Beden", Kind = "size", Options = { new StoreVariantOption { Id = "m", Label = "M" }, new StoreVariantOption { Id = "xl", Label = "XL", PriceDelta = 20 } } },
        };
        var shirt = await data.ProductAsync(category, 200m, 10, discountPrice: 150m, variants: variants);

        await Run(uow => StoreTestData.Cart(uow).AddItemAsync(user, new AddCartItemRequest { ProductId = shirt.Id, Quantity = 1, SelectedVariant = new() { ["size"] = "m" } }));
        await Run(uow => StoreTestData.Cart(uow).AddItemAsync(user, new AddCartItemRequest { ProductId = shirt.Id, Quantity = 2, SelectedVariant = new() { ["size"] = "m" } }));
        var cart = await Run(uow => StoreTestData.Cart(uow).AddItemAsync(user, new AddCartItemRequest { ProductId = shirt.Id, Quantity = 1, SelectedVariant = new() { ["size"] = "xl" } }));

        Assert.Equal(2, cart.Items.Count);
        var m = cart.Items.Single(i => i.VariantLabel == "M");
        var xl = cart.Items.Single(i => i.VariantLabel == "XL");
        Assert.Equal(3, m.Quantity);
        Assert.Equal(170m, xl.UnitPrice);
        Assert.Equal(220m, xl.UnitOriginalPrice);
        // 3 x 150 + 170 = 620 (>= 500, kargo ucretsiz); liste: 3 x 200 + 220 = 820.
        Assert.Equal(820m, cart.Totals.Subtotal);
        Assert.Equal(200m, cart.Totals.Discount);
        Assert.Equal(0m, cart.Totals.ShippingCost);
        Assert.Equal(620m, cart.Totals.Total);
        Assert.True(cart.CanCheckout);

        cart = await Run(uow => StoreTestData.Cart(uow).UpdateItemAsync(user, m.Id, new UpdateCartItemRequest { Quantity = 1 }));
        Assert.Equal(1, cart.Items.Single(i => i.Id == m.Id).Quantity);
        // 150 + 170 = 320 < 500 => kargo 49,90.
        Assert.Equal(49.90m, cart.Totals.ShippingCost);
        Assert.Equal(369.90m, cart.Totals.Total);

        cart = await Run(uow => StoreTestData.Cart(uow).RemoveItemAsync(user, xl.Id));
        Assert.Single(cart.Items);
        cart = await Run(uow => StoreTestData.Cart(uow).UpdateItemAsync(user, m.Id, new UpdateCartItemRequest { Quantity = 0 }));
        Assert.Empty(cart.Items);
        Assert.False(cart.CanCheckout);
    }

    [PostgresFact]
    public async Task Cart_CannotExceedStock_OrMaxPerLine_OrAddOutOfStockOrInactive()
    {
        var data = new StoreTestData();
        var user = await data.UserAsync();
        var category = await data.CategoryAsync();
        var product = await data.ProductAsync(category, 100m, 3);
        var empty = await data.ProductAsync(category, 100m, 0);
        var inactive = await data.ProductAsync(category, 100m, 10, isActive: false);
        var plenty = await data.ProductAsync(category, 10m, 100);

        await Run(uow => StoreTestData.Cart(uow).AddItemAsync(user, new AddCartItemRequest { ProductId = product.Id, Quantity = 2 }));
        var tooMany = await Assert.ThrowsAsync<ConflictException>(() =>
            Run(uow => StoreTestData.Cart(uow).AddItemAsync(user, new AddCartItemRequest { ProductId = product.Id, Quantity = 2 })));
        Assert.Contains("Stokta yeterli ürün yok", tooMany.Message);
        await Assert.ThrowsAsync<ConflictException>(() =>
            Run(uow => StoreTestData.Cart(uow).AddItemAsync(user, new AddCartItemRequest { ProductId = empty.Id, Quantity = 1 })));
        await Assert.ThrowsAsync<NotFoundException>(() =>
            Run(uow => StoreTestData.Cart(uow).AddItemAsync(user, new AddCartItemRequest { ProductId = inactive.Id, Quantity = 1 })));
        await Assert.ThrowsAsync<ConflictException>(() =>
            Run(uow => StoreTestData.Cart(uow).AddItemAsync(user, new AddCartItemRequest { ProductId = plenty.Id, Quantity = 11 })));

        var cart = await Run(uow => StoreTestData.Cart(uow).GetAsync(user));
        var line = Assert.Single(cart.Items);
        Assert.Equal(2, line.Quantity);
        await Assert.ThrowsAsync<ConflictException>(() =>
            Run(uow => StoreTestData.Cart(uow).UpdateItemAsync(user, line.Id, new UpdateCartItemRequest { Quantity = 4 })));
    }

    [PostgresFact]
    public async Task Cart_ReflectsCurrentPrice_AndFlagsLinesThatLostStock()
    {
        var data = new StoreTestData();
        var user = await data.UserAsync();
        var category = await data.CategoryAsync();
        var product = await data.ProductAsync(category, 100m, 5);
        await Run(uow => StoreTestData.Cart(uow).AddItemAsync(user, new AddCartItemRequest { ProductId = product.Id, Quantity = 3 }));

        await StoreTestData.SetPriceAsync(product.Id, 120m, 90m);
        var cart = await Run(uow => StoreTestData.Cart(uow).GetAsync(user));
        Assert.Equal(90m, cart.Items[0].UnitPrice);
        Assert.Equal(270m, cart.Totals.Total - cart.Totals.ShippingCost);

        await StoreTestData.SetStockAsync(product.Id, 2);
        cart = await Run(uow => StoreTestData.Cart(uow).GetAsync(user));
        Assert.False(cart.Items[0].IsAvailable);
        Assert.Equal(2, cart.Items[0].MaxQuantity);
        Assert.False(cart.CanCheckout);
        Assert.Equal(0m, cart.Totals.Total);
    }

    [PostgresFact]
    public async Task Cart_OtherUsersLine_IsNotFound()
    {
        var data = new StoreTestData();
        var owner = await data.UserAsync();
        var intruder = await data.UserAsync();
        var category = await data.CategoryAsync();
        var product = await data.ProductAsync(category, 100m, 5);
        var cart = await Run(uow => StoreTestData.Cart(uow).AddItemAsync(owner, new AddCartItemRequest { ProductId = product.Id, Quantity = 1 }));
        var lineId = cart.Items[0].Id;

        await Assert.ThrowsAsync<NotFoundException>(() => Run(uow => StoreTestData.Cart(uow).UpdateItemAsync(intruder, lineId, new UpdateCartItemRequest { Quantity = 5 })));
        await Assert.ThrowsAsync<NotFoundException>(() => Run(uow => StoreTestData.Cart(uow).RemoveItemAsync(intruder, lineId)));
        Assert.Empty((await Run(uow => StoreTestData.Cart(uow).GetAsync(intruder))).Items);
        Assert.Equal(1, (await Run(uow => StoreTestData.Cart(uow).GetAsync(owner))).Items[0].Quantity);
    }

    [PostgresFact]
    public async Task Favorites_AreUniqueIdempotentAndPerUser()
    {
        var data = new StoreTestData();
        var user = await data.UserAsync();
        var other = await data.UserAsync();
        var category = await data.CategoryAsync();
        var product = await data.ProductAsync(category, 100m, 5);

        await Run(uow => StoreTestData.Favorites(uow).AddAsync(user, product.Id));
        await Run(uow => StoreTestData.Favorites(uow).AddAsync(user, product.Id));
        await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Task.Run(() => Run(uow => StoreTestData.Favorites(uow).AddAsync(user, product.Id)))));

        Assert.Single(await Run(uow => StoreTestData.Favorites(uow).GetAsync(user)));
        Assert.Empty(await Run(uow => StoreTestData.Favorites(uow).GetAsync(other)));

        await Run(uow => StoreTestData.Favorites(uow).RemoveAsync(user, product.Id));
        await Run(uow => StoreTestData.Favorites(uow).RemoveAsync(user, product.Id));
        Assert.Empty(await Run(uow => StoreTestData.Favorites(uow).GetAsync(user)));
    }

    [PostgresFact]
    public async Task Addresses_FirstIsDefault_SingleDefault_AndOwnershipEnforced()
    {
        var data = new StoreTestData();
        var user = await data.UserAsync();
        var other = await data.UserAsync();
        var first = await data.AddressAsync(user);
        var second = await data.AddressAsync(user);

        var list = await Run(uow => StoreTestData.Addresses(uow).GetAsync(user));
        Assert.True(list.Single(a => a.Id == first).IsDefault);
        Assert.Equal("5321234567", list[0].PhoneNumber);

        await Run(uow => StoreTestData.Addresses(uow).SetDefaultAsync(user, second));
        list = await Run(uow => StoreTestData.Addresses(uow).GetAsync(user));
        Assert.Equal(second, Assert.Single(list, a => a.IsDefault).Id);

        await Run(uow => StoreTestData.Addresses(uow).DeleteAsync(user, second));
        list = await Run(uow => StoreTestData.Addresses(uow).GetAsync(user));
        Assert.True(Assert.Single(list).IsDefault);

        await Assert.ThrowsAsync<NotFoundException>(() => Run(uow => StoreTestData.Addresses(uow).DeleteAsync(other, first)));
        await Assert.ThrowsAsync<AppValidationException>(() => Run(uow => StoreTestData.Addresses(uow).CreateAsync(user, new UpsertStoreAddressRequest
        {
            Title = "Ev", RecipientName = "Test Kullanici", PhoneNumber = "0212 000 00 00", City = "İstanbul", District = "Kadıköy", AddressLine = "Uzun bir açık adres",
        })));
    }
}
