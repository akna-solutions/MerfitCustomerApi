using MerfitCustomerApi.Business.Common;
using MerfitCustomerApi.Business.Interfaces.Services;
using MerfitCustomerApi.Business.Services.Store;
using MerfitCustomerApi.Business.Tests.Notifications;
using MerfitCustomerApi.Domain.Entities;
using MerfitCustomerApi.Domain.Entities.Enums;
using MerfitCustomerApi.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace MerfitCustomerApi.Business.Tests.Store;

/// <summary>
/// MB Shop Postgres testleri icin veri ve servis fabrikasi. Her test kendi kullanicisini, kategorisini ve urunlerini
/// (benzersiz marka/slug/SKU ile) olusturur; testler ayni veritabaninda birbirini etkilemez.
/// </summary>
internal sealed class StoreTestData
{
    public string Tag { get; } = Guid.NewGuid().ToString("N")[..10];

    public static AppDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(Environment.GetEnvironmentVariable(PostgresNotificationQueueTests.ConnectionVariable)).Options);

    public static IOptions<StoreOptions> Options(StoreOptions? options = null) => Microsoft.Extensions.Options.Options.Create(options ?? new StoreOptions());

    /// <summary>Her servis kendi DbContext/UnitOfWork'unu alir (gercek istekler gibi).</summary>
    public static (T Service, AppDbContext Db) Create<T>(Func<UnitOfWork, T> factory)
    {
        var db = CreateContext();
        return (factory(new UnitOfWork(db)), db);
    }

    public static StoreCatalogService Catalog(UnitOfWork uow) => new(uow);
    public static StoreCartService Cart(UnitOfWork uow, StoreOptions? options = null) => new(uow, Options(options));
    public static StoreAddressService Addresses(UnitOfWork uow) => new(uow, Options());
    public static StoreFavoriteService Favorites(UnitOfWork uow) => new(uow);

    public static StoreOrderService Orders(UnitOfWork uow, StoreOptions? options = null) =>
        new(uow, Options(options), Array.Empty<IStorePaymentProvider>(), NullLogger<StoreOrderService>.Instance);

    public async Task<long> UserAsync()
    {
        await using var db = CreateContext();
        var email = $"store-{Guid.NewGuid():N}@test.local";
        var user = new ApplicationUser
        {
            Email = email, NormalizedEmail = email.ToUpperInvariant(), UserName = email, NormalizedUserName = email.ToUpperInvariant(),
            PasswordHash = "x", IsActive = true, Role = UserRole.User, CreatedAt = DateTime.UtcNow,
        };
        db.ApplicationUsers.Add(user);
        await db.SaveChangesAsync();
        return user.Id;
    }

    public async Task<long> CategoryAsync(bool isActive = true, long? parentId = null)
    {
        await using var db = CreateContext();
        var category = new StoreCategory { Name = $"Kategori {Tag}", Slug = $"kategori-{Tag}-{Guid.NewGuid():N}", IsActive = isActive, ParentCategoryId = parentId, CreatedAt = DateTime.UtcNow };
        db.StoreCategories.Add(category);
        await db.SaveChangesAsync();
        return category.Id;
    }

    public string Brand => $"Marka-{Tag}";

    public async Task<Product> ProductAsync(long categoryId, decimal price, int stock, decimal? discountPrice = null, bool isActive = true,
        string? name = null, List<StoreVariantGroup>? variants = null, DateTime? createdAt = null, bool featured = false, string? imageUrl = null)
    {
        await using var db = CreateContext();
        var id = Guid.NewGuid().ToString("N");
        var product = new Product
        {
            Name = name ?? $"Urun {id[..6]}",
            Slug = $"urun-{id}",
            Sku = $"SKU-{id[..20]}",
            Description = "Test urunu",
            Brand = Brand,
            CategoryId = categoryId,
            Price = price,
            DiscountPrice = discountPrice,
            StockQuantity = stock,
            IsActive = isActive,
            IsFeatured = featured,
            VariantsJson = variants is null ? null : StoreJson.Write(variants),
            CreatedAt = createdAt ?? DateTime.UtcNow,
        };
        if (imageUrl is not null) product.Images.Add(new ProductImage { ImageUrl = imageUrl, IsPrimary = true, CreatedAt = DateTime.UtcNow });
        db.Products.Add(product);
        await db.SaveChangesAsync();
        return product;
    }

    public static async Task<int> StockAsync(long productId)
    {
        await using var db = CreateContext();
        return await db.Products.Where(p => p.Id == productId).Select(p => p.StockQuantity).SingleAsync();
    }

    public static async Task SetStockAsync(long productId, int stock)
    {
        await using var db = CreateContext();
        await db.Products.Where(p => p.Id == productId).ExecuteUpdateAsync(s => s.SetProperty(p => p.StockQuantity, stock));
    }

    public static async Task SetPriceAsync(long productId, decimal price, decimal? discount)
    {
        await using var db = CreateContext();
        await db.Products.Where(p => p.Id == productId).ExecuteUpdateAsync(s => s.SetProperty(p => p.Price, price).SetProperty(p => p.DiscountPrice, discount));
    }

    public async Task<long> AddressAsync(long userId)
    {
        var (service, db) = Create(Addresses);
        await using (db)
        {
            var address = await service.CreateAsync(userId, new Dtos.Customer.Store.UpsertStoreAddressRequest
            {
                Title = "Ev", RecipientName = "Test Kullanici", PhoneNumber = "0532 123 45 67", City = "İstanbul", District = "Kadıköy",
                AddressLine = "Örnek Mah. Deneme Sok. No: 12",
            });
            return address.Id;
        }
    }
}
