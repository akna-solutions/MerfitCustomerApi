using System.Globalization;
using MerfitCustomerApi.Business.Common;
using MerfitCustomerApi.Domain.Entities;

namespace MerfitCustomerApi.Business.Services.Store;

/// <summary>
/// MB Shop fiyat kurallarinin tek kaynagi. Satis fiyati = DiscountPrice ?? Price; varyant secenegi her iki fiyata da
/// PriceDelta ekler. Ara toplam liste fiyatlariyla, indirim (liste - satis) farkiyla, kargo indirimli ara toplama gore
/// hesaplanir: Total = Subtotal - Discount + Shipping. Tutarlar kurusa (2 hane, yukari yuvarlama) yuvarlanir.
/// </summary>
public static class StorePricing
{
    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");

    public static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);

    public static decimal SalePrice(Product product) => product.DiscountPrice ?? product.Price;

    public static int DiscountPercent(decimal listPrice, decimal salePrice)
    {
        if (listPrice <= 0 || salePrice >= listPrice) return 0;
        return (int)Math.Round((listPrice - salePrice) / listPrice * 100m, MidpointRounding.AwayFromZero);
    }

    public static decimal ShippingFor(decimal discountedSubtotal, StoreOptions options)
    {
        if (discountedSubtotal <= 0 || discountedSubtotal >= options.FreeShippingThreshold) return 0m;
        return Round(options.ShippingCost);
    }

    public static decimal RemainingForFreeShipping(decimal discountedSubtotal, StoreOptions options) =>
        discountedSubtotal > 0 && discountedSubtotal < options.FreeShippingThreshold
            ? Round(options.FreeShippingThreshold - discountedSubtotal)
            : 0m;

    /// <summary>
    /// Istemcinin sectigi varyantlari urunun tanimina gore dogrular: her grup icin gecerli ve stokta bir secenek
    /// secilmeli, tanimsiz grup gonderilmemeli. Fiyat farki ve okunabilir etiket sunucuda uretilir.
    /// </summary>
    public static StoreVariantSelection ResolveVariant(IReadOnlyList<StoreVariantGroup> groups, IReadOnlyDictionary<string, string>? selected)
    {
        selected ??= new Dictionary<string, string>();
        var known = groups.Select(g => g.Id).ToHashSet(StringComparer.Ordinal);
        if (selected.Keys.Any(k => !known.Contains(k)))
        {
            return Invalid("Seçilen ürün seçeneği geçersiz.");
        }

        var delta = 0m;
        var labels = new List<string>();
        var normalized = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var group in groups)
        {
            if (!selected.TryGetValue(group.Id, out var optionId) || string.IsNullOrWhiteSpace(optionId))
            {
                return Invalid($"Lütfen {group.Label.ToLower(Turkish)} seçin.");
            }

            var option = group.Options.FirstOrDefault(o => o.Id == optionId);
            if (option is null)
            {
                return Invalid("Seçilen ürün seçeneği geçersiz.");
            }

            if (!option.InStock)
            {
                return Invalid($"{group.Label}: {option.Label} seçeneği şu an stokta yok.");
            }

            delta += option.PriceDelta;
            labels.Add(option.Label);
            normalized[group.Id] = option.Id;
        }

        var key = string.Join("|", normalized.OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => $"{kv.Key}:{kv.Value}"));
        return new StoreVariantSelection(true, null, delta, string.Join(" · ", labels), key, normalized);

        static StoreVariantSelection Invalid(string error) => new(false, error, 0m, string.Empty, string.Empty, new Dictionary<string, string>());
    }
}
