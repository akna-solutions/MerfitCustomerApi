using MerfitCustomerApi.Domain.Entities;
using MerfitCustomerApi.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace MerfitCustomerApi.Business.Services.Store;

/// <summary>
/// Atomik stok islemleri. Okuyup-yazmak yerine tek UPDATE kullanilir; boylece eszamanli siparisler ayni stogu iki kez
/// satamaz (PostgreSQL WHERE kosulunu satir kilidi alindiktan sonra yeniden degerlendirir) ve stok negatife dusmez.
/// Cagiran, islemleri siparis kayitlariyla ayni transaction icinde yapmalidir.
/// </summary>
internal static class StoreStock
{
    /// <summary>Stok yeterliyse dusurur ve true doner; yetersizse (veya urun pasifse) hicbir sey yapmaz, false doner.</summary>
    public static async Task<bool> TryReserveAsync(IUnitOfWork unitOfWork, long productId, int quantity, DateTime now, CancellationToken cancellationToken)
    {
        var affected = await unitOfWork.Repository<Product>().GetQueryable()
            .Where(p => p.Id == productId && p.IsActive && p.StockQuantity >= quantity)
            .ExecuteUpdateAsync(s => s
                .SetProperty(p => p.StockQuantity, p => p.StockQuantity - quantity)
                .SetProperty(p => p.UpdatedAt, now), cancellationToken);
        return affected == 1;
    }

    /// <summary>Iptal/basarisiz odemede siparis kalemlerinin stogunu iade eder (urun silinmisse atlanir).</summary>
    public static async Task ReleaseAsync(IUnitOfWork unitOfWork, IEnumerable<StoreOrderItem> items, DateTime now, CancellationToken cancellationToken)
    {
        foreach (var group in items.Where(i => i.ProductId.HasValue).GroupBy(i => i.ProductId!.Value).OrderBy(g => g.Key))
        {
            var quantity = group.Sum(i => i.Quantity);
            var productId = group.Key;
            await unitOfWork.Repository<Product>().GetQueryable()
                .Where(p => p.Id == productId)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(p => p.StockQuantity, p => p.StockQuantity + quantity)
                    .SetProperty(p => p.UpdatedAt, now), cancellationToken);
        }
    }
}
