using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using MerfitCustomerApi.Api.BackgroundJobs;
using MerfitCustomerApi.Api.Controllers;
using MerfitCustomerApi.Business.Common.Pagination;
using MerfitCustomerApi.Business.Dtos.Customer.Store;
using MerfitCustomerApi.Business.Interfaces.Services;
using MerfitCustomerApi.Business.Services.Store;
using MerfitCustomerApi.Domain.Entities;
using MerfitCustomerApi.Domain.Entities.Enums;
using MerfitCustomerApi.Domain.Exceptions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace MerfitCustomerApi.Business.Tests.Controllers;

/// <summary>
/// MB Shop uclari kimlik ister, kullaniciyi yalnizca JWT'den alir, istemcinin gonderdigi fiyat/kullanici alanlarini
/// kullanmaz ve is kurali hatalarini dogru HTTP koduyla (409 + kod) dondurur.
/// </summary>
public class StoreControllerSecurityTests : IClassFixture<WebApplicationFactory<StoreCartController>>
{
    private readonly WebApplicationFactory<StoreCartController> _factory;
    private readonly Mock<IStoreCartService> _cart = new();
    private readonly Mock<IStoreOrderService> _orders = new();
    private readonly Mock<IStoreCatalogService> _catalog = new();

    public StoreControllerSecurityTests(WebApplicationFactory<StoreCartController> factory)
    {
        _cart.Setup(s => s.AddItemAsync(It.IsAny<long>(), It.IsAny<AddCartItemRequest>(), It.IsAny<CancellationToken>())).ReturnsAsync(new StoreCartDto());
        _orders.Setup(s => s.GetAsync(It.IsAny<long>(), It.IsAny<long>(), It.IsAny<CancellationToken>())).ThrowsAsync(new NotFoundException("Sipariş bulunamadı."));
        _orders.Setup(s => s.CreateAsync(It.IsAny<long>(), It.IsAny<CreateStoreOrderRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ConflictException("Sepet tutarın güncellendi.", StoreOrderService.PriceChangedCode));
        _catalog.Setup(s => s.GetProductsAsync(It.IsAny<StoreProductQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(PagedResult<StoreProductListItemDto>.Create(Array.Empty<StoreProductListItemDto>(), 1, 20, 0));

        _factory = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.Remove(services.Single(d => d.ImplementationType == typeof(PersonalizationBackgroundWorker)));
            services.Remove(services.Single(d => d.ImplementationType == typeof(NotificationBackgroundWorker)));
            services.AddScoped(_ => _cart.Object);
            services.AddScoped(_ => _orders.Object);
            services.AddScoped(_ => _catalog.Object);
        }));
    }

    private HttpClient CreateClient(long? userId)
    {
        var client = _factory.CreateClient();
        if (userId is not null)
        {
            using var scope = _factory.Services.CreateScope();
            var tokenService = scope.ServiceProvider.GetRequiredService<ITokenService>();
            var (token, _) = tokenService.GenerateAccessToken(new ApplicationUser { Id = userId.Value, Email = "u@test.local", Role = UserRole.User });
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
        return client;
    }

    [Theory]
    [InlineData("GET", "/api/store/products")]
    [InlineData("GET", "/api/store/cart")]
    [InlineData("POST", "/api/store/cart/items")]
    [InlineData("GET", "/api/store/orders")]
    [InlineData("POST", "/api/store/orders")]
    [InlineData("GET", "/api/store/addresses")]
    [InlineData("PUT", "/api/store/favorites/1")]
    public async Task AnonymousRequests_AreRejectedWith401(string method, string url)
    {
        var request = new HttpRequestMessage(new HttpMethod(method), url) { Content = method == "GET" ? null : JsonContent.Create(new { }) };
        var response = await CreateClient(null).SendAsync(request);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task AddToCart_UsesJwtUser_AndIgnoresClientPriceAndUserId()
    {
        var response = await CreateClient(42).PostAsync("/api/store/cart/items",
            JsonContent.Create(new { userId = 7, productId = 5, quantity = 2, unitPrice = 0.01, price = 0.01 }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        _cart.Verify(s => s.AddItemAsync(42, It.Is<AddCartItemRequest>(r => r.ProductId == 5 && r.Quantity == 2), It.IsAny<CancellationToken>()), Times.Once);
        _cart.Verify(s => s.AddItemAsync(7, It.IsAny<AddCartItemRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task AddToCart_InvalidQuantity_Returns400()
    {
        var response = await CreateClient(42).PostAsync("/api/store/cart/items", JsonContent.Create(new { productId = 5, quantity = 0 }));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        _cart.Verify(s => s.AddItemAsync(It.IsAny<long>(), It.IsAny<AddCartItemRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task OtherUsersOrder_Returns404()
    {
        var response = await CreateClient(42).GetAsync("/api/store/orders/99");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        _orders.Verify(s => s.GetAsync(42, 99, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateOrder_PriceChanged_Returns409WithCode()
    {
        var response = await CreateClient(42).PostAsync("/api/store/orders",
            JsonContent.Create(new { addressId = 1, paymentMethod = "cash_on_delivery", idempotencyKey = "abcdefgh1234", expectedTotal = 1 }));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<MerfitCustomerApi.Business.Common.Responses.ApiResponse>();
        Assert.Contains(StoreOrderService.PriceChangedCode, body!.Errors);
    }

    [Fact]
    public async Task CreateOrder_InvalidIdempotencyKey_Returns400()
    {
        var response = await CreateClient(42).PostAsync("/api/store/orders",
            JsonContent.Create(new { addressId = 1, paymentMethod = "cash_on_delivery", idempotencyKey = "x" }));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
