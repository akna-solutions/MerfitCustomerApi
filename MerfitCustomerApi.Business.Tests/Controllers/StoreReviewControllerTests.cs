using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using MerfitCustomerApi.Api.BackgroundJobs;
using MerfitCustomerApi.Api.Controllers;
using MerfitCustomerApi.Business.Common.Pagination;
using MerfitCustomerApi.Business.Dtos.Customer.Store;
using MerfitCustomerApi.Business.Interfaces.Services;
using MerfitCustomerApi.Business.Tests.Store;
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
/// Yorum uclari: kimlik zorunlu (fotograf okuma haric), kullanici yalnizca JWT'den alinir, govdedeki userId /
/// isVerifiedPurchase / status yok sayilir ve servis hatalari dogru HTTP koduna donusur.
/// </summary>
public class StoreReviewControllerTests : IClassFixture<WebApplicationFactory<StoreReviewsController>>
{
    private readonly WebApplicationFactory<StoreReviewsController> _factory;
    private readonly Mock<IStoreReviewService> _reviews = new();

    public StoreReviewControllerTests(WebApplicationFactory<StoreReviewsController> factory)
    {
        _factory = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.Remove(services.Single(d => d.ImplementationType == typeof(PersonalizationBackgroundWorker)));
            services.Remove(services.Single(d => d.ImplementationType == typeof(NotificationBackgroundWorker)));
            services.AddScoped(_ => _reviews.Object);
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
    [InlineData("GET", "/api/store/products/1/reviews")]
    [InlineData("GET", "/api/store/products/1/reviews/summary")]
    [InlineData("GET", "/api/store/products/1/reviews/mine")]
    [InlineData("POST", "/api/store/products/1/reviews")]
    [InlineData("PUT", "/api/store/reviews/1")]
    [InlineData("DELETE", "/api/store/reviews/1")]
    [InlineData("POST", "/api/store/reviews/1/images")]
    [InlineData("DELETE", "/api/store/reviews/1/images/2")]
    [InlineData("PUT", "/api/store/reviews/1/vote")]
    [InlineData("DELETE", "/api/store/reviews/1/vote")]
    [InlineData("POST", "/api/store/reviews/1/reports")]
    public async Task AnonymousRequests_AreRejectedWith401(string method, string url)
    {
        HttpContent? content = method is "GET" or "DELETE" ? null
            : url.EndsWith("/images") ? new MultipartFormDataContent { { new ByteArrayContent(ReviewTestImages.Jpeg()), "photo", "a.jpg" } }
            : JsonContent.Create(new { });
        var request = new HttpRequestMessage(new HttpMethod(method), url) { Content = content };
        var response = await CreateClient(null).SendAsync(request);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        _reviews.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Create_UsesJwtUser_AndIgnoresClientControlledFields()
    {
        UpsertStoreReviewRequest? captured = null;
        _reviews.Setup(s => s.CreateAsync(42, 7, It.IsAny<UpsertStoreReviewRequest>(), It.IsAny<CancellationToken>()))
            .Callback<long, long, UpsertStoreReviewRequest, CancellationToken>((_, _, r, _) => captured = r)
            .ReturnsAsync(new StoreReviewDto { Id = 1, Rating = 5 });

        var response = await CreateClient(42).PostAsJsonAsync("/api/store/products/7/reviews", new
        {
            rating = 5, comment = "Gayet güzel bir ürün", userId = 999, isVerifiedPurchase = true, status = "Published", helpfulCount = 50,
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(captured);
        Assert.Equal(5, captured!.Rating);
        _reviews.Verify(s => s.CreateAsync(999, It.IsAny<long>(), It.IsAny<UpsertStoreReviewRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ServiceErrors_MapTo403_409_400()
    {
        _reviews.Setup(s => s.UpdateAsync(5, 1, It.IsAny<UpsertStoreReviewRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ForbiddenException("Yalnızca kendi yorumunu düzenleyebilirsin."));
        _reviews.Setup(s => s.CreateAsync(5, 1, It.IsAny<UpsertStoreReviewRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ConflictException("Bu ürüne zaten bir yorumun var.", "REVIEW_EXISTS"));
        _reviews.Setup(s => s.VoteAsync(5, 1, It.IsAny<VoteStoreReviewRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AppValidationException("IsHelpful", "Oy seçilmedi."));
        var client = CreateClient(5);

        Assert.Equal(HttpStatusCode.Forbidden, (await client.PutAsJsonAsync("/api/store/reviews/1", new { rating = 1, comment = "xxxxxxxxxxxx" })).StatusCode);
        var conflict = await client.PostAsJsonAsync("/api/store/products/1/reviews", new { rating = 1, comment = "xxxxxxxxxxxx" });
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        Assert.Contains("REVIEW_EXISTS", await conflict.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync("/api/store/reviews/1/vote", new { })).StatusCode);
    }

    [Fact]
    public async Task Report_Returns201ForNew_200ForRepeat()
    {
        _reviews.SetupSequence(s => s.ReportAsync(3, 9, It.IsAny<ReportStoreReviewRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ReportStoreReviewResponse { AlreadyReported = false })
            .ReturnsAsync(new ReportStoreReviewResponse { AlreadyReported = true });
        var client = CreateClient(3);
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/store/reviews/9/reports", new { reason = "spam" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/store/reviews/9/reports", new { reason = "spam" })).StatusCode);
    }

    [Fact]
    public async Task ImageUpload_PassesBytesForJwtUser_AndRejectsOversizedBodies()
    {
        StoreReviewImageUpload? upload = null;
        _reviews.Setup(s => s.AddImageAsync(8, 4, It.IsAny<StoreReviewImageUpload>(), It.IsAny<CancellationToken>()))
            .Callback<long, long, StoreReviewImageUpload, CancellationToken>((_, _, u, _) => upload = u)
            .ReturnsAsync(new StoreReviewImageDto { Id = 1, Url = "/api/store/review-images/x" });
        var client = CreateClient(8);
        var jpeg = ReviewTestImages.Jpeg();

        using var form = new MultipartFormDataContent();
        var part = new ByteArrayContent(jpeg);
        part.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        form.Add(part, "photo", "../../evil name.jpg");
        var response = await client.PostAsync("/api/store/reviews/4/images", form);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(jpeg, upload!.Content);
        Assert.Equal("image/jpeg", upload.DeclaredContentType);

        using var big = new MultipartFormDataContent { { new ByteArrayContent(new byte[4 * 1024 * 1024]), "photo", "big.jpg" } };
        var tooLarge = await client.PostAsync("/api/store/reviews/4/images", big);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, tooLarge.StatusCode);

        using var empty = new MultipartFormDataContent { { new StringContent("x"), "note" } };
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/api/store/reviews/4/images", empty)).StatusCode);
    }

    [Fact]
    public async Task ReviewImage_IsServedWithoutAuth_WithSafeHeaders_And404WhenUnavailable()
    {
        const string key = "0123456789abcdef0123456789abcdef";
        var bytes = ReviewTestImages.Png();
        _reviews.Setup(s => s.GetImageAsync(key, It.IsAny<CancellationToken>())).ReturnsAsync(new StoreReviewImageFile(bytes, "image/png"));
        var client = CreateClient(null);

        var response = await client.GetAsync($"/api/store/review-images/{key}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("image/png", response.Content.Headers.ContentType!.MediaType);
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal(bytes, await response.Content.ReadAsByteArrayAsync());

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/store/review-images/ffffffffffffffffffffffffffffffff")).StatusCode);
    }

    [Fact]
    public async Task Mine_Returns204WhenUserHasNoReview()
    {
        _reviews.Setup(s => s.GetMineAsync(6, 2, It.IsAny<CancellationToken>())).ReturnsAsync((StoreReviewDto?)null);
        var response = await CreateClient(6).GetAsync("/api/store/products/2/reviews/mine");
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task List_PassesQueryToService()
    {
        StoreReviewQuery? query = null;
        _reviews.Setup(s => s.GetListAsync(6, 2, It.IsAny<StoreReviewQuery>(), It.IsAny<CancellationToken>()))
            .Callback<long, long, StoreReviewQuery, CancellationToken>((_, _, q, _) => query = q)
            .ReturnsAsync(PagedResult<StoreReviewDto>.Create(Array.Empty<StoreReviewDto>(), 2, 10, 0));
        var response = await CreateClient(6).GetAsync("/api/store/products/2/reviews?page=2&pageSize=10&sort=helpful&rating=4&withPhotos=true&verifiedOnly=true");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal((2, 10, "helpful", (int?)4, true, true), (query!.Page, query.PageSize, query.Sort, query.Rating, query.WithPhotos, query.VerifiedOnly));
    }
}
