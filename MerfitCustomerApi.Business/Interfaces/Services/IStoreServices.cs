using MerfitCustomerApi.Business.Common.Pagination;
using MerfitCustomerApi.Business.Dtos.Customer.Store;

namespace MerfitCustomerApi.Business.Interfaces.Services;

/// <summary>MB Shop katalogu: yalnizca aktif kategori ve aktif urunler (pasif kategorideki urunler de gizlidir).</summary>
public interface IStoreCatalogService
{
    Task<List<StoreCategoryDto>> GetCategoriesAsync(CancellationToken cancellationToken = default);

    /// <summary>Su an yayinda olan ve en az bir aktif urunu bulunan kampanyalar.</summary>
    Task<List<StoreCampaignDto>> GetCampaignsAsync(CancellationToken cancellationToken = default);

    /// <summary>Aktif urunlerin markalari (filtre listesi icin); kategori verilirse o kategoridekiler.</summary>
    Task<List<string>> GetBrandsAsync(long? categoryId, CancellationToken cancellationToken = default);

    Task<PagedResult<StoreProductListItemDto>> GetProductsAsync(StoreProductQuery query, CancellationToken cancellationToken = default);

    /// <summary>Aktif urun detayi; urun yoksa veya pasifse NotFound.</summary>
    Task<StoreProductDetailDto> GetProductAsync(long id, CancellationToken cancellationToken = default);
}

/// <summary>Kullanicinin kalici sepeti. Tum islemler JWT'deki kullaniciya aittir; fiyatlar sunucuda hesaplanir.</summary>
public interface IStoreCartService
{
    Task<StoreCartDto> GetAsync(long userId, CancellationToken cancellationToken = default);

    /// <summary>Ayni urun + varyant varsa adedi artirir. Stok veya satir siniri asilirsa 409.</summary>
    Task<StoreCartDto> AddItemAsync(long userId, AddCartItemRequest request, CancellationToken cancellationToken = default);

    /// <summary>Adedi gunceller; 0 satiri siler. Baska kullanicinin satiri icin NotFound.</summary>
    Task<StoreCartDto> UpdateItemAsync(long userId, long itemId, UpdateCartItemRequest request, CancellationToken cancellationToken = default);

    Task<StoreCartDto> RemoveItemAsync(long userId, long itemId, CancellationToken cancellationToken = default);

    Task<StoreCartDto> ClearAsync(long userId, CancellationToken cancellationToken = default);
}

public interface IStoreFavoriteService
{
    /// <summary>Favori urunler (yalnizca hala aktif olanlar), son eklenen once.</summary>
    Task<List<StoreProductListItemDto>> GetAsync(long userId, CancellationToken cancellationToken = default);

    /// <summary>Idempotent: zaten favoriyse bir sey yapmaz.</summary>
    Task AddAsync(long userId, long productId, CancellationToken cancellationToken = default);

    /// <summary>Idempotent: favoride degilse bir sey yapmaz.</summary>
    Task RemoveAsync(long userId, long productId, CancellationToken cancellationToken = default);
}

public interface IStoreAddressService
{
    Task<List<StoreAddressDto>> GetAsync(long userId, CancellationToken cancellationToken = default);

    Task<StoreAddressDto> CreateAsync(long userId, UpsertStoreAddressRequest request, CancellationToken cancellationToken = default);

    Task<StoreAddressDto> UpdateAsync(long userId, long id, UpsertStoreAddressRequest request, CancellationToken cancellationToken = default);

    Task DeleteAsync(long userId, long id, CancellationToken cancellationToken = default);

    Task<StoreAddressDto> SetDefaultAsync(long userId, long id, CancellationToken cancellationToken = default);
}

public interface IStoreOrderService
{
    Task<StoreCheckoutOptionsDto> GetCheckoutOptionsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Sepetten siparis olusturur. Fiyat, indirim, kargo ve stok sunucuda dogrulanir; stok tek transaction icinde
    /// atomik olarak dusurulur. Ayni IdempotencyKey ile tekrar gelen istek ilk siparisi dondurur (Created=false).
    /// </summary>
    Task<(StoreOrderDto Order, bool Created)> CreateAsync(long userId, CreateStoreOrderRequest request, CancellationToken cancellationToken = default);

    Task<PagedResult<StoreOrderListItemDto>> GetListAsync(long userId, PagedRequest request, CancellationToken cancellationToken = default);

    /// <summary>Yalnizca kullanicinin kendi siparisi; aksi halde NotFound.</summary>
    Task<StoreOrderDto> GetAsync(long userId, long orderId, CancellationToken cancellationToken = default);

    /// <summary>Yalnizca "received" durumundaki siparis iptal edilebilir; stok iade edilir.</summary>
    Task<StoreOrderDto> CancelAsync(long userId, long orderId, CancelStoreOrderRequest request, CancellationToken cancellationToken = default);
}
