using MerfitCustomerApi.Business.Common;
using MerfitCustomerApi.Business.Dtos.Customer.Store;
using MerfitCustomerApi.Domain.Entities;
using MerfitCustomerApi.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace MerfitCustomerApi.Business.Services.Store;

/// <summary>Bir sepet satirinin guncel urun verisiyle degerlendirilmis hali.</summary>
internal sealed class EvaluatedCartLine
{
    public CartItem Item { get; init; } = null!;
    public Product? Product { get; init; }
    public string? ImageUrl { get; init; }
    public StoreVariantSelection? Variant { get; init; }
    public decimal UnitPrice { get; init; }
    public decimal UnitListPrice { get; init; }
    public int MaxQuantity { get; init; }
    public string? UnavailableReason { get; init; }
    public bool IsAvailable => UnavailableReason is null;
}

/// <summary>
/// Sepeti guncel urun, varyant ve stok verisiyle degerlendirir. Sepet ekrani ve siparis olusturma ayni kurali
/// kullanir: satisa kapali, varyanti gecersiz veya stogu yetmeyen satir "uygun degil" olur ve toplama katilmaz.
/// Stok urun seviyesindedir; ayni urunun farkli varyant satirlari ayni stoktan dusulur.
/// </summary>
internal static class StoreCartEvaluator
{
    public static async Task<List<EvaluatedCartLine>> EvaluateAsync(
        IUnitOfWork unitOfWork, IReadOnlyList<CartItem> items, StoreOptions options, CancellationToken cancellationToken)
    {
        if (items.Count == 0) return new List<EvaluatedCartLine>();

        var productIds = items.Select(i => i.ProductId).Distinct().ToList();
        var activeCategories = unitOfWork.Repository<StoreCategory>().GetQueryable().Where(c => c.IsActive).Select(c => c.Id);
        var images = unitOfWork.Repository<ProductImage>().GetQueryable();
        var rows = await unitOfWork.Repository<Product>().GetQueryable()
            .Where(p => productIds.Contains(p.Id))
            .Select(p => new
            {
                Product = p,
                Visible = p.IsActive && activeCategories.Contains(p.CategoryId),
                ImageUrl = images.Where(i => i.ProductId == p.Id)
                    .OrderByDescending(i => i.IsPrimary).ThenBy(i => i.SortOrder).ThenBy(i => i.Id)
                    .Select(i => i.ImageUrl)
                    .FirstOrDefault(),
            })
            .ToListAsync(cancellationToken);
        var byId = rows.ToDictionary(r => r.Product.Id);

        var remainingStock = rows.ToDictionary(r => r.Product.Id, r => Math.Max(0, r.Product.StockQuantity));
        var result = new List<EvaluatedCartLine>(items.Count);
        foreach (var item in items.OrderBy(i => i.CreatedAt).ThenBy(i => i.Id))
        {
            if (!byId.TryGetValue(item.ProductId, out var row) || !row.Visible)
            {
                result.Add(new EvaluatedCartLine { Item = item, Product = row?.Product, ImageUrl = row?.ImageUrl, UnavailableReason = "Bu ürün artık satışta değil." });
                continue;
            }

            var product = row.Product;
            var variant = StorePricing.ResolveVariant(StoreJson.ReadList<StoreVariantGroup>(product.VariantsJson), StoreJson.ReadMap(item.SelectedVariantJson));
            var sale = StorePricing.Round(StorePricing.SalePrice(product) + variant.PriceDelta);
            var list = StorePricing.Round(product.Price + variant.PriceDelta);
            var stockLeft = remainingStock[product.Id];
            var maxQuantity = Math.Min(stockLeft, options.MaxQuantityPerLine);

            string? reason = null;
            if (!variant.IsValid) reason = variant.Error ?? "Seçilen ürün seçeneği artık geçerli değil.";
            else if (sale <= 0) reason = "Bu ürün seçeneği şu an satışta değil.";
            else if (product.StockQuantity <= 0) reason = "Bu ürün stokta kalmadı.";
            else if (item.Quantity > options.MaxQuantityPerLine) reason = $"Bir üründen en fazla {options.MaxQuantityPerLine} adet alınabilir.";
            else if (item.Quantity > stockLeft) reason = stockLeft > 0 ? $"Stokta yalnızca {stockLeft} adet kaldı." : "Bu ürün stokta kalmadı.";

            if (reason is null) remainingStock[product.Id] = stockLeft - item.Quantity;

            result.Add(new EvaluatedCartLine
            {
                Item = item,
                Product = product,
                ImageUrl = row.ImageUrl,
                Variant = variant,
                UnitPrice = sale,
                UnitListPrice = list,
                MaxQuantity = Math.Max(0, maxQuantity),
                UnavailableReason = reason,
            });
        }

        return result;
    }

    public static StoreTotalsDto Totals(IEnumerable<EvaluatedCartLine> lines, StoreOptions options)
    {
        var available = lines.Where(l => l.IsAvailable).ToList();
        var subtotal = StorePricing.Round(available.Sum(l => l.UnitListPrice * l.Item.Quantity));
        var paid = StorePricing.Round(available.Sum(l => l.UnitPrice * l.Item.Quantity));
        var shipping = StorePricing.ShippingFor(paid, options);
        return new StoreTotalsDto
        {
            ItemCount = available.Sum(l => l.Item.Quantity),
            Subtotal = subtotal,
            Discount = StorePricing.Round(Math.Max(0, subtotal - paid)),
            ShippingCost = shipping,
            Total = StorePricing.Round(paid + shipping),
            RemainingForFreeShipping = StorePricing.RemainingForFreeShipping(paid, options),
            FreeShippingThreshold = options.FreeShippingThreshold,
            Currency = options.Currency,
        };
    }

    public static StoreCartDto ToDto(List<EvaluatedCartLine> lines, StoreOptions options)
    {
        var items = lines.Select(l => new StoreCartItemDto
        {
            Id = l.Item.Id,
            ProductId = l.Item.ProductId,
            CategoryId = l.Product?.CategoryId ?? 0,
            Name = l.Product?.Name ?? "Ürün",
            Brand = l.Product?.Brand ?? string.Empty,
            Sku = l.Product?.Sku ?? string.Empty,
            ImageUrl = StoreUrls.SafeImage(l.ImageUrl),
            SelectedVariant = StoreJson.ReadMap(l.Item.SelectedVariantJson),
            VariantLabel = l.Variant?.IsValid == true ? l.Variant.Label : string.Empty,
            Quantity = l.Item.Quantity,
            MaxQuantity = l.MaxQuantity,
            UnitPrice = l.UnitPrice,
            UnitOriginalPrice = l.UnitListPrice,
            LineTotal = StorePricing.Round(l.UnitPrice * l.Item.Quantity),
            LineOriginalTotal = StorePricing.Round(l.UnitListPrice * l.Item.Quantity),
            IsAvailable = l.IsAvailable,
            UnavailableReason = l.UnavailableReason,
        }).ToList();

        return new StoreCartDto
        {
            Items = items,
            Totals = Totals(lines, options),
            CanCheckout = items.Count > 0 && items.All(i => i.IsAvailable),
        };
    }
}
