using MerfitCustomerApi.Business.Common;
using MerfitCustomerApi.Business.Services.Store;
using MerfitCustomerApi.Domain.Entities;
using Xunit;

namespace MerfitCustomerApi.Business.Tests.Store;

/// <summary>Fiyat ve varyant kurallari (veritabani gerektirmez).</summary>
public class StorePricingTests
{
    private static readonly List<StoreVariantGroup> Groups = new()
    {
        new StoreVariantGroup
        {
            Id = "package", Label = "Paket", Kind = "package",
            Options = { new StoreVariantOption { Id = "1kg", Label = "1 kg", PriceDelta = -650 }, new StoreVariantOption { Id = "2kg", Label = "2 kg" } },
        },
        new StoreVariantGroup
        {
            Id = "flavor", Label = "Aroma", Kind = "option",
            Options = { new StoreVariantOption { Id = "choc", Label = "Çikolata" }, new StoreVariantOption { Id = "caramel", Label = "Karamel", InStock = false } },
        },
    };

    [Fact]
    public void ResolveVariant_ValidSelection_BuildsSortedKeyLabelAndDelta()
    {
        var result = StorePricing.ResolveVariant(Groups, new Dictionary<string, string> { ["flavor"] = "choc", ["package"] = "1kg" });

        Assert.True(result.IsValid);
        Assert.Equal(-650m, result.PriceDelta);
        Assert.Equal("1 kg · Çikolata", result.Label);
        Assert.Equal("flavor:choc|package:1kg", result.Key);
    }

    [Theory]
    [InlineData("package", "2kg", null, null)]
    [InlineData("package", "5kg", "flavor", "choc")]
    [InlineData("package", "2kg", "flavor", "caramel")]
    public void ResolveVariant_MissingUnknownOrOutOfStockOption_IsInvalid(string g1, string o1, string? g2, string? o2)
    {
        var selected = new Dictionary<string, string> { [g1] = o1 };
        if (g2 is not null) selected[g2] = o2!;

        Assert.False(StorePricing.ResolveVariant(Groups, selected).IsValid);
    }

    [Fact]
    public void ResolveVariant_UnknownGroup_IsInvalid()
    {
        var result = StorePricing.ResolveVariant(new List<StoreVariantGroup>(), new Dictionary<string, string> { ["size"] = "m" });
        Assert.False(result.IsValid);
    }

    [Fact]
    public void ResolveVariant_NoVariants_IsValidWithEmptyKey()
    {
        var result = StorePricing.ResolveVariant(new List<StoreVariantGroup>(), null);
        Assert.True(result.IsValid);
        Assert.Equal(string.Empty, result.Key);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(100, 49.90)]
    [InlineData(499.99, 49.90)]
    [InlineData(500, 0)]
    public void ShippingFor_UsesFreeShippingThreshold(decimal subtotal, decimal expected)
    {
        Assert.Equal(expected, StorePricing.ShippingFor(subtotal, new StoreOptions()));
    }

    [Fact]
    public void SalePriceAndDiscountPercent_DeriveFromPrices()
    {
        var product = new Product { Price = 1699.90m, DiscountPrice = 1349.90m };
        Assert.Equal(1349.90m, StorePricing.SalePrice(product));
        Assert.Equal(21, StorePricing.DiscountPercent(product.Price, StorePricing.SalePrice(product)));
        Assert.Equal(0, StorePricing.DiscountPercent(100, 100));
    }

    [Theory]
    [InlineData("0532 123 45 67", "5321234567")]
    [InlineData("+90 532 123 45 67", "5321234567")]
    [InlineData("5321234567", "5321234567")]
    [InlineData("0212 123 45 67", null)]
    [InlineData("123", null)]
    public void NormalizePhone_AcceptsTurkishMobileFormats(string raw, string? expected)
    {
        Assert.Equal(expected, StoreAddressService.NormalizePhone(raw));
    }
}
