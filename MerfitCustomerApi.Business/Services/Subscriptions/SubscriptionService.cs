using MerfitCustomerApi.Business.Dtos.Customer.Subscriptions;
using MerfitCustomerApi.Business.Interfaces.Services;
using MerfitCustomerApi.Domain.Entities;
using MerfitCustomerApi.Domain.Entities.Enums;
using MerfitCustomerApi.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace MerfitCustomerApi.Business.Services.Subscriptions;

/// <summary>ISubscriptionService'in varsayilan implementasyonu.</summary>
public class SubscriptionService : ISubscriptionService
{
    private readonly IUnitOfWork _unitOfWork;

    public SubscriptionService(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<MySubscriptionResponse> GetMySubscriptionAsync(long userId, CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;

        // Aktif bir abonelik varsa o, yoksa en son baslayan abonelik gosterilir (orn. suresi dolmus).
        var subscriptions = await _unitOfWork.Repository<Subscription>()
            .GetQueryable()
            .Where(s => s.UserId == userId)
            .OrderByDescending(s => s.StartedAt)
            .ToListAsync(cancellationToken);
        var current = subscriptions.FirstOrDefault(s => IsActive(s, now)) ?? subscriptions.FirstOrDefault();
        long? currentProductId = current?.SubscriptionProductId;

        var products = await _unitOfWork.Repository<SubscriptionProduct>()
            .GetQueryable()
            .Where(p => p.IsActive || p.Id == currentProductId)
            .OrderBy(p => p.BillingPeriod)
            .ThenBy(p => p.Price)
            .ToListAsync(cancellationToken);
        var productIds = products.Select(p => p.Id).ToList();

        var featureRows = await (
                from planFeature in _unitOfWork.Repository<SubscriptionPlanFeature>().GetQueryable()
                join feature in _unitOfWork.Repository<Feature>().GetQueryable() on planFeature.FeatureId equals feature.Id
                where productIds.Contains(planFeature.SubscriptionProductId)
                orderby feature.Id
                select new { planFeature.SubscriptionProductId, feature.Name })
            .ToListAsync(cancellationToken);
        var featuresByProduct = featureRows
            .GroupBy(f => f.SubscriptionProductId)
            .ToDictionary(g => g.Key, g => g.Select(f => f.Name).ToList());

        SubscriptionProductDto ToDto(SubscriptionProduct p) => new()
        {
            Id = p.Id,
            Code = p.Code,
            Name = p.Name,
            BillingPeriod = p.BillingPeriod.ToString(),
            Price = p.Price,
            Currency = p.Currency,
            StoreProductIdIos = p.StoreProductIdIos,
            StoreProductIdAndroid = p.StoreProductIdAndroid,
            Features = featuresByProduct.TryGetValue(p.Id, out var features) ? features : new List<string>(),
        };

        var currentProduct = current is null ? null : products.FirstOrDefault(p => p.Id == current.SubscriptionProductId);

        return new MySubscriptionResponse
        {
            IsPlus = current is not null && IsActive(current, now),
            Current = current is null
                ? null
                : new SubscriptionInfoDto
                {
                    Status = current.Status.ToString(),
                    Provider = current.Provider.ToString(),
                    StartedAt = current.StartedAt,
                    ExpiresAt = current.ExpiresAt,
                    AutoRenew = current.AutoRenew,
                    CancelledAt = current.CancelledAt,
                    Product = currentProduct is null ? null : ToDto(currentProduct),
                },
            Products = products.Where(p => p.IsActive).Select(ToDto).ToList(),
        };
    }

    /// <summary>WorkoutPlanGenerator.HasActivePlusSubscriptionAsync ile ayni kural.</summary>
    private static bool IsActive(Subscription subscription, DateTime now) =>
        subscription.Status == SubscriptionStatus.Active
        && (subscription.ExpiresAt == null || subscription.ExpiresAt > now);
}
