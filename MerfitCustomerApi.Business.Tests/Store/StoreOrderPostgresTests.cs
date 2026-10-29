using System.Text.Json;
using MerfitCustomerApi.Business.Common;
using MerfitCustomerApi.Business.Common.Pagination;
using MerfitCustomerApi.Business.Dtos.Customer.Store;
using MerfitCustomerApi.Business.Interfaces.Services;
using MerfitCustomerApi.Business.Services.Store;
using MerfitCustomerApi.Business.Tests.Notifications;
using MerfitCustomerApi.Domain.Entities.Enums;
using MerfitCustomerApi.Domain.Exceptions;
using MerfitCustomerApi.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace MerfitCustomerApi.Business.Tests.Store;

/// <summary>
/// Yalnizca MERFIT_TEST_POSTGRES tanimliysa calisir. Siparis olusturma, stok tutarliligi (eszamanli siparisler dahil),
/// idempotency, sahiplik, iptal ve odeme sonucu kurallari gercek PostgreSQL uzerinde dogrulanir.
/// </summary>
public class StoreOrderPostgresTests
{
    private static async Task<T> Run<T>(Func<UnitOfWork, Task<T>> action)
    {
        await using var db = StoreTestData.CreateContext();
        return await action(new UnitOfWork(db));
    }

    private static async Task Run(Func<UnitOfWork, Task> action)
    {
        await using var db = StoreTestData.CreateContext();
        await action(new UnitOfWork(db));
    }

    private static async Task AddToCart(long user, long productId, int quantity, Dictionary<string, string>? variant = null) =>
        await Run(uow => StoreTestData.Cart(uow).AddItemAsync(user, new AddCartItemRequest { ProductId = productId, Quantity = quantity, SelectedVariant = variant }));

    private static CreateStoreOrderRequest Request(long addressId, decimal? expectedTotal = null, string? key = null) => new()
    {
        AddressId = addressId,
        PaymentMethod = "cash_on_delivery",
        IdempotencyKey = key ?? Guid.NewGuid().ToString("N"),
        ExpectedTotal = expectedTotal,
    };

    private static Task<(StoreOrderDto Order, bool Created)> PlaceOrder(long user, CreateStoreOrderRequest request) =>
        Run(uow => StoreTestData.Orders(uow).CreateAsync(user, request));

    [PostgresFact]
    public async Task CreateOrder_ComputesTotalsOnServer_SnapshotsItems_DecrementsStock_AndClearsCart()
    {
        var data = new StoreTestData();
        var user = await data.UserAsync();
        var category = await data.CategoryAsync();
        var variants = new List<StoreVariantGroup>
        {
            new() { Id = "package", Label = "Paket", Kind = "package", Options = { new StoreVariantOption { Id = "1kg", Label = "1 kg", PriceDelta = -100 }, new StoreVariantOption { Id = "2kg", Label = "2 kg" } } },
        };
        var whey = await data.ProductAsync(category, 400m, 10, discountPrice: 300m, variants: variants, name: "Whey 2 kg");
        var shaker = await data.ProductAsync(category, 49.95m, 5, name: "Shaker");
        var address = await data.AddressAsync(user);
        await AddToCart(user, whey.Id, 1, new() { ["package"] = "1kg" });
        await AddToCart(user, shaker.Id, 2);

        // Satir: 1 x (300-100)=200 (liste 300) + 2 x 49,95 = 99,90 => indirimli 299,90 < 500 => kargo 49,90.
        var (order, created) = await PlaceOrder(user, Request(address, expectedTotal: 349.80m));

        Assert.True(created);
        Assert.Equal(399.90m, order.Subtotal);
        Assert.Equal(100m, order.Discount);
        Assert.Equal(49.90m, order.ShippingCost);
        Assert.Equal(349.80m, order.Total);
        Assert.Equal("received", order.Status);
        Assert.Equal("pending", order.PaymentStatus);
        Assert.Matches("^MBF-\\d{6}-[A-Z2-9]{6}$", order.OrderNumber);
        var line = order.Items.Single(i => i.ProductId == whey.Id);
        Assert.Equal("1 kg", line.VariantLabel);
        Assert.Equal(200m, line.UnitPrice);
        Assert.Equal(300m, line.UnitOriginalPrice);
        Assert.Equal(9, await StoreTestData.StockAsync(whey.Id));
        Assert.Equal(3, await StoreTestData.StockAsync(shaker.Id));
        Assert.Empty((await Run(uow => StoreTestData.Cart(uow).GetAsync(user))).Items);
        Assert.Single(order.History);
    }

