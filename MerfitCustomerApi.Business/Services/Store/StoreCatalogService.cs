using MerfitCustomerApi.Business.Common.Pagination;
using MerfitCustomerApi.Business.Dtos.Customer.Store;
using MerfitCustomerApi.Business.Interfaces.Services;
using MerfitCustomerApi.Domain.Entities;
using MerfitCustomerApi.Domain.Entities.Enums;
using MerfitCustomerApi.Domain.Exceptions;
using MerfitCustomerApi.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace MerfitCustomerApi.Business.Services.Store;

/// <summary>
/// IStoreCatalogService'in varsayilan implementasyonu. Filtreleme, siralama ve sayfalama veritabaninda yapilir
/// (tum katalog belleğe cekilmez). Musteri yalnizca aktif urunleri ve aktif kategorilerdeki urunleri gorur.
/// </summary>
public class StoreCatalogService : IStoreCatalogService
{
    public const int MaxCampaigns = 20;
    private const int MaxSearchTerms = 5;
    private const int MaxSearchLength = 100;

    private readonly IUnitOfWork _unitOfWork;
    private readonly TimeProvider _timeProvider;

    public StoreCatalogService(IUnitOfWork unitOfWork, TimeProvider? timeProvider = null)
    {
        _unitOfWork = unitOfWork;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<List<StoreCategoryDto>> GetCategoriesAsync(CancellationToken cancellationToken = default)
    {
        var products = VisibleProducts();
        var rows = await _unitOfWork.Repository<StoreCategory>().GetQueryable()
            .Where(c => c.IsActive)
            .OrderBy(c => c.SortOrder).ThenBy(c => c.Name)
            .Select(c => new
            {
                Category = c,
                Count = products.Count(p => p.CategoryId == c.Id),
            })
            .ToListAsync(cancellationToken);

        return rows.Select(r => new StoreCategoryDto
        {
            Id = r.Category.Id,
            Name = r.Category.Name,
            Slug = r.Category.Slug,
            Description = r.Category.Description,
            ImageUrl = StoreUrls.SafeImage(r.Category.ImageUrl),
            Icon = r.Category.Icon,
            ParentCategoryId = r.Category.ParentCategoryId,
            ProductCount = r.Count,
        }).ToList();
    }

    public async Task<List<StoreCampaignDto>> GetCampaignsAsync(CancellationToken cancellationToken = default)
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var campaigns = await _unitOfWork.Repository<StoreCampaign>().GetQueryable()
            .Where(c => c.IsActive && (c.StartAt == null || c.StartAt <= now) && (c.EndAt == null || c.EndAt >= now))
            .OrderBy(c => c.SortOrder).ThenByDescending(c => c.Id)
            .Take(MaxCampaigns)
            .ToListAsync(cancellationToken);
        if (campaigns.Count == 0) return new List<StoreCampaignDto>();

        var campaignIds = campaigns.Select(c => c.Id).ToList();
        var links = await _unitOfWork.Repository<StoreCampaignProduct>().GetQueryable()
            .Where(l => campaignIds.Contains(l.CampaignId))
            .Select(l => new { l.CampaignId, l.ProductId })
            .ToListAsync(cancellationToken);
        var productIds = links.Select(l => l.ProductId).Distinct().ToList();
        var products = await WithPrimaryImage(VisibleProducts().Where(p => productIds.Contains(p.Id)))
            .ToListAsync(cancellationToken);
        var byId = products.ToDictionary(x => x.Product.Id);

        var result = new List<StoreCampaignDto>();
        foreach (var campaign in campaigns)
        {
            var items = links.Where(l => l.CampaignId == campaign.Id && byId.ContainsKey(l.ProductId))
                .Select(l => StoreProductMapper.ToListItem(byId[l.ProductId].Product, byId[l.ProductId].ImageUrl))
                .ToList();
            // Urunu kalmamis (hepsi pasif) kampanya bos kart olarak gosterilmez.
            if (items.Count == 0) continue;

            var featured = items.OrderByDescending(i => i.DiscountPercentage).ThenBy(i => i.Price).First();
            result.Add(new StoreCampaignDto
            {
                Id = campaign.Id,
                Title = campaign.Title,
                Description = campaign.Description,
                ImageUrl = StoreUrls.SafeImage(campaign.ImageUrl),
                Tint = campaign.Tint,
                ProductCount = items.Count,
                DiscountPercentage = items.Max(i => i.DiscountPercentage),
                FeaturedProduct = featured,
            });
        }

        return result;
    }

