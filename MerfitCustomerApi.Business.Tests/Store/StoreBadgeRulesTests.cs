using MerfitCustomerApi.Business.Common;
using MerfitCustomerApi.Business.Services.Store;
using Xunit;

namespace MerfitCustomerApi.Business.Tests.Store;

public class StoreBadgeRulesTests
{
    private static readonly DateTime Now = new(2026, 11, 21, 12, 0, 0, DateTimeKind.Utc);
    private static readonly StoreOptions Options = new() { FreeShippingThreshold = 500m };

    private static StoreBadgeFacts Facts(string? highlight = null, DateTime? created = null, int stock = 50, decimal price = 100m,
        bool bestseller = false, int reviews = 0, decimal average = 0m) =>
        new(highlight, created ?? Now.AddDays(-90), stock, price, bestseller, reviews, average);

    private static List<string> Codes(StoreBadgeFacts facts) => StoreBadgeRules.Build(facts, Options, Now).Select(b => b.Code).ToList();

    [Fact]
    public void PlainProduct_HasNoBadges()
    {
        Assert.Empty(Codes(Facts()));
    }

    [Fact]
    public void AllConditions_AreOrderedByImportance()
    {
        var codes = Codes(Facts(highlight: "advantageous", created: Now.AddDays(-2), stock: 3, price: 750m, bestseller: true, reviews: 8, average: 4.7m));
        Assert.Equal(new[] { "advantageous", "low_stock", "bestseller", "top_rated", "new", "free_shipping" }, codes);
    }

    [Fact]
    public void LowStock_CarriesRemainingCount_AndIsHiddenWhenSoldOutOrPlenty()
    {
        var badge = Assert.Single(StoreBadgeRules.Build(Facts(stock: 2), Options, Now));
        Assert.Equal("low_stock", badge.Code);
        Assert.Equal("Son 2 Ürün", badge.Label);
        Assert.Equal(2, badge.Value);

        Assert.Empty(Codes(Facts(stock: 0)));
        Assert.Empty(Codes(Facts(stock: 6)));
    }

    [Theory]
    [InlineData(29, true)]
    [InlineData(31, false)]
    [InlineData(-1, false)] // ileri tarihli kayit "yeni" sayilmaz
    public void New_UsesConfiguredWindow(int ageDays, bool expected)
    {
        Assert.Equal(expected, Codes(Facts(created: Now.AddDays(-ageDays))).Contains("new"));
    }

    [Theory]
    [InlineData(5, 4.5, true)]
    [InlineData(4, 5.0, false)] // az yorum
    [InlineData(10, 4.4, false)] // dusuk ortalama
    public void TopRated_RequiresEnoughReviewsAndAverage(int reviews, double average, bool expected)
    {
        Assert.Equal(expected, Codes(Facts(reviews: reviews, average: (decimal)average)).Contains("top_rated"));
    }

    [Theory]
    [InlineData(499.99, false)]
    [InlineData(500, true)]
    public void FreeShipping_FollowsCheckoutThreshold(double price, bool expected)
    {
        Assert.Equal(expected, Codes(Facts(price: (decimal)price)).Contains("free_shipping"));
    }

    [Fact]
    public void UnknownHighlight_IsIgnored_MbfitPickHasLabel()
    {
        Assert.Empty(Codes(Facts(highlight: "whatever")));
        var badge = Assert.Single(StoreBadgeRules.Build(Facts(highlight: "mbfit_pick"), Options, Now));
        Assert.Equal("MBFit Seçimi", badge.Label);
    }
}
