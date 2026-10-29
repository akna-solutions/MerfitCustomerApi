using MerfitCustomerApi.Business.Common;
using MerfitCustomerApi.Business.Dtos.Customer.Store;
using MerfitCustomerApi.Business.Interfaces.Services;
using MerfitCustomerApi.Domain.Entities;
using MerfitCustomerApi.Domain.Exceptions;
using MerfitCustomerApi.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace MerfitCustomerApi.Business.Services.Store;

/// <summary>
/// IStoreCartService'in varsayilan implementasyonu. Sepet kullanici basina tek satirdir (store.Cart.UserId benzersiz)
/// ve veritabaninda kalicidir. Satirlarda fiyat saklanmaz; her yanit guncel fiyatla hesaplanir.
/// </summary>
public class StoreCartService : IStoreCartService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly StoreOptions _options;
    private readonly TimeProvider _timeProvider;

    public StoreCartService(IUnitOfWork unitOfWork, IOptions<StoreOptions> options, TimeProvider? timeProvider = null)
    {
        _unitOfWork = unitOfWork;
        _options = options.Value;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    private DateTime UtcNow => _timeProvider.GetUtcNow().UtcDateTime;

    public async Task<StoreCartDto> GetAsync(long userId, CancellationToken cancellationToken = default)
    {
        var items = await ItemsOf(userId).ToListAsync(cancellationToken);
        var lines = await StoreCartEvaluator.EvaluateAsync(_unitOfWork, items, _options, cancellationToken);
        return StoreCartEvaluator.ToDto(lines, _options);
    }

    public async Task<StoreCartDto> AddItemAsync(long userId, AddCartItemRequest request, CancellationToken cancellationToken = default)
    {
        var product = await VisibleProduct(request.ProductId, cancellationToken)
            ?? throw new NotFoundException("Ürün bulunamadı veya artık satışta değil.");
        if (product.StockQuantity <= 0)
        {
            throw new ConflictException("Bu ürün stokta yok.");
        }

        var variant = StorePricing.ResolveVariant(StoreJson.ReadList<StoreVariantGroup>(product.VariantsJson), request.SelectedVariant);
        if (!variant.IsValid)
        {
            throw new AppValidationException(nameof(request.SelectedVariant), variant.Error!);
        }

        var cart = await GetOrCreateCartAsync(userId, cancellationToken);
        var itemRepo = _unitOfWork.Repository<CartItem>();
        var lines = await itemRepo.GetQueryable(asNoTracking: false).Where(i => i.CartId == cart.Id).ToListAsync(cancellationToken);
        var existing = lines.FirstOrDefault(i => i.ProductId == product.Id && i.VariantKey == variant.Key);
        var newQuantity = (existing?.Quantity ?? 0) + request.Quantity;
        var otherLines = lines.Where(i => i.ProductId == product.Id && i != existing).Sum(i => i.Quantity);

        EnsureQuantityAllowed(product, newQuantity, otherLines);
        if (existing is null && lines.Count >= _options.MaxCartLines)
        {
            throw new ConflictException($"Sepete en fazla {_options.MaxCartLines} farklı ürün eklenebilir.");
        }

        if (existing is null)
        {
            await itemRepo.AddAsync(new CartItem
            {
                CartId = cart.Id,
                ProductId = product.Id,
                VariantKey = variant.Key,
                SelectedVariantJson = variant.Selected.Count > 0 ? StoreJson.Write(variant.Selected) : null,
                Quantity = newQuantity,
                CreatedAt = UtcNow,
            }, cancellationToken);
        }
        else
        {
            existing.Quantity = newQuantity;
            existing.UpdatedAt = UtcNow;
        }

        await SaveAsync(cancellationToken);
        return await GetAsync(userId, cancellationToken);
    }

    public async Task<StoreCartDto> UpdateItemAsync(long userId, long itemId, UpdateCartItemRequest request, CancellationToken cancellationToken = default)
    {
        var item = await OwnedItemAsync(userId, itemId, cancellationToken);
        if (request.Quantity == 0)
        {
            _unitOfWork.Repository<CartItem>().Remove(item);
            await SaveAsync(cancellationToken);
            return await GetAsync(userId, cancellationToken);
        }

        if (request.Quantity > item.Quantity)
        {
            // Adet artiriliyorsa urun satista ve stok yeterli olmali; azaltmak her zaman serbesttir.
            var product = await VisibleProduct(item.ProductId, cancellationToken)
                ?? throw new ConflictException("Bu ürün artık satışta değil. Sepetinden kaldırabilirsin.");
            var otherLines = await _unitOfWork.Repository<CartItem>().GetQueryable()
                .Where(i => i.CartId == item.CartId && i.ProductId == item.ProductId && i.Id != item.Id)
                .SumAsync(i => i.Quantity, cancellationToken);
            EnsureQuantityAllowed(product, request.Quantity, otherLines);
        }

        item.Quantity = request.Quantity;
        item.UpdatedAt = UtcNow;
        await SaveAsync(cancellationToken);
        return await GetAsync(userId, cancellationToken);
    }

    public async Task<StoreCartDto> RemoveItemAsync(long userId, long itemId, CancellationToken cancellationToken = default)
    {
        var item = await OwnedItemAsync(userId, itemId, cancellationToken);
        _unitOfWork.Repository<CartItem>().Remove(item);
        await SaveAsync(cancellationToken);
        return await GetAsync(userId, cancellationToken);
    }

    public async Task<StoreCartDto> ClearAsync(long userId, CancellationToken cancellationToken = default)
    {
        var repo = _unitOfWork.Repository<CartItem>();
        var items = await ItemsOf(userId, tracking: true).ToListAsync(cancellationToken);
        if (items.Count > 0)
        {
            repo.RemoveRange(items);
            await SaveAsync(cancellationToken);
        }

        return await GetAsync(userId, cancellationToken);
    }

    private void EnsureQuantityAllowed(Product product, int lineQuantity, int otherLinesQuantity)
    {
        if (lineQuantity > _options.MaxQuantityPerLine)
        {
            throw new ConflictException($"Bir üründen en fazla {_options.MaxQuantityPerLine} adet ekleyebilirsin.");
        }

        var available = product.StockQuantity - otherLinesQuantity;
        if (lineQuantity > available)
        {
            throw new ConflictException(available > 0
                ? $"Stokta yeterli ürün yok. En fazla {available} adet ekleyebilirsin."
                : "Stokta yeterli ürün yok.");
        }
    }

    private IQueryable<CartItem> ItemsOf(long userId, bool tracking = false)
    {
        // Alt sorgudaki AsNoTracking tum sorguya yayildigi icin sepet sorgusu da ayni izleme moduyla kurulur.
        var cartIds = _unitOfWork.Repository<Cart>().GetQueryable(asNoTracking: !tracking).Where(c => c.UserId == userId).Select(c => c.Id);
        return _unitOfWork.Repository<CartItem>().GetQueryable(asNoTracking: !tracking).Where(i => cartIds.Contains(i.CartId));
    }

    private async Task<CartItem> OwnedItemAsync(long userId, long itemId, CancellationToken cancellationToken) =>
        await ItemsOf(userId, tracking: true).FirstOrDefaultAsync(i => i.Id == itemId, cancellationToken)
        ?? throw new NotFoundException("Sepet satırı bulunamadı.");

    private Task<Product?> VisibleProduct(long productId, CancellationToken cancellationToken)
    {
        var activeCategories = _unitOfWork.Repository<StoreCategory>().GetQueryable().Where(c => c.IsActive).Select(c => c.Id);
        return _unitOfWork.Repository<Product>().GetQueryable()
            .FirstOrDefaultAsync(p => p.Id == productId && p.IsActive && activeCategories.Contains(p.CategoryId), cancellationToken);
    }

    private async Task<Cart> GetOrCreateCartAsync(long userId, CancellationToken cancellationToken)
    {
        var repo = _unitOfWork.Repository<Cart>();
        var cart = await repo.GetQueryable().FirstOrDefaultAsync(c => c.UserId == userId, cancellationToken);
        if (cart is not null) return cart;

        cart = new Cart { UserId = userId, CreatedAt = UtcNow };
        await repo.AddAsync(cart, cancellationToken);
        try
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return cart;
        }
        catch (DbUpdateException ex) when (DbExceptionClassifier.IsUniqueViolation(ex, out _))
        {
            // Ayni kullanicinin eszamanli ilk istegi sepeti olusturdu; onu kullan.
            _unitOfWork.ClearTracking();
            return await repo.GetQueryable().FirstAsync(c => c.UserId == userId, cancellationToken);
        }
    }

    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (DbExceptionClassifier.IsUniqueViolation(ex, out _) || ex is DbUpdateConcurrencyException)
        {
            _unitOfWork.ClearTracking();
            throw new ConflictException("Sepetin aynı anda başka bir işlemle güncellendi. Lütfen tekrar dene.");
        }
    }
}