    public async Task<List<string>> GetBrandsAsync(long? categoryId, CancellationToken cancellationToken = default)
    {
        var query = VisibleProducts();
        if (categoryId.HasValue) query = query.Where(p => p.CategoryId == categoryId.Value);
        return await query.Select(p => p.Brand).Distinct().OrderBy(b => b).Take(200).ToListAsync(cancellationToken);
    }

    public async Task<PagedResult<StoreProductListItemDto>> GetProductsAsync(StoreProductQuery request, CancellationToken cancellationToken = default)
    {
        var query = VisibleProducts();

        var terms = SearchTerms(request.Q);
        foreach (var term in terms)
        {
            query = query.Where(p => p.Name.ToLower().Contains(term) || p.Brand.ToLower().Contains(term) || p.Sku.ToLower().Contains(term));
        }

        if (request.CategoryId.HasValue)
        {
            var categoryId = request.CategoryId.Value;
            var children = _unitOfWork.Repository<StoreCategory>().GetQueryable().Where(c => c.ParentCategoryId == categoryId).Select(c => c.Id);
            query = query.Where(p => p.CategoryId == categoryId || children.Contains(p.CategoryId));
        }

        if (request.CampaignId.HasValue)
        {
            var campaignId = request.CampaignId.Value;
            var now = _timeProvider.GetUtcNow().UtcDateTime;
            var live = _unitOfWork.Repository<StoreCampaign>().GetQueryable()
                .Where(c => c.Id == campaignId && c.IsActive && (c.StartAt == null || c.StartAt <= now) && (c.EndAt == null || c.EndAt >= now))
                .Select(c => c.Id);
            var linked = _unitOfWork.Repository<StoreCampaignProduct>().GetQueryable()
                .Where(l => live.Contains(l.CampaignId))
                .Select(l => l.ProductId);
            query = query.Where(p => linked.Contains(p.Id));
        }

        var brands = (request.Brands ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Take(20)
            .ToList();
        if (brands.Count > 0) query = query.Where(p => brands.Contains(p.Brand));

        if (request.MinPrice.HasValue) query = query.Where(p => (p.DiscountPrice ?? p.Price) >= request.MinPrice.Value);
        if (request.MaxPrice.HasValue) query = query.Where(p => (p.DiscountPrice ?? p.Price) <= request.MaxPrice.Value);
        if (request.InStockOnly) query = query.Where(p => p.StockQuantity > 0);
        if (request.DiscountedOnly) query = query.Where(p => p.DiscountPrice != null);
        if (request.ExcludeId.HasValue) query = query.Where(p => p.Id != request.ExcludeId.Value);

        var collection = request.Collection?.Trim().ToLowerInvariant();
        var sort = request.Sort?.Trim().ToLowerInvariant();
        var sold = SoldQuantities();
        switch (collection)
        {
            case null or "":
                break;
            case "featured":
                query = query.Where(p => p.IsFeatured);
                break;
            case "discounted":
                query = query.Where(p => p.DiscountPrice != null);
                sort ??= "discount";
                break;
            case "new":
                sort ??= "newest";
                break;
            case "bestsellers":
                // Gercek satislardan: iptal edilmemis siparislerde en az bir kez satilan urunler.
                query = query.Where(p => sold.Any(s => s.ProductId == p.Id));
                sort ??= "bestsellers";
                break;
            default:
                throw new AppValidationException(nameof(request.Collection), "Geçersiz koleksiyon.");
        }

        IOrderedQueryable<Product> ordered = sort switch
        {
            null or "" or "featured" => query.OrderByDescending(p => p.IsFeatured).ThenBy(p => p.SortOrder).ThenByDescending(p => p.CreatedAt),
            "price_asc" => query.OrderBy(p => p.DiscountPrice ?? p.Price),
            "price_desc" => query.OrderByDescending(p => p.DiscountPrice ?? p.Price),
            "discount" => query.OrderByDescending(p => p.DiscountPrice == null ? 0m : (p.Price - p.DiscountPrice.Value) / p.Price),
            "newest" => query.OrderByDescending(p => p.CreatedAt),
            "name_asc" => query.OrderBy(p => p.Name),
            "bestsellers" => query.OrderByDescending(p => sold.Where(s => s.ProductId == p.Id).Sum(s => (int?)s.Quantity) ?? 0),
            _ => throw new AppValidationException(nameof(request.Sort), "Geçersiz sıralama."),
        };
        // Sayfalar arasi kararli siralama.
        ordered = ordered.ThenByDescending(p => p.Id);

        var totalCount = await query.LongCountAsync(cancellationToken);
        var rows = await WithPrimaryImage(ordered.Skip((request.Page - 1) * request.PageSize).Take(request.PageSize))
            .ToListAsync(cancellationToken);

        return PagedResult<StoreProductListItemDto>.Create(
            rows.Select(r => StoreProductMapper.ToListItem(r.Product, r.ImageUrl)).ToList(),
            request.Page, request.PageSize, totalCount);
    }

    public async Task<StoreProductDetailDto> GetProductAsync(long id, CancellationToken cancellationToken = default)
    {
        var product = await VisibleProducts()
            .Include(p => p.Images)
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken)
            ?? throw new NotFoundException("Ürün bulunamadı veya artık satışta değil.");

        var categoryName = await _unitOfWork.Repository<StoreCategory>().GetQueryable()
            .Where(c => c.Id == product.CategoryId)
            .Select(c => c.Name)
            .FirstOrDefaultAsync(cancellationToken);

        var images = product.Images
            .OrderByDescending(i => i.IsPrimary).ThenBy(i => i.SortOrder).ThenBy(i => i.Id)
            .Select(i => StoreUrls.SafeImage(i.ImageUrl))
            .OfType<string>()
            .ToList();

        var dto = StoreProductMapper.Fill(new StoreProductDetailDto(), product, images.FirstOrDefault());
        dto.Description = product.Description;
        dto.CategoryName = categoryName;
        dto.Images = images;
        dto.Variants = StoreJson.ReadList<StoreVariantGroup>(product.VariantsJson);
        dto.Features = StoreJson.ReadList<string>(product.FeaturesJson);
        dto.Specs = StoreJson.ReadList<StoreSpec>(product.SpecsJson);
        return dto;
    }

