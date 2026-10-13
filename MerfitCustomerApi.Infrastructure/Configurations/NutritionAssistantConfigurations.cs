using MerfitCustomerApi.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MerfitCustomerApi.Infrastructure.Configurations;

/// <summary>
/// Beslenme asistani sohbeti. Kullanici ve gun basina tek sohbet veritabaninda garanti edilir; ayni anda
/// gelen iki ilk mesajdan biri benzersiz index ihlali alir ve mevcut sohbeti kullanir.
/// </summary>
public class NutritionAssistantConversationConfiguration : IEntityTypeConfiguration<NutritionAssistantConversation>
{
    public const string UserDateIndexName = "UX_NutritionAssistantConversation_UserId_Date";

    public void Configure(EntityTypeBuilder<NutritionAssistantConversation> builder)
    {
        builder.ToTable("NutritionAssistantConversation", "ai");

        builder.HasKey(x => x.Id);

        builder.HasIndex(x => new { x.UserId, x.Date })
            .IsUnique()
            .HasDatabaseName(UserDateIndexName);

        // Saklama suresi temizligi.
        builder.HasIndex(x => x.LastMessageAt);

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

/// <summary>Beslenme asistani mesaji. Sohbet silinince mesajlari da silinir.</summary>
public class NutritionAssistantMessageConfiguration : IEntityTypeConfiguration<NutritionAssistantMessage>
{
    public void Configure(EntityTypeBuilder<NutritionAssistantMessage> builder)
    {
        builder.ToTable("NutritionAssistantMessage", "ai");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Role)
            .HasConversion<string>()
            .IsRequired()
            .HasMaxLength(20);

        builder.Property(x => x.Content)
            .IsRequired()
            .HasMaxLength(8000);

        // Sohbet gecmisi ve kullanici basina gunluk mesaj limiti sorgulari.
        builder.HasIndex(x => new { x.ConversationId, x.CreatedAt });

        builder.HasOne<NutritionAssistantConversation>()
            .WithMany()
            .HasForeignKey(x => x.ConversationId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
