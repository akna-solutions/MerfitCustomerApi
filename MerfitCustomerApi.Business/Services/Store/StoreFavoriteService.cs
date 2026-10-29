using MerfitCustomerApi.Business.Common;
using MerfitCustomerApi.Business.Dtos.Customer.Store;
using MerfitCustomerApi.Business.Interfaces.Services;
using MerfitCustomerApi.Domain.Entities;
using MerfitCustomerApi.Domain.Exceptions;
using MerfitCustomerApi.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace MerfitCustomerApi.Business.Services.Store;

/// <summary>IStoreFavoriteService'in varsayilan implementasyonu; (UserId, ProductId) benzersizdir.</summary>
public class StoreFavoriteService : IStoreFavoriteService
{
    /// <summary>Kullanici basina en fazla favori.</summary>
    public const int MaxFavorites = 200;

    private readonly IUnitOfWork _unitOfWork;
    private readonly TimeProvider _timeProvider;

    public StoreFavoriteService(IUnitOfWork unitOfWork, TimeProvider? timeProvider = null)
    {
        _unitOfWork = unitOfWork;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<List<StoreProductListItemDto>> GetAsync(long userId, CancellationToken cancellationToken = default)
    {
        var activeCategories = _unitOfWork.Repository<StoreCategory>().GetQueryable().Where(c => c.IsActive).Select(c => c.Id);
        var products = _unitOfWork.Repository<Product>().GetQueryable().Where(p => p.IsActive && activeCategories.Contains(p.CategoryId));
        var images = _unitOfWork.Repository<ProductImage>().GetQueryable();

        var rows = await _unitOfWork.Repository<FavoriteProduct>().GetQueryable()
            .Where(f => f.UserId == userId)
            .OrderByDescending(f => f.CreatedAt).ThenByDescending(f => f.Id)
            .Join(products, f => f.ProductId, p => p.Id, (f, p) => new
            {
                Product = p,
                ImageUrl = images.Where(i => i.ProductId == p.Id)
                    .OrderByDescending(i => i.IsPrimary).ThenBy(i => i.SortOrder).ThenBy(i => i.Id)
                    .Select(i => i.ImageUrl)
                    .FirstOrDefault(),
            })
            .Take(MaxFavorites)
            .ToListAsync(cancellationToken);

        return rows.Select(r => StoreProductMapper.ToListItem(r.Product, r.ImageUrl)).ToList();
    }

    public async Task AddAsync(long userId, long productId, CancellationToken cancellationToken = default)
    {
        var repo = _unitOfWork.Repository<FavoriteProduct>();
        if (await repo.AnyAsync(f => f.UserId == userId && f.ProductId == productId, cancellationToken)) return;

        var activeCategories = _unitOfWork.Repository<StoreCategory>().GetQueryable().Where(c => c.IsActive).Select(c => c.Id);
        var exists = await _unitOfWork.Repository<Product>().GetQueryable()
            .AnyAsync(p => p.Id == productId && p.IsActive && activeCategories.Contains(p.CategoryId), cancellationToken);
        if (!exists) throw new NotFoundException("Ürün bulunamadı veya artık satışta değil.");

        if (await repo.GetQueryable().CountAsync(f => f.UserId == userId, cancellationToken) >= MaxFavorites)
        {
            throw new ConflictException($"Favorilerine en fazla {MaxFavorites} ürün ekleyebilirsin.");
        }

        await repo.AddAsync(new FavoriteProduct { UserId = userId, ProductId = productId, CreatedAt = _timeProvider.GetUtcNow().UtcDateTime }, cancellationToken);
        try
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (DbExceptionClassifier.IsUniqueViolation(ex, out _))
        {
            // Eszamanli ikinci istek: kayit zaten var, sonuc ayni.
            _unitOfWork.ClearTracking();
        }
    }

    public async Task RemoveAsync(long userId, long productId, CancellationToken cancellationToken = default)
    {
        var repo = _unitOfWork.Repository<FavoriteProduct>();
        var row = await repo.GetQueryable(asNoTracking: false).FirstOrDefaultAsync(f => f.UserId == userId && f.ProductId == productId, cancellationToken);
        if (row is null) return;
        repo.Remove(row);
        try
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Eszamanli silindi; sonuc ayni.
            _unitOfWork.ClearTracking();
        }
    }
}
