using MerfitCustomerApi.Business.Services.Campaigns;
using MerfitCustomerApi.Business.Tests.AccountEmail;
using MerfitCustomerApi.Business.Tests.TestHelpers;
using MerfitCustomerApi.Domain.Entities;
using MerfitCustomerApi.Domain.Exceptions;
using Xunit;

namespace MerfitCustomerApi.Business.Tests.Campaigns;

/// <summary>Mobil kampanya listesi: yalnizca yayindaki kampanyalar, siralama ve guvenli baglantilar.</summary>
public class CampaignServiceTests
{
    private static readonly DateTime Now = new(2026, 10, 20, 9, 0, 0, DateTimeKind.Utc);

    private readonly InMemoryUnitOfWork _db = new();
    private readonly MutableTimeProvider _clock = new() { Now = new DateTimeOffset(Now) };

    private CampaignService CreateService() => new(_db.Object, _clock);

    private AppContent Add(long id, string type = "Campaign", bool active = true, DateTime? start = null, DateTime? end = null,
        int sort = 0, string? title = null, string? link = null, string? image = null)
    {
        var entity = new AppContent
        {
            Id = id, Key = $"c-{id}", Type = type, IsActive = active, StartAt = start, EndAt = end, SortOrder = sort,
            Title = title ?? $"Kampanya {id}", Description = "Kısa", Body = "Uzun açıklama", LinkUrl = link, ImageUrl = image,
            CreatedAt = Now.AddDays(-id),
        };
        _db.Set<AppContent>().Add(entity);
        return entity;
    }

    [Fact]
    public async Task List_ReturnsOnlyLiveCampaigns_InSortOrder()
    {
        Add(1, sort: 2);
        Add(2, sort: 1);
        Add(3, active: false);
        Add(4, end: Now.AddMinutes(-1));                 // suresi doldu
        Add(5, start: Now.AddDays(1));                   // henuz baslamadi
        Add(6, type: "Banner");                          // baska icerik turu
        Add(7, title: "");                               // basliksiz kayit gosterilmez
        Add(8, start: Now.AddDays(-1), end: Now.AddDays(1), sort: 1);

        var result = await CreateService().GetActiveAsync();

        Assert.Equal(new long[] { 8, 2, 1 }, result.Select(c => c.Id).ToArray());
        Assert.Equal("Kısa", result[0].Summary);
    }

    [Fact]
    public async Task Detail_ForExpiredOrPassiveCampaign_IsNotFound()
    {
        Add(1, end: Now.AddDays(-1));
        Add(2, active: false);
        Add(3, type: "Announcement");
        var service = CreateService();

        await Assert.ThrowsAsync<NotFoundException>(() => service.GetByIdAsync(1));
        await Assert.ThrowsAsync<NotFoundException>(() => service.GetByIdAsync(2));
        await Assert.ThrowsAsync<NotFoundException>(() => service.GetByIdAsync(3));
        await Assert.ThrowsAsync<NotFoundException>(() => service.GetByIdAsync(404));
    }

    [Fact]
    public async Task Detail_ReturnsBody_AndDropsUnsafeLinks()
    {
        Add(1, link: "javascript:alert(1)", image: "ftp://x/y.png");
        Add(2, link: " https://mbfit.online/kampanya ", image: "https://cdn.mbfit.online/a.jpg");
        var service = CreateService();

        var unsafeOne = await service.GetByIdAsync(1);
        var safeOne = await service.GetByIdAsync(2);

        Assert.Equal("Uzun açıklama", unsafeOne.Body);
        Assert.Null(unsafeOne.LinkUrl);
        Assert.Null(unsafeOne.ImageUrl);
        Assert.Equal("https://mbfit.online/kampanya", safeOne.LinkUrl);
        Assert.Equal("https://cdn.mbfit.online/a.jpg", safeOne.ImageUrl);
    }
}
