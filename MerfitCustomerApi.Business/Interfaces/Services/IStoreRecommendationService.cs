using MerfitCustomerApi.Business.Dtos.Customer.Store;

namespace MerfitCustomerApi.Business.Interfaces.Services;

/// <summary>Urun detayindaki "Benzer Urunler" ve "Senin Icin Onerilenler" (bkz. StoreRecommendationScorer).</summary>
public interface IStoreRecommendationService
{
    /// <summary>Urun yoksa veya satista degilse NotFound. limit 1-20 (her liste icin).</summary>
    Task<StoreProductRecommendationsDto> GetForProductAsync(long userId, long productId, int limit, CancellationToken cancellationToken = default);
}
