using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MerfitCustomerApi.Domain.Entities;

namespace MerfitCustomerApi.Infrastructure.Configurations;

/// <summary>
/// UserEquipment varliginin veritabani (Entity Framework Core) yapilandirmasini tanimlar.
/// </summary>
public class UserEquipmentConfiguration : IEntityTypeConfiguration<UserEquipment>
{
    /// <summary>
    /// UserEquipment varligi icin tablo adi, birincil anahtar, alan kisitlari ve iliskileri yapilandirir.
    /// </summary>
    public void Configure(EntityTypeBuilder<UserEquipment> builder)
    {
        // Tablo adini belirtir.
        builder.ToTable("UserEquipment", "profile");

        // Birincil anahtari tanimlar (BaseEntity.Id).
        builder.HasKey(x => x.Id);

        // Ayni kullanici-ekipman ciftinin birden fazla kez eklenmesini engeller.
        builder.HasIndex(x => new { x.UserId, x.EquipmentId })
            .IsUnique();

        // Iliskiler (foreign key) yapilandirmasi.
        // Iliskili kullanicinin kimligi.
        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        // Iliskili ekipmanin kimligi.
        builder.HasOne<Equipment>()
            .WithMany()
            .HasForeignKey(x => x.EquipmentId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}