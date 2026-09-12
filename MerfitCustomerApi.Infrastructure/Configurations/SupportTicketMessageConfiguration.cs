using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MerfitCustomerApi.Domain.Entities;

namespace MerfitCustomerApi.Infrastructure.Configurations;

/// <summary>
/// SupportTicketMessage varliginin veritabani (Entity Framework Core) yapilandirmasini tanimlar.
/// </summary>
public class SupportTicketMessageConfiguration : IEntityTypeConfiguration<SupportTicketMessage>
{
    /// <summary>
    /// SupportTicketMessage varligi icin tablo adi, birincil anahtar, alan kisitlari ve iliskileri yapilandirir.
    /// </summary>
    public void Configure(EntityTypeBuilder<SupportTicketMessage> builder)
    {
        // Tablo adini belirtir.
        builder.ToTable("SupportTicketMessage", "support");

        // Birincil anahtari tanimlar.
        builder.HasKey(x => x.Id);

        // Mesaj icerigi.
        builder.Property(x => x.Message)
            .IsRequired(true)
            .HasMaxLength(2000);

        // Iliskiler (foreign key) yapilandirmasi.
        // Iliskili destek talebinin kimligi.
        builder.HasOne<SupportTicket>()
            .WithMany()
            .HasForeignKey(x => x.TicketId)
            .OnDelete(DeleteBehavior.Restrict);

        // Mesaji gonderen kullanicinin kimligi.
        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(x => x.SenderUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
