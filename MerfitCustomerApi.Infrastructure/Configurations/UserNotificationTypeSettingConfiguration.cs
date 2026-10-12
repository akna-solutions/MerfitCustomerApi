using MerfitCustomerApi.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MerfitCustomerApi.Infrastructure.Configurations;

/// <summary>
/// UserNotificationTypeSetting varliginin veritabani (Entity Framework Core) yapilandirmasini tanimlar.
/// </summary>
public class UserNotificationTypeSettingConfiguration : IEntityTypeConfiguration<UserNotificationTypeSetting>
{
    public void Configure(EntityTypeBuilder<UserNotificationTypeSetting> builder)
    {
        builder.ToTable("UserNotificationTypeSetting", "profile");

        builder.HasKey(x => x.Id);

        // Notification.Type ile ayni sekilde metin olarak saklanir.
        builder.Property(x => x.Type)
            .HasConversion<string>()
            .IsRequired(true)
            .HasMaxLength(50);

        // Bir kullanicinin her tur icin tek bir ayari olur.
        builder.HasIndex(x => new { x.UserId, x.Type }).IsUnique();

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
