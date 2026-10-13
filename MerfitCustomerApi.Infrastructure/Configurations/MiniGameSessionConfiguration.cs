using MerfitCustomerApi.Domain.Entities;
using MerfitCustomerApi.Domain.Entities.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MerfitCustomerApi.Infrastructure.Configurations;

/// <summary>
/// MiniGameSession varliginin veritabani yapilandirmasi. Suistimale karsi veritabani seviyesindeki
/// garantiler burada tanimlanir:
/// - Kullanici basina en fazla bir "Started" oturum (filtreli benzersiz index). Boylece iki farkli oturum
///   ayni anda bitirilip gunluk limit yarisla asilamaz.
/// - Status iyimser eszamanlilik belirtecidir: ayni oturumun iki kez odullendirilmesi engellenir.
/// </summary>
public class MiniGameSessionConfiguration : IEntityTypeConfiguration<MiniGameSession>
{
    public void Configure(EntityTypeBuilder<MiniGameSession> builder)
    {
        builder.ToTable("MiniGameSession", "gamification", table =>
        {
            table.HasCheckConstraint("CK_MiniGameSession_Points_NonNegative", "\"TierPoints\" >= 0 AND \"AwardedPoints\" >= 0 AND \"AwardedPoints\" <= \"TierPoints\"");
        });

        builder.HasKey(x => x.Id);

        builder.Property(x => x.GameKey)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(x => x.Status)
            .HasConversion<string>()
            .IsRequired()
            .HasMaxLength(20)
            .IsConcurrencyToken();

        builder.Property(x => x.RejectReason)
            .HasMaxLength(100);

        // Tek aktif oturum kurali.
        builder.HasIndex(x => x.UserId)
            .IsUnique()
            .HasFilter($"\"Status\" = '{nameof(MiniGameSessionStatus.Started)}'")
            .HasDatabaseName("IX_MiniGameSession_UserId_Active");

        // Gunluk limit ve puan olayi sorgulari.
        builder.HasIndex(x => new { x.UserId, x.StartedAt });
        builder.HasIndex(x => new { x.Status, x.CompletedAt });

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
