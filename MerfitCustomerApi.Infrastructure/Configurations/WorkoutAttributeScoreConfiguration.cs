using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MerfitCustomerApi.Domain.Entities;

namespace MerfitCustomerApi.Infrastructure.Configurations;

/// <summary>
/// WorkoutAttributeScore varliginin veritabani (Entity Framework Core) yapilandirmasini tanimlar.
/// </summary>
public class WorkoutAttributeScoreConfiguration : IEntityTypeConfiguration<WorkoutAttributeScore>
{
    /// <summary>
    /// WorkoutAttributeScore varligi icin tablo adi, birincil anahtar, alan kisitlari ve iliskileri yapilandirir.
    /// </summary>
    public void Configure(EntityTypeBuilder<WorkoutAttributeScore> builder)
    {
        // Tablo adini ve puan araligini veritabani seviyesinde garanti eden check constraint'i belirtir.
        builder.ToTable("WorkoutAttributeScore", "workout", t => t.HasCheckConstraint(
            "CK_WorkoutAttributeScore_Score_Range",
            $"\"Score\" >= {WorkoutAttributeScore.MinScore} AND \"Score\" <= {WorkoutAttributeScore.MaxScore}"));

        // Birincil anahtari tanimlar.
        builder.HasKey(x => x.Id);

        // Puanlanan ozellik turu (veritabaninda okunabilir olmasi icin metin olarak saklanir).
        builder.Property(x => x.AttributeType)
            .HasConversion<string>()
            .IsRequired(true)
            .HasMaxLength(50);

        // Ozellik puani (0-100).
        builder.Property(x => x.Score)
            .IsRequired(true);

        // Bir antrenman icin ayni ozellik turunun birden fazla kez eklenmesini engeller.
        builder.HasIndex(x => new { x.WorkoutId, x.AttributeType })
            .IsUnique();

        // Iliskiler (foreign key) yapilandirmasi.
        // Puanlar antrenmanin parcasidir; antrenman silindiginde (orn. bu tabloyu tanimayan admin
        // API'si tarafindan) puanlar da veritabani seviyesinde silinir ve silme islemi engellenmez.
        builder.HasOne<Workout>()
            .WithMany()
            .HasForeignKey(x => x.WorkoutId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
