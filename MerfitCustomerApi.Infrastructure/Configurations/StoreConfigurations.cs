using MerfitCustomerApi.Domain.Entities;
using MerfitCustomerApi.Domain.Entities.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MerfitCustomerApi.Infrastructure.Configurations;

// MB Shop (e-ticaret) tablolari "store" semasindadir. Veritabani seviyesindeki garantiler burada tanimlanir:
// - Slug/SKU/siparis numarasi benzersizligi, kullanici basina tek sepet, tek varsayilan adres, tekil favori.
// - Stok negatif olamaz; fiyat ve tutarlar numeric(12,2), negatif olamaz.
// - Siparis Status alani iyimser eszamanlilik belirtecidir (cift iptal / cift stok iadesi olmaz).
// - (UserId, IdempotencyKey) benzersiz: ayni istek tekrar gelse de tek siparis olusur.
// MerfitApi (admin) ayni tablolari kendi kopya yapilandirmasiyla okur/yazar; migration yalnizca bu projededir.

internal static class StoreColumns
{
    public const string Schema = "store";
    public const string Money = "numeric(12,2)";
}

public class StoreCategoryConfiguration : IEntityTypeConfiguration<StoreCategory>
{
    public void Configure(EntityTypeBuilder<StoreCategory> builder)
    {
        builder.ToTable("Category", StoreColumns.Schema);
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Name).IsRequired().HasMaxLength(120);
        builder.Property(x => x.Slug).IsRequired().HasMaxLength(140);
        builder.Property(x => x.Description).HasMaxLength(1000);
        builder.Property(x => x.ImageUrl).HasMaxLength(500);
        builder.Property(x => x.Icon).HasMaxLength(60);
        builder.Property(x => x.SortOrder).HasDefaultValue(0);
        builder.Property(x => x.IsActive).HasDefaultValue(true);

        builder.HasIndex(x => x.Slug).IsUnique();
        builder.HasIndex(x => new { x.IsActive, x.SortOrder });