    /// <summary>Musterinin gorebildigi urunler: aktif ve kategorisi aktif.</summary>
    private IQueryable<Product> VisibleProducts()
    {
        var activeCategories = _unitOfWork.Repository<StoreCategory>().GetQueryable().Where(c => c.IsActive).Select(c => c.Id);
        return _unitOfWork.Repository<Product>().GetQueryable().Where(p => p.IsActive && activeCategories.Contains(p.CategoryId));
    }

    /// <summary>Iptal edilmemis siparislerdeki urun satirlari ("cok satanlar").</summary>
    private IQueryable<StoreOrderItem> SoldQuantities()
    {
        var countedOrders = _unitOfWork.Repository<StoreOrder>().GetQueryable()
            .Where(o => o.Status != StoreOrderStatus.Cancelled)
            .Select(o => o.Id);
        return _unitOfWork.Repository<StoreOrderItem>().GetQueryable().Where(i => i.ProductId != null && countedOrders.Contains(i.OrderId));
    }

    private IQueryable<ProductRow> WithPrimaryImage(IQueryable<Product> query)
    {
        var images = _unitOfWork.Repository<ProductImage>().GetQueryable();
        return query.Select(p => new ProductRow
        {
            Product = p,
            ImageUrl = images.Where(i => i.ProductId == p.Id)
                .OrderByDescending(i => i.IsPrimary).ThenBy(i => i.SortOrder).ThenBy(i => i.Id)
                .Select(i => i.ImageUrl)
                .FirstOrDefault(),
        });
    }

    private static List<string> SearchTerms(string? q)
    {
        if (string.IsNullOrWhiteSpace(q)) return new List<string>();
        var text = q.Trim();
        if (text.Length > MaxSearchLength) text = text[..MaxSearchLength];
        return text.ToLowerInvariant()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Distinct()
            .Take(MaxSearchTerms)
            .ToList();
    }

    private sealed class ProductRow
    {
        public Product Product { get; set; } = null!;
        public string? ImageUrl { get; set; }
    }
}

/// <summary>Istemciye yalnizca http(s) gorsel adresleri gonderilir.</summary>
internal static class StoreUrls
{
    public static string? SafeImage(string? url) =>
        !string.IsNullOrWhiteSpace(url)
        && Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp)
            ? url.Trim()
            : null;
}
