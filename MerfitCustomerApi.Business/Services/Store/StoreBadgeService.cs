using MerfitCustomerApi.Business.Common;
using MerfitCustomerApi.Business.Dtos.Customer.Store;
using MerfitCustomerApi.Business.Interfaces.Services;
using MerfitCustomerApi.Domain.Entities;
using MerfitCustomerApi.Domain.Entities.Enums;
using MerfitCustomerApi.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace MerfitCustomerApi.Business.Services.Store;

/// <summary>
/// IStoreBadgeService'in varsayilan implementasyonu. Liste basina iki sinirli sorgu: pencere icindeki en cok satan ilk N
/// urun (iptal edilmemis siparisler) ve listedeki urunlerin yayindaki yorum ozeti. Diger rozetler DTO'daki alanlardan gelir.
/// </summary>
public class StoreBadgeService : IStoreBadgeService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly StoreOptions _options;
    private readonly TimeProvider _timeProvider;

    public StoreBadgeService(IUnitOfWork unitOfWork, IOptions<StoreOptions>? options = null, TimeProvider? timeProvider = null)
    {
        _unitOfWork = unitOfWork;
        _options = options?.Value ?? new StoreOptions();
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task ApplyAsync(IEnumerable<StoreProductListItemDto> items, CancellationToken cancellationToken = default)
    {
        var list = items.Where(i => i is not null).ToList();
        if (list.Count == 0) return;

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var rules = _options.Badges;
        var ids = list.Select(i => i.Id).Distinct().ToList();

        var bestsellers = new HashSet<long>();
        if (rules.BestsellerTopCount > 0)
        {
            var since = now.AddDays(-Math.Max(1, rules.BestsellerWindowDays));
            var orders = _unitOfWork.Repository<StoreOrder>().GetQueryable()
                .Where(o => o.Status != StoreOrderStatus.Cancelled && o.CreatedAt >= since)
                .Select(o => o.Id);
            var top = await _unitOfWork.Repository<StoreOrderItem>().GetQueryable()
                .Where(i => i.ProductId != null && orders.Contains(i.OrderId))
                .GroupBy(i => i.ProductId!.Value)
                .Select(g => new { ProductId = g.Key, Quantity = g.Sum(i => i.Quantity) })
                .Where(x => x.Quantity >= rules.BestsellerMinQuantity)
                .OrderByDescending(x => x.Quantity).ThenBy(x => x.ProductId)
                .Take(rules.BestsellerTopCount)
                .Select(x => x.ProductId)
                .ToListAsync(cancellationToken);
            bestsellers = top.ToHashSet();
        }

        var ratings = await _unitOfWork.Repository<ProductReview>().GetQueryable()
            .Where(r => ids.Contains(r.ProductId) && r.Status == ProductReviewStatus.Published)
            .GroupBy(r => r.ProductId)
            .Select(g => new { ProductId = g.Key, Count = g.Count(), Sum = g.Sum(r => r.Rating) })
            .ToDictionaryAsync(x => x.ProductId, cancellationToken);

        foreach (var item in list)
        {
            var rating = ratings.GetValueOrDefault(item.Id);
            var count = rating?.Count ?? 0;
            var average = count == 0 ? 0m : Math.Round((decimal)rating!.Sum / count, 1, MidpointRounding.AwayFromZero);
            item.Badges = StoreBadgeRules.Build(
                new StoreBadgeFacts(item.HighlightBadge, item.CreatedAt, item.StockQuantity, item.Price, bestsellers.Contains(item.Id), count, average),
                _options, now);
        }
    }
}
