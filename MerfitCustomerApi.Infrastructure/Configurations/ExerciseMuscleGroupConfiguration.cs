using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MerfitCustomerApi.Domain.Entities;

namespace MerfitCustomerApi.Infrastructure.Configurations;

/// <summary>
/// ExerciseMuscleGroup (egzersiz - kas grubu, ozellikle yardimci kaslar) yapilandirmasi.
/// </summary>
public class ExerciseMuscleGroupConfiguration : IEntityTypeConfiguration<ExerciseMuscleGroup>
{
    public void Configure(EntityTypeBuilder<ExerciseMuscleGroup> builder)
    {
        builder.ToTable("ExerciseMuscleGroup", "workout");
        builder.HasKey(x => x.Id);

        // Rol metin olarak saklanir.
        builder.Property(x => x.Role)
            .HasConversion<string>()
            .IsRequired(true)
            .HasMaxLength(20);

        // Ayni kas bir egzersize bir kez baglanir.
        builder.HasIndex(x => new { x.ExerciseId, x.MuscleGroupId })
            .IsUnique();

        // Egzersiz silinirse (admin API fiziksel silme yapabilir) baglanti satirlari da silinir.
        builder.HasOne<Exercise>()
            .WithMany()
            .HasForeignKey(x => x.ExerciseId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<MuscleGroup>()
            .WithMany()
            .HasForeignKey(x => x.MuscleGroupId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