    [PostgresFact]
    public async Task CreateOrder_ClientExpectedTotalMismatch_IsRejected_AndNothingChanges()
    {
        var data = new StoreTestData();
        var user = await data.UserAsync();
        var category = await data.CategoryAsync();
        var product = await data.ProductAsync(category, 600m, 5);
        var address = await data.AddressAsync(user);
        await AddToCart(user, product.Id, 1);

        // Istemci 1 TL gosterdigini iddia ediyor; tutar istemciden alinmaz, siparis reddedilir.
        var ex = await Assert.ThrowsAsync<ConflictException>(() => PlaceOrder(user, Request(address, expectedTotal: 1m)));
        Assert.Equal(StoreOrderService.PriceChangedCode, ex.Code);
        Assert.Equal(5, await StoreTestData.StockAsync(product.Id));
        Assert.Single((await Run(uow => StoreTestData.Cart(uow).GetAsync(user))).Items);

        // Fiyat sunucuda degisirse yeni fiyat uygulanir.
        await StoreTestData.SetPriceAsync(product.Id, 700m, null);
        var (order, _) = await PlaceOrder(user, Request(address));
        Assert.Equal(700m, order.Total);
    }

    [PostgresFact]
    public async Task CreateOrder_SameIdempotencyKey_ReturnsFirstOrder_EvenWhenConcurrent()
    {
        var data = new StoreTestData();
        var user = await data.UserAsync();
        var category = await data.CategoryAsync();
        var product = await data.ProductAsync(category, 100m, 10);
        var address = await data.AddressAsync(user);
        await AddToCart(user, product.Id, 2);
        var key = Guid.NewGuid().ToString("N");

        var results = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => Task.Run(async () =>
        {
            try
            {
                return (await PlaceOrder(user, Request(address, key: key))).Order.Id;
            }
            catch (AppValidationException)
            {
                // Ilk siparis sepeti bosalttiktan sonra baslayan istek sepeti bos gorebilir; bu durumda anahtarla
                // tekrar sorgulandiginda ayni siparis doner (asagida).
                return 0L;
            }
        })));
        var (again, created) = await PlaceOrder(user, Request(address, key: key));

        Assert.False(created);
        Assert.All(results.Where(id => id != 0), id => Assert.Equal(again.Id, id));
        await using var db = StoreTestData.CreateContext();
        Assert.Equal(1, await db.StoreOrders.CountAsync(o => o.UserId == user));
        Assert.Equal(8, await StoreTestData.StockAsync(product.Id));
    }

    [PostgresFact]
    public async Task ConcurrentOrders_NeverOversellStock()
    {
        var data = new StoreTestData();
        var category = await data.CategoryAsync();
        var product = await data.ProductAsync(category, 100m, 5);
        var buyers = new List<(long User, long Address)>();
        for (var i = 0; i < 8; i++)
        {
            var user = await data.UserAsync();
            buyers.Add((user, await data.AddressAsync(user)));
            await AddToCart(user, product.Id, 2);
        }

        var outcomes = await Task.WhenAll(buyers.Select(b => Task.Run(async () =>
        {
            try
            {
                await PlaceOrder(b.User, Request(b.Address));
                return true;
            }
            catch (ConflictException)
            {
                return false;
            }
        })));

        // 5 stok, her siparis 2 adet: en fazla 2 siparis olusabilir, stok asla negatif olmaz.
        Assert.Equal(2, outcomes.Count(o => o));
        Assert.Equal(1, await StoreTestData.StockAsync(product.Id));
        await using var db = StoreTestData.CreateContext();
        var userIds = buyers.Select(b => b.User).ToList();
        Assert.Equal(4, await db.StoreOrderItems.Where(i => i.ProductId == product.Id).SumAsync(i => i.Quantity));
        Assert.Equal(2, await db.StoreOrders.CountAsync(o => userIds.Contains(o.UserId)));
    }

    [PostgresFact]
    public async Task FailedOrder_RollsBackStockAndLeavesNoOrder()
    {
        var data = new StoreTestData();
        var user = await data.UserAsync();
        var category = await data.CategoryAsync();
        var marker = $"FAIL-{data.Tag}";
        var good = await data.ProductAsync(category, 100m, 10);
        var failing = await data.ProductAsync(category, 100m, 10, name: marker);
        var address = await data.AddressAsync(user);
        await AddToCart(user, good.Id, 3);
        await AddToCart(user, failing.Id, 1);

        // Siparis kalemi yazilirken hata: stok dusuldukten SONRA, transaction icinde. Hepsi geri alinmali.
        await using (var db = StoreTestData.CreateContext())
        {
            await db.Database.ExecuteSqlRawAsync($"""
                CREATE OR REPLACE FUNCTION store.test_fail_{data.Tag}() RETURNS trigger AS $$
                BEGIN
                  IF NEW."ProductNameSnapshot" = '{marker}' THEN RAISE EXCEPTION 'test failure'; END IF;
                  RETURN NEW;
                END $$ LANGUAGE plpgsql;
                CREATE TRIGGER test_fail_{data.Tag} BEFORE INSERT ON store."OrderItem"
                  FOR EACH ROW EXECUTE FUNCTION store.test_fail_{data.Tag}();
                """);
        }

        try
        {
            await Assert.ThrowsAnyAsync<Exception>(() => PlaceOrder(user, Request(address)));
        }
        finally
        {
            await using var db = StoreTestData.CreateContext();
            await db.Database.ExecuteSqlRawAsync($"DROP TRIGGER IF EXISTS test_fail_{data.Tag} ON store.\"OrderItem\"; DROP FUNCTION IF EXISTS store.test_fail_{data.Tag}();");
        }

        Assert.Equal(10, await StoreTestData.StockAsync(good.Id));
        Assert.Equal(10, await StoreTestData.StockAsync(failing.Id));
        await using (var db = StoreTestData.CreateContext())
        {
            Assert.Equal(0, await db.StoreOrders.CountAsync(o => o.UserId == user));
        }

        Assert.Equal(2, (await Run(uow => StoreTestData.Cart(uow).GetAsync(user))).Items.Count);
    }

    [PostgresFact]
    public async Task Orders_AreVisibleOnlyToTheirOwner()
    {
        var data = new StoreTestData();
        var owner = await data.UserAsync();
        var intruder = await data.UserAsync();
        var category = await data.CategoryAsync();
        var product = await data.ProductAsync(category, 100m, 10);
        var address = await data.AddressAsync(owner);
        await AddToCart(owner, product.Id, 1);
        var (order, _) = await PlaceOrder(owner, Request(address));

        await Assert.ThrowsAsync<NotFoundException>(() => Run(uow => StoreTestData.Orders(uow).GetAsync(intruder, order.Id)));
        await Assert.ThrowsAsync<NotFoundException>(() => Run(uow => StoreTestData.Orders(uow).CancelAsync(intruder, order.Id, new CancelStoreOrderRequest())));
        Assert.Equal(0, (await Run(uow => StoreTestData.Orders(uow).GetListAsync(intruder, new PagedRequest()))).TotalCount);
        var mine = await Run(uow => StoreTestData.Orders(uow).GetListAsync(owner, new PagedRequest()));
        Assert.Equal(order.Id, Assert.Single(mine.Items).Id);

        // Baskasinin adresiyle siparis verilemez.
        await AddToCart(intruder, product.Id, 1);
        await Assert.ThrowsAsync<NotFoundException>(() => PlaceOrder(intruder, Request(address)));
    }

    [PostgresFact]
    public async Task Cancel_RestoresStockOnce_AndOnlyWhileReceived()
    {
        var data = new StoreTestData();
        var user = await data.UserAsync();
        var category = await data.CategoryAsync();
        var product = await data.ProductAsync(category, 100m, 4);
        var address = await data.AddressAsync(user);
        await AddToCart(user, product.Id, 3);
        var (order, _) = await PlaceOrder(user, Request(address));
        Assert.Equal(1, await StoreTestData.StockAsync(product.Id));

        var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Task.Run(async () =>
        {
            try
            {
                await Run(uow => StoreTestData.Orders(uow).CancelAsync(user, order.Id, new CancelStoreOrderRequest { Reason = "Vazgeçtim" }));
                return true;
            }
            catch (ConflictException)
            {
                return false;
            }
        })));

        Assert.Equal(1, results.Count(r => r));
        Assert.Equal(4, await StoreTestData.StockAsync(product.Id));
        var cancelled = await Run(uow => StoreTestData.Orders(uow).GetAsync(user, order.Id));
        Assert.Equal("cancelled", cancelled.Status);
        Assert.Equal("cancelled", cancelled.PaymentStatus);
        Assert.False(cancelled.CanCancel);
    }

    [PostgresFact]
    public async Task OrderAddress_IsImmutableSnapshot()
    {
        var data = new StoreTestData();
        var user = await data.UserAsync();
        var category = await data.CategoryAsync();
        var product = await data.ProductAsync(category, 100m, 4);
        var address = await data.AddressAsync(user);
        await AddToCart(user, product.Id, 1);
        var (order, _) = await PlaceOrder(user, Request(address));

        await Run(uow => StoreTestData.Addresses(uow).UpdateAsync(user, address, new UpsertStoreAddressRequest
        {
            Title = "Yeni", RecipientName = "Baska Kisi", PhoneNumber = "5559998877", City = "Ankara", District = "Çankaya", AddressLine = "Tamamen farklı bir adres",
        }));
        await Run(uow => StoreTestData.Addresses(uow).DeleteAsync(user, address));
        await StoreTestData.SetPriceAsync(product.Id, 999m, null);

        var again = await Run(uow => StoreTestData.Orders(uow).GetAsync(user, order.Id));
        Assert.Equal("İstanbul", again.ShippingAddress.City);
        Assert.Equal("Test Kullanici", again.ShippingAddress.RecipientName);
        Assert.Equal(100m, again.Items[0].UnitPrice);
    }

    [PostgresFact]
    public async Task UnavailablePaymentMethod_AndEmptyCart_AreRejected()
    {
        var data = new StoreTestData();
        var user = await data.UserAsync();
        var address = await data.AddressAsync(user);

        await Assert.ThrowsAsync<AppValidationException>(() => PlaceOrder(user, Request(address)));
        var card = Request(address);
        card.PaymentMethod = "card";
        await Assert.ThrowsAsync<AppValidationException>(() => PlaceOrder(user, card));
        var transfer = Request(address);
        transfer.PaymentMethod = "bank_transfer";
        await Assert.ThrowsAsync<AppValidationException>(() => PlaceOrder(user, transfer));

        var options = await Run(uow => StoreTestData.Orders(uow).GetCheckoutOptionsAsync());
        Assert.True(options.PaymentMethods.Single(m => m.Method == "cash_on_delivery").Available);
        Assert.False(options.PaymentMethods.Single(m => m.Method == "card").Available);
        Assert.False(options.PaymentMethods.Single(m => m.Method == "bank_transfer").Available);
    }

    [PostgresFact]
    public async Task BankTransfer_WhenConfigured_ShowsAccountAndReference()
    {
        var data = new StoreTestData();
        var user = await data.UserAsync();
        var category = await data.CategoryAsync();
        var product = await data.ProductAsync(category, 100m, 4);
        var address = await data.AddressAsync(user);
        await AddToCart(user, product.Id, 1);
        var options = new StoreOptions { BankTransfer = new BankTransferOptions { Iban = "TR00 0000 0000 0000 0000 0000 00", AccountName = "MBFit Test" } };
        var request = Request(address);
        request.PaymentMethod = "bank_transfer";

        var (order, _) = await Run(uow => StoreTestData.Orders(uow, options).CreateAsync(user, request));

        Assert.Equal("pending", order.PaymentStatus);
        Assert.NotNull(order.BankTransfer);
        Assert.Equal(order.OrderNumber, order.BankTransfer!.Reference);
    }

    [PostgresFact]
    public async Task PaymentResult_FailureCancelsAndRestocks_SuccessMarksPaid_DuplicatesIgnored()
    {
        var data = new StoreTestData();
        var user = await data.UserAsync();
        var category = await data.CategoryAsync();
        var product = await data.ProductAsync(category, 100m, 5);
        var address = await data.AddressAsync(user);
        await AddToCart(user, product.Id, 2);
        var (failedOrder, _) = await PlaceOrder(user, Request(address));
        await AddToCart(user, product.Id, 1);
        var (paidOrder, _) = await PlaceOrder(user, Request(address));
        Assert.Equal(2, await StoreTestData.StockAsync(product.Id));

        Task Apply(StorePaymentResult result) =>
            Run(uow => new StorePaymentResultService(uow, NullLogger<StorePaymentResultService>.Instance).ApplyAsync(result));

        await Apply(new StorePaymentResult(failedOrder.OrderNumber, false, "test", "ref-1", "card declined"));
        await Apply(new StorePaymentResult(failedOrder.OrderNumber, false, "test", "ref-1", "card declined"));
        await Apply(new StorePaymentResult(paidOrder.OrderNumber, true, "test", "ref-2"));
        await Apply(new StorePaymentResult(paidOrder.OrderNumber, false, "test", "ref-2", "late failure"));

        var failed = await Run(uow => StoreTestData.Orders(uow).GetAsync(user, failedOrder.Id));
        var paid = await Run(uow => StoreTestData.Orders(uow).GetAsync(user, paidOrder.Id));
        Assert.Equal("cancelled", failed.Status);
        Assert.Equal("failed", failed.PaymentStatus);
        Assert.Equal("received", paid.Status);
        Assert.Equal("paid", paid.PaymentStatus);
        Assert.Equal(4, await StoreTestData.StockAsync(product.Id));
    }
}
