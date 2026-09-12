using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MerfitCustomerApi.Domain.Entities;

namespace MerfitCustomerApi.Infrastructure.Configurations;

/// <summary>
/// Achievement varliginin veritabani (Entity Framework Core) yapilandirmasini tanimlar.
/// </summary>
public class AchievementConfiguration : IEntityTypeConfiguration<Achievement>
{
    /// <summary>
    /// Achievement varligi icin tablo adi, birincil anahtar, alan kisitlari ve iliskileri yapilandirir.
    /// </summary>
    public void Configure(EntityTypeBuilder<Achievement> builder)
    {
        // Tablo adini belirtir.
        builder.ToTable("Achievement", "gamification");

        // Birincil anahtari tanimlar.
        builder.HasKey(x => x.Id);

        // Basarinin sistemsel kodu (orn. FIRST_WORKOUT).
        builder.Property(x => x.Code)
            .IsRequired(true)
            .HasMaxLength(200);

        // Basarinin baslikta gosterilen adi.
        builder.Property(x => x.Title)
            .IsRequired(true)
            .HasMaxLength(200);

        // Basarinin aciklamasi.
        builder.Property(x => x.Description)
            .IsRequired(false)
            .HasMaxLength(2000);

        // Basari icin kullanilan ikon/gorsel adresi.
        builder.Property(x => x.Icon)
            .IsRequired(false)
            .HasMaxLength(500);

        // Basarinin kazanilma kosulunun turu.
        builder.Property(x => x.ConditionType)
            .IsRequired(true)
            .HasMaxLength(2000);

        // Basarinin kazanilma kosuluna ait deger.
        builder.Property(x => x.ConditionValue)
            .IsRequired(true)
            .HasMaxLength(2000);
    }
}
