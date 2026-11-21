using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MerfitCustomerApi.Domain.Entities;

namespace MerfitCustomerApi.Infrastructure.Configurations;

/// <summary>
/// ExerciseEquipment (egzersiz - gereken ekipman) yapilandirmasi.
/// </summary>
public class ExerciseEquipmentConfiguration : IEntityTypeConfiguration<ExerciseEquipment>
{
    public void Configure(EntityTypeBuilder<ExerciseEquipment> builder)
    {
        builder.ToTable("ExerciseEquipment", "workout");
        builder.HasKey(x => x.Id);

        builder.HasIndex(x => new { x.ExerciseId, x.EquipmentId })
            .IsUnique();

        builder.HasOne<Exercise>()
            .WithMany()
            .HasForeignKey(x => x.ExerciseId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<Equipment>()
            .WithMany()
            .HasForeignKey(x => x.EquipmentId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
