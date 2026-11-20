using MerfitCustomerApi.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MerfitCustomerApi.Infrastructure.Configurations;

/// <summary>
/// DailyActivity tablosu. Kullanici + gun benzersizdir (mukerrer gun kaydi olusamaz; eszamanli iki
/// senkronizasyondan biri benzersiz index'e takilir ve servis tekrar dener). Saatlik adimlar PostgreSQL
/// integer[] olarak tutulur; check constraint'ler negatif adimi ve 24 disi diziyi veritabani seviyesinde engeller.
/// </summary>
public class DailyActivityConfiguration : IEntityTypeConfiguration<DailyActivity>
{
    public void Configure(EntityTypeBuilder<DailyActivity> builder)
    {
        builder.ToTable("DailyActivity", "activity", table =>
        {
            table.HasCheckConstraint("CK_DailyActivity_NonNegative",
                "\"TotalSteps\" >= 0 AND \"DistanceMeters\" >= 0 AND \"CaloriesBurned\" >= 0 AND \"DailyGoal\" > 0");
            table.HasCheckConstraint("CK_DailyActivity_HourlySteps_Length", "cardinality(\"HourlySteps\") = 24");
        });

        builder.HasKey(x => x.Id);

        builder.Property(x => x.HourlySteps).IsRequired();

        // PostgreSQL'in sistem kolonu xmin: ek kolon gerektirmez, her guncellemede degisir.
        builder.Property(x => x.Version).IsRowVersion();

        builder.Property(x => x.Source)
            .IsRequired()
            .HasMaxLength(30);

        builder.Property(x => x.Platform)
            .HasMaxLength(20);

        builder.Property(x => x.SuspiciousReason)
            .HasMaxLength(100);

        // Tekil gun kaydi + tarih araligi sorgulari (UserId, Date) ayni index'i kullanir.
        builder.HasIndex(x => new { x.UserId, x.Date })
            .IsUnique();

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
