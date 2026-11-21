using MerfitCustomerApi.Business.Dtos.Customer.Store;
using MerfitCustomerApi.Business.Interfaces.Services;
using MerfitCustomerApi.Domain.Entities;
using MerfitCustomerApi.Domain.Entities.Enums;
using MerfitCustomerApi.Domain.Exceptions;
using MerfitCustomerApi.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace MerfitCustomerApi.Business.Services.Store;

/// <summary>
/// Oneri verisini toplar (aday urunler, birlikte satin alma, satis, yorum puani, kullanici sinyalleri) ve
/// StoreRecommendationScorer ile siralar. Her sorgu sinirlidir; aday havuzu birkac yuz urunu gecmez.
/// Yalnizca gercek veriler kullanilir: siparis yoksa populerlik/birlikte alinma 0'dir, sahte veri uretilmez.
/// Kullanici gizlilik ayarinda kisisellestirilmis onerileri kapattiysa kullanicinin favori, sepet ve siparisleri okunmaz.
/// </summary>
public class StoreRecommendationService : IStoreRecommendationService
{
    public const int DefaultLimit = 10;
    public const int MaxLimit = 20;
    private const int CoPurchaseOrderWindowDays = 365;
    private const int PopularityWindowDays = 90;
    private const int MaxContextOrders = 2000;
    private const int MaxUserSeeds = 20;
    private const int RelatedCategoryPool = 200;
    private const int BrandPool = 60;
    private const int AffinityPool = 150;
    private const int PopularPool = 60;

    private readonly IUnitOfWork _unitOfWork;
    private readonly TimeProvider _timeProvider;

