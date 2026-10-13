using MerfitCustomerApi.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MerfitCustomerApi.Infrastructure.Configurations;

/// <summary>
/// UserAccountToken (e-posta dogrulama / sifre sifirlama jetonu) veritabani yapilandirmasi.
/// - UsedAt ve AttemptCount iyimser eszamanlilik belirtecidir; ayni kod iki kez kullanilamaz ve paralel
///   isteklerle deneme siniri asilamaz.
/// - (UserId, Purpose, CreatedAt) indeksi tekrar gonderim bekleme suresi ve saatlik sinir sorgulari ile
///   kullanicinin aktif kodunun bulunmasi icindir.
/// </summary>
public class UserAccountTokenConfiguration : IEntityTypeConfiguration<UserAccountToken>
{
    public void Configure(EntityTypeBuilder<UserAccountToken> builder)
    {
        builder.ToTable("UserAccountToken", "authentication", table =>
        {
            table.HasCheckConstraint("CK_UserAccountToken_Purpose", "\"Purpose\" IN ('EmailVerification', 'PasswordReset')");
        });

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Purpose)
            .HasConversion<string>()
            .IsRequired()
            .HasMaxLength(30);

        builder.Property(x => x.TokenHash)
            .IsRequired()
            .HasMaxLength(64);

        builder.Property(x => x.NormalizedEmail)
            .IsRequired()
            .HasMaxLength(500);

        builder.Property(x => x.UsedAt)
            .IsConcurrencyToken();

        builder.Property(x => x.AttemptCount)
            .HasDefaultValue(0)
            .IsConcurrencyToken();

        builder.Property(x => x.RequestedByIp)
            .HasMaxLength(300);

        builder.HasIndex(x => new { x.UserId, x.Purpose, x.CreatedAt });

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