        builder.HasOne<StoreCategory>()
            .WithMany()
            .HasForeignKey(x => x.ParentCategoryId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        builder.ToTable("Product", StoreColumns.Schema, table =>
        {
            table.HasCheckConstraint("CK_Product_StockQuantity_NonNegative", "\"StockQuantity\" >= 0");
            table.HasCheckConstraint("CK_Product_Price_Positive", "\"Price\" > 0");
            table.HasCheckConstraint("CK_Product_DiscountPrice_Valid", "\"DiscountPrice\" IS NULL OR (\"DiscountPrice\" > 0 AND \"DiscountPrice\" < \"Price\")");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Name).IsRequired().HasMaxLength(200);
        builder.Property(x => x.Slug).IsRequired().HasMaxLength(220);
        builder.Property(x => x.Description).IsRequired().HasMaxLength(4000);
        builder.Property(x => x.Sku).IsRequired().HasMaxLength(64);
        builder.Property(x => x.Brand).IsRequired().HasMaxLength(100);
        builder.Property(x => x.Price).HasColumnType(StoreColumns.Money);
        builder.Property(x => x.DiscountPrice).HasColumnType(StoreColumns.Money);
        // Admin stok guncellemesinin eszamanli bir siparisin stok dususunu ezmemesi icin (UPDATE ... WHERE StockQuantity = eski).
        builder.Property(x => x.StockQuantity).IsConcurrencyToken();
        builder.Property(x => x.IsActive).HasDefaultValue(true);
        builder.Property(x => x.IsFeatured).HasDefaultValue(false);
        builder.Property(x => x.SortOrder).HasDefaultValue(0);
        builder.Property(x => x.FeaturesJson).HasColumnType("jsonb");
        builder.Property(x => x.SpecsJson).HasColumnType("jsonb");
        builder.Property(x => x.VariantsJson).HasColumnType("jsonb");

        builder.HasIndex(x => x.Slug).IsUnique();
        builder.HasIndex(x => x.Sku).IsUnique();
        // Katalog sorgulari: aktif urunler kategoriye/markaya gore filtrelenir, siraya/tarihe gore listelenir.
        builder.HasIndex(x => new { x.IsActive, x.CategoryId, x.SortOrder });
        builder.HasIndex(x => new { x.IsActive, x.Brand });
        builder.HasIndex(x => new { x.IsActive, x.CreatedAt });

        builder.HasOne<StoreCategory>()
            .WithMany()
            .HasForeignKey(x => x.CategoryId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(x => x.Images)
            .WithOne()
            .HasForeignKey(x => x.ProductId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class ProductImageConfiguration : IEntityTypeConfiguration<ProductImage>
{
    public void Configure(EntityTypeBuilder<ProductImage> builder)
    {
        builder.ToTable("ProductImage", StoreColumns.Schema);
        builder.HasKey(x => x.Id);
        builder.Property(x => x.ImageUrl).IsRequired().HasMaxLength(500);
        builder.HasIndex(x => new { x.ProductId, x.SortOrder });
        builder.HasIndex(x => x.ProductId)
            .IsUnique()
            .HasFilter("\"IsPrimary\" = TRUE")
            .HasDatabaseName("IX_ProductImage_ProductId_Primary");
    }
}

public class StoreCampaignConfiguration : IEntityTypeConfiguration<StoreCampaign>
{
    public void Configure(EntityTypeBuilder<StoreCampaign> builder)
    {
        builder.ToTable("Campaign", StoreColumns.Schema);
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Title).IsRequired().HasMaxLength(200);
        builder.Property(x => x.Description).HasMaxLength(500);
        builder.Property(x => x.ImageUrl).HasMaxLength(500);
        builder.Property(x => x.Tint).HasMaxLength(9);
        builder.Property(x => x.SortOrder).HasDefaultValue(0);
        builder.Property(x => x.IsActive).HasDefaultValue(true);
        builder.HasIndex(x => new { x.IsActive, x.SortOrder });

        builder.HasMany(x => x.Products)
            .WithOne()
            .HasForeignKey(x => x.CampaignId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class StoreCampaignProductConfiguration : IEntityTypeConfiguration<StoreCampaignProduct>
{
    public void Configure(EntityTypeBuilder<StoreCampaignProduct> builder)
    {
        builder.ToTable("CampaignProduct", StoreColumns.Schema);
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => new { x.CampaignId, x.ProductId }).IsUnique();
        builder.HasIndex(x => x.ProductId);
        builder.HasOne<Product>()
            .WithMany()
            .HasForeignKey(x => x.ProductId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class CartConfiguration : IEntityTypeConfiguration<Cart>
{
    public void Configure(EntityTypeBuilder<Cart> builder)
    {
        builder.ToTable("Cart", StoreColumns.Schema);
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => x.UserId).IsUnique();
        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(x => x.Items)
            .WithOne()
            .HasForeignKey(x => x.CartId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class CartItemConfiguration : IEntityTypeConfiguration<CartItem>
{
    public void Configure(EntityTypeBuilder<CartItem> builder)
    {
        builder.ToTable("CartItem", StoreColumns.Schema, table =>
        {
            table.HasCheckConstraint("CK_CartItem_Quantity_Positive", "\"Quantity\" > 0");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.VariantKey).IsRequired().HasMaxLength(300).HasDefaultValue(string.Empty);
        builder.Property(x => x.SelectedVariantJson).HasColumnType("jsonb");
        builder.HasIndex(x => new { x.CartId, x.ProductId, x.VariantKey }).IsUnique();
        builder.HasIndex(x => x.ProductId);
        builder.HasOne<Product>()
            .WithMany()
            .HasForeignKey(x => x.ProductId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class UserAddressConfiguration : IEntityTypeConfiguration<UserAddress>
{
    public void Configure(EntityTypeBuilder<UserAddress> builder)
    {
        builder.ToTable("Address", StoreColumns.Schema);
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Title).IsRequired().HasMaxLength(50);
        builder.Property(x => x.RecipientName).IsRequired().HasMaxLength(120);
        builder.Property(x => x.PhoneNumber).IsRequired().HasMaxLength(20);
        builder.Property(x => x.City).IsRequired().HasMaxLength(60);
        builder.Property(x => x.District).IsRequired().HasMaxLength(60);
        builder.Property(x => x.AddressLine).IsRequired().HasMaxLength(500);
        builder.Property(x => x.PostalCode).HasMaxLength(10);
        builder.HasIndex(x => x.UserId);
        builder.HasIndex(x => x.UserId)
            .IsUnique()
            .HasFilter("\"IsDefault\" = TRUE")
            .HasDatabaseName("IX_Address_UserId_Default");
        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class FavoriteProductConfiguration : IEntityTypeConfiguration<FavoriteProduct>
{
    public void Configure(EntityTypeBuilder<FavoriteProduct> builder)
    {
        builder.ToTable("Favorite", StoreColumns.Schema);
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => new { x.UserId, x.ProductId }).IsUnique();
        builder.HasIndex(x => x.ProductId);
        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Product>()
            .WithMany()
            .HasForeignKey(x => x.ProductId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class StoreOrderConfiguration : IEntityTypeConfiguration<StoreOrder>
{
    public void Configure(EntityTypeBuilder<StoreOrder> builder)
    {
        builder.ToTable("Order", StoreColumns.Schema, table =>
        {
            table.HasCheckConstraint("CK_Order_Amounts_NonNegative",
                "\"Subtotal\" >= 0 AND \"DiscountAmount\" >= 0 AND \"ShippingCost\" >= 0 AND \"TotalAmount\" >= 0");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.OrderNumber).IsRequired().HasMaxLength(32);
        builder.Property(x => x.Status).HasConversion<string>().IsRequired().HasMaxLength(20).IsConcurrencyToken();
        builder.Property(x => x.PaymentStatus).HasConversion<string>().IsRequired().HasMaxLength(20);
        builder.Property(x => x.PaymentMethod).HasConversion<string>().IsRequired().HasMaxLength(20);
        builder.Property(x => x.Subtotal).HasColumnType(StoreColumns.Money);
        builder.Property(x => x.DiscountAmount).HasColumnType(StoreColumns.Money);
        builder.Property(x => x.ShippingCost).HasColumnType(StoreColumns.Money);
        builder.Property(x => x.TotalAmount).HasColumnType(StoreColumns.Money);
        builder.Property(x => x.Currency).IsRequired().HasMaxLength(3);
        builder.Property(x => x.ShippingAddressSnapshot).IsRequired().HasColumnType("jsonb");
        builder.Property(x => x.IdempotencyKey).IsRequired().HasMaxLength(64);
        builder.Property(x => x.PaymentProvider).HasMaxLength(40);
        builder.Property(x => x.PaymentReference).HasMaxLength(120);
        builder.Property(x => x.CancelReason).HasMaxLength(300);

        builder.HasIndex(x => x.OrderNumber).IsUnique();
        builder.HasIndex(x => new { x.UserId, x.IdempotencyKey }).IsUnique();
        builder.HasIndex(x => new { x.UserId, x.CreatedAt });
        // Admin listesi: durum/odeme durumu ve tarihe gore filtrelenir.
        builder.HasIndex(x => new { x.Status, x.CreatedAt });
        builder.HasIndex(x => new { x.PaymentStatus, x.CreatedAt });

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(x => x.Items)
            .WithOne()
            .HasForeignKey(x => x.OrderId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class StoreOrderItemConfiguration : IEntityTypeConfiguration<StoreOrderItem>
{
    public void Configure(EntityTypeBuilder<StoreOrderItem> builder)
    {
        builder.ToTable("OrderItem", StoreColumns.Schema, table =>
        {
            table.HasCheckConstraint("CK_OrderItem_Quantity_Positive", "\"Quantity\" > 0");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.ProductNameSnapshot).IsRequired().HasMaxLength(200);
        builder.Property(x => x.SkuSnapshot).IsRequired().HasMaxLength(64);
        builder.Property(x => x.BrandSnapshot).IsRequired().HasMaxLength(100);
        builder.Property(x => x.ImageUrlSnapshot).HasMaxLength(500);
        builder.Property(x => x.VariantLabel).HasMaxLength(200);
        builder.Property(x => x.VariantKey).IsRequired().HasMaxLength(300).HasDefaultValue(string.Empty);
        builder.Property(x => x.UnitListPrice).HasColumnType(StoreColumns.Money);
        builder.Property(x => x.UnitPrice).HasColumnType(StoreColumns.Money);
        builder.Property(x => x.DiscountAmount).HasColumnType(StoreColumns.Money);
        builder.Property(x => x.LineTotal).HasColumnType(StoreColumns.Money);
        builder.HasIndex(x => x.OrderId);
        // "Cok satanlar" siralamasi urun bazinda satilan adedi toplar.
        builder.HasIndex(x => x.ProductId);
        builder.HasOne<Product>()
            .WithMany()
            .HasForeignKey(x => x.ProductId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

public class StoreOrderStatusHistoryConfiguration : IEntityTypeConfiguration<StoreOrderStatusHistory>
{
    public void Configure(EntityTypeBuilder<StoreOrderStatusHistory> builder)
    {
        builder.ToTable("OrderStatusHistory", StoreColumns.Schema);
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Status).HasConversion<string>().IsRequired().HasMaxLength(20);
        builder.Property(x => x.PaymentStatus).HasConversion<string>().IsRequired().HasMaxLength(20);
        builder.Property(x => x.ChangedBy).IsRequired().HasMaxLength(20);
        builder.Property(x => x.Note).HasMaxLength(300);
        builder.HasIndex(x => new { x.OrderId, x.CreatedAt });
        builder.HasOne<StoreOrder>()
            .WithMany()
            .HasForeignKey(x => x.OrderId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