    public StoreRecommendationService(IUnitOfWork unitOfWork, TimeProvider? timeProvider = null)
    {
        _unitOfWork = unitOfWork;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<StoreProductRecommendationsDto> GetForProductAsync(long userId, long productId, int limit, CancellationToken cancellationToken = default)
    {
        limit = Math.Clamp(limit <= 0 ? DefaultLimit : limit, 1, MaxLimit);
        var now = _timeProvider.GetUtcNow().UtcDateTime;

        var product = await VisibleProducts().FirstOrDefaultAsync(p => p.Id == productId, cancellationToken)
            ?? throw new NotFoundException("Ürün bulunamadı veya artık satışta değil.");

        var parents = await _unitOfWork.Repository<StoreCategory>().GetQueryable()
            .Where(c => c.IsActive)
            .Select(c => new { c.Id, c.ParentCategoryId })
            .ToDictionaryAsync(c => c.Id, c => c.ParentCategoryId, cancellationToken);
        var tree = new StoreRecommendationScorer.CategoryTree(parents);

        var personalized = await PersonalizationAllowedAsync(userId, cancellationToken);
        var signals = personalized ? await UserSignalsAsync(userId, now, cancellationToken) : UserSignals.Empty;

        // --- Birlikte satin alma ---
        var countedOrders = Orders(now.AddDays(-CoPurchaseOrderWindowDays));
        var coWithContext = await CoPurchaseAsync(countedOrders, new[] { productId }, cancellationToken);
        var userSeeds = signals.CartProductIds.Concat(signals.FavoriteProductIds).Where(id => id != productId).Distinct().Take(MaxUserSeeds).ToList();
        var coWithUser = userSeeds.Count > 0 ? await CoPurchaseAsync(countedOrders, userSeeds, cancellationToken) : new Dictionary<long, int>();

        // --- Kullanici ilgi alanlari (kategori/marka) ---
        var signalIds = signals.AllProductIds.Append(productId).Distinct().ToList();
        var signalProducts = await _unitOfWork.Repository<Product>().GetQueryable()
            .Where(p => signalIds.Contains(p.Id))
            .Select(p => new { p.Id, p.CategoryId, p.Brand })
            .ToListAsync(cancellationToken);
        var affinity = new StoreRecommendationScorer.Affinity();
        foreach (var p in signalProducts)
        {
            double weight = 0;
            if (p.Id == productId) weight += 3;
            if (signals.CartProductIds.Contains(p.Id)) weight += 3;
            if (signals.FavoriteProductIds.Contains(p.Id)) weight += 2;
            if (signals.PurchasedProductIds.Contains(p.Id)) weight += 2;
            if (weight > 0) affinity.Add(p.CategoryId, p.Brand, weight, tree);
        }

        // --- Aday havuzu ---
        var related = parents.Keys.Where(id => tree.Relation(product.CategoryId, id) > 0).ToList();
        var affinityCategories = affinity.Categories.OrderByDescending(kv => kv.Value).Take(8).Select(kv => kv.Key).ToList();
        var affinityBrands = affinity.Brands.OrderByDescending(kv => kv.Value).Take(5).Select(kv => kv.Key).ToList();
        var since90 = now.AddDays(-PopularityWindowDays);
        var popularIds = await Orders(since90)
            .Join(_unitOfWork.Repository<StoreOrderItem>().GetQueryable().Where(i => i.ProductId != null), o => o, i => i.OrderId, (o, i) => i)
            .GroupBy(i => i.ProductId!.Value)
            .OrderByDescending(g => g.Sum(i => i.Quantity))
            .Select(g => g.Key)
            .Take(PopularPool)
            .ToListAsync(cancellationToken);

        var others = VisibleProducts().Where(p => p.Id != productId);
        var pools = new List<IQueryable<Product>>
        {
            others.Where(p => related.Contains(p.CategoryId)).OrderByDescending(p => p.IsFeatured).ThenByDescending(p => p.Id).Take(RelatedCategoryPool),
            others.Where(p => p.Brand == product.Brand).OrderByDescending(p => p.Id).Take(BrandPool),
        };
        var directIds = coWithContext.Keys.Concat(coWithUser.Keys).Concat(popularIds).Distinct().ToList();
        if (directIds.Count > 0) pools.Add(others.Where(p => directIds.Contains(p.Id)));
        if (affinityCategories.Count > 0)
            pools.Add(others.Where(p => affinityCategories.Contains(p.CategoryId) || affinityBrands.Contains(p.Brand)).OrderByDescending(p => p.Id).Take(AffinityPool));

        var candidatesById = new Dictionary<long, Product>();
        foreach (var pool in pools)
        {
            foreach (var p in await pool.ToListAsync(cancellationToken)) candidatesById.TryAdd(p.Id, p);
        }
        if (candidatesById.Count == 0) return new StoreProductRecommendationsDto { ProductId = productId, Personalized = personalized };

        var ids = candidatesById.Keys.ToList();
        var sales = await Orders(since90)
            .Join(_unitOfWork.Repository<StoreOrderItem>().GetQueryable().Where(i => i.ProductId != null && ids.Contains(i.ProductId.Value)), o => o, i => i.OrderId, (o, i) => i)
            .GroupBy(i => i.ProductId!.Value)
            .Select(g => new { ProductId = g.Key, Quantity = g.Sum(i => i.Quantity) })
            .ToDictionaryAsync(x => x.ProductId, x => x.Quantity, cancellationToken);
        var ratings = await _unitOfWork.Repository<ProductReview>().GetQueryable()
            .Where(r => r.Status == ProductReviewStatus.Published && ids.Contains(r.ProductId))
            .GroupBy(r => r.ProductId)
            .Select(g => new { ProductId = g.Key, Sum = g.Sum(r => r.Rating), Count = g.Count() })
            .ToDictionaryAsync(x => x.ProductId, x => (Sum: (double)x.Sum, x.Count), cancellationToken);

        var candidates = candidatesById.Values.Select(p =>
        {
            var rating = ratings.GetValueOrDefault(p.Id);
            return new StoreRecommendationScorer.Candidate(
                p.Id, p.CategoryId, p.Brand, StorePricing.SalePrice(p), p.Price, p.StockQuantity > 0,
                StoreRecommendationScorer.Tokens(p.Name, p.Brand),
                coWithContext.GetValueOrDefault(p.Id), coWithUser.GetValueOrDefault(p.Id), sales.GetValueOrDefault(p.Id),
                rating.Sum, rating.Count,
                InCart: signals.CartProductIds.Contains(p.Id),
                PurchasedBefore: signals.PurchasedProductIds.Contains(p.Id));
        }).ToList();

        var context = new StoreRecommendationScorer.Context(product.Id, product.CategoryId, product.Brand, StorePricing.SalePrice(product),
            StoreRecommendationScorer.Tokens(product.Name, product.Brand));

        var similar = StoreRecommendationScorer.Pick(
            candidates.Select(c => (c, StoreRecommendationScorer.SimilarScore(context, c, tree))), limit, capCategory: false);
        var similarIds = similar.Select(c => c.Id).ToHashSet();
        var maxSales = sales.Count == 0 ? 0 : sales.Values.Max();
        var recommended = StoreRecommendationScorer.Pick(
            candidates.Where(c => !similarIds.Contains(c.Id)).Select(c => (c, StoreRecommendationScorer.RecommendedScore(context, c, tree, affinity, maxSales))),
            limit, capCategory: true);

        var images = await PrimaryImagesAsync(similar.Concat(recommended).Select(c => c.Id).ToList(), cancellationToken);
        StoreProductListItemDto ToDto(StoreRecommendationScorer.Candidate c) => StoreProductMapper.ToListItem(candidatesById[c.Id], images.GetValueOrDefault(c.Id));

        return new StoreProductRecommendationsDto
        {
            ProductId = productId,
            Similar = similar.Select(ToDto).ToList(),
            Recommended = recommended.Select(ToDto).ToList(),
            Personalized = personalized,
        };
    }

    private IQueryable<Product> VisibleProducts()
    {
        var activeCategories = _unitOfWork.Repository<StoreCategory>().GetQueryable().Where(c => c.IsActive).Select(c => c.Id);
        return _unitOfWork.Repository<Product>().GetQueryable().Where(p => p.IsActive && activeCategories.Contains(p.CategoryId));
    }

    /// <summary>Iptal edilmemis ve odemesi basarisiz/iade olmamis siparisler (talep sinyali).</summary>
    private IQueryable<long> Orders(DateTime since) =>
        _unitOfWork.Repository<StoreOrder>().GetQueryable()
            .Where(o => o.CreatedAt >= since
                        && o.Status != StoreOrderStatus.Cancelled
                        && o.PaymentStatus != StorePaymentStatus.Failed
                        && o.PaymentStatus != StorePaymentStatus.Cancelled
                        && o.PaymentStatus != StorePaymentStatus.RefundPending
                        && o.PaymentStatus != StorePaymentStatus.Refunded)
            .Select(o => o.Id);

    /// <summary>Tohum urunlerden biriyle ayni sipariste bulunan diger urunler: urun -> farkli siparis sayisi.</summary>
    private async Task<Dictionary<long, int>> CoPurchaseAsync(IQueryable<long> countedOrders, IReadOnlyCollection<long> seeds, CancellationToken cancellationToken)
    {
        var items = _unitOfWork.Repository<StoreOrderItem>().GetQueryable();
        var orderIds = await items
            .Where(i => i.ProductId != null && seeds.Contains(i.ProductId.Value) && countedOrders.Contains(i.OrderId))
            .Select(i => i.OrderId)
            .Distinct()
            .OrderByDescending(id => id)
            .Take(MaxContextOrders)
            .ToListAsync(cancellationToken);
        if (orderIds.Count == 0) return new Dictionary<long, int>();

        return await items
            .Where(i => i.ProductId != null && orderIds.Contains(i.OrderId) && !seeds.Contains(i.ProductId.Value))
            .GroupBy(i => i.ProductId!.Value)
            .Select(g => new { ProductId = g.Key, Orders = g.Select(i => i.OrderId).Distinct().Count() })
            .ToDictionaryAsync(x => x.ProductId, x => x.Orders, cancellationToken);
    }

    /// <summary>Gizlilik ayari kaydi yoksa varsayilan acik (ProfileService ile ayni).</summary>
    private async Task<bool> PersonalizationAllowedAsync(long userId, CancellationToken cancellationToken)
    {
        var setting = await _unitOfWork.Repository<UserPrivacySetting>().GetQueryable()
            .Where(s => s.UserId == userId)
            .Select(s => (bool?)s.PersonalizedRecommendations)
            .FirstOrDefaultAsync(cancellationToken);
        return setting ?? true;
    }

    private async Task<UserSignals> UserSignalsAsync(long userId, DateTime now, CancellationToken cancellationToken)
    {
        var favorites = await _unitOfWork.Repository<FavoriteProduct>().GetQueryable()
            .Where(f => f.UserId == userId)
            .OrderByDescending(f => f.CreatedAt)
            .Select(f => f.ProductId)
            .Take(50)
            .ToListAsync(cancellationToken);
        var carts = _unitOfWork.Repository<Cart>().GetQueryable().Where(c => c.UserId == userId).Select(c => c.Id);
        var cart = await _unitOfWork.Repository<CartItem>().GetQueryable()
            .Where(i => carts.Contains(i.CartId))
            .Select(i => i.ProductId)
            .Distinct()
            .Take(50)
            .ToListAsync(cancellationToken);
        var since = now.AddDays(-CoPurchaseOrderWindowDays);
        var userOrders = _unitOfWork.Repository<StoreOrder>().GetQueryable()
            .Where(o => o.UserId == userId && o.CreatedAt >= since && o.Status != StoreOrderStatus.Cancelled
                        && o.PaymentStatus != StorePaymentStatus.Failed && o.PaymentStatus != StorePaymentStatus.RefundPending
                        && o.PaymentStatus != StorePaymentStatus.Refunded)
            .Select(o => o.Id);
        var purchased = await _unitOfWork.Repository<StoreOrderItem>().GetQueryable()
            .Where(i => i.ProductId != null && userOrders.Contains(i.OrderId))
            .Select(i => i.ProductId!.Value)
            .Distinct()
            .Take(100)
            .ToListAsync(cancellationToken);
        return new UserSignals(favorites.ToHashSet(), cart.ToHashSet(), purchased.ToHashSet());
    }

    private async Task<Dictionary<long, string?>> PrimaryImagesAsync(List<long> productIds, CancellationToken cancellationToken)
    {
        if (productIds.Count == 0) return new Dictionary<long, string?>();
        var rows = await _unitOfWork.Repository<ProductImage>().GetQueryable()
            .Where(i => productIds.Contains(i.ProductId))
            .OrderByDescending(i => i.IsPrimary).ThenBy(i => i.SortOrder).ThenBy(i => i.Id)
            .Select(i => new { i.ProductId, i.ImageUrl })
            .ToListAsync(cancellationToken);
        return rows.GroupBy(r => r.ProductId).ToDictionary(g => g.Key, g => (string?)g.First().ImageUrl);
    }

    private sealed record UserSignals(HashSet<long> FavoriteProductIds, HashSet<long> CartProductIds, HashSet<long> PurchasedProductIds)
    {
        public static readonly UserSignals Empty = new(new HashSet<long>(), new HashSet<long>(), new HashSet<long>());
        public IEnumerable<long> AllProductIds => FavoriteProductIds.Concat(CartProductIds).Concat(PurchasedProductIds);
    }
}
