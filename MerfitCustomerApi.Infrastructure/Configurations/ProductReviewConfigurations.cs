using MerfitCustomerApi.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MerfitCustomerApi.Infrastructure.Configurations;

// MB Shop urun yorumlari ("store" semasi). Veritabani seviyesindeki garantiler:
// - Kullanici basina urun basina tek aktif (Deleted olmayan) yorum: filtreli unique index.
// - Ayni kullanici ayni yoruma tek oy ve tek rapor verebilir: unique index.
// - Puan 1-5, oy sayaclari negatif olamaz: CHECK.
// - Ozet (ortalama + yildiz dagilimi) (ProductId, Status, Rating) indeksinden index-only scan ile hesaplanir.
// MerfitApi (admin) ayni tablolari kendi kopya yapilandirmasiyla okur/yazar; migration yalnizca bu projededir.

public class ProductReviewConfiguration : IEntityTypeConfiguration<ProductReview>
{
    public void Configure(EntityTypeBuilder<ProductReview> builder)
    {
        builder.ToTable("ProductReview", StoreColumns.Schema, table =>
        {
            table.HasCheckConstraint("CK_ProductReview_Rating_Range", "\"Rating\" BETWEEN 1 AND 5");
            table.HasCheckConstraint("CK_ProductReview_VoteCounts_NonNegative", "\"HelpfulCount\" >= 0 AND \"NotHelpfulCount\" >= 0");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Title).HasMaxLength(100);
        builder.Property(x => x.Comment).IsRequired().HasMaxLength(2000);
        builder.Property(x => x.Status).HasConversion<string>().IsRequired().HasMaxLength(20);
        builder.Property(x => x.IsVerifiedPurchase).HasDefaultValue(false);
        builder.Property(x => x.HelpfulCount).HasDefaultValue(0);
        builder.Property(x => x.NotHelpfulCount).HasDefaultValue(0);
        builder.Property(x => x.ModerationNote).HasMaxLength(500);

        builder.HasIndex(x => new { x.ProductId, x.UserId })
            .IsUnique()
            .HasFilter("\"Status\" <> 'Deleted'")
            .HasDatabaseName("IX_ProductReview_ProductId_UserId_Active");
        builder.HasIndex(x => new { x.ProductId, x.Status, x.Rating });
        builder.HasIndex(x => new { x.ProductId, x.Status, x.CreatedAt });
        builder.HasIndex(x => new { x.ProductId, x.Status, x.HelpfulCount });
        builder.HasIndex(x => new { x.Status, x.CreatedAt });
        builder.HasIndex(x => x.UserId);

        builder.HasOne<Product>()
            .WithMany()
            .HasForeignKey(x => x.ProductId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public class ProductReviewImageConfiguration : IEntityTypeConfiguration<ProductReviewImage>
{
    public void Configure(EntityTypeBuilder<ProductReviewImage> builder)
    {
        builder.ToTable("ProductReviewImage", StoreColumns.Schema, table =>
        {
            table.HasCheckConstraint("CK_ProductReviewImage_ByteSize_Positive", "\"ByteSize\" > 0");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.StorageKey).IsRequired().HasMaxLength(32);
        builder.Property(x => x.ContentType).IsRequired().HasMaxLength(20);
        builder.Property(x => x.Content).IsRequired().HasColumnType("bytea");
        builder.HasIndex(x => x.StorageKey).IsUnique();
        builder.HasIndex(x => new { x.ReviewId, x.SortOrder });
        builder.HasOne<ProductReview>()
            .WithMany()
            .HasForeignKey(x => x.ReviewId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class ProductReviewVoteConfiguration : IEntityTypeConfiguration<ProductReviewVote>
{
    public void Configure(EntityTypeBuilder<ProductReviewVote> builder)
    {
        builder.ToTable("ProductReviewVote", StoreColumns.Schema);
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => new { x.ReviewId, x.UserId }).IsUnique();
        builder.HasIndex(x => new { x.ReviewId, x.IsHelpful });
        builder.HasIndex(x => x.UserId);
        builder.HasOne<ProductReview>()
            .WithMany()
            .HasForeignKey(x => x.ReviewId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public class ProductReviewReportConfiguration : IEntityTypeConfiguration<ProductReviewReport>
{
    public void Configure(EntityTypeBuilder<ProductReviewReport> builder)
    {
        builder.ToTable("ProductReviewReport", StoreColumns.Schema);
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Reason).HasConversion<string>().IsRequired().HasMaxLength(20);
        builder.Property(x => x.Status).HasConversion<string>().IsRequired().HasMaxLength(20);
        builder.Property(x => x.Description).HasMaxLength(500);
        builder.HasIndex(x => new { x.ReviewId, x.ReporterUserId }).IsUnique();
        builder.HasIndex(x => new { x.Status, x.CreatedAt });
        builder.HasIndex(x => new { x.ReviewId, x.Status });
        builder.HasIndex(x => x.ReporterUserId);
        builder.HasOne<ProductReview>()
            .WithMany()
            .HasForeignKey(x => x.ReviewId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(x => x.ReporterUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
