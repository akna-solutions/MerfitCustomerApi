using MerfitCustomerApi.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MerfitCustomerApi.Infrastructure.Configurations;

/// <summary>
/// WorkoutPlanSimulation (Antrenman Laboratuvari) varliginin veritabani yapilandirmasi. Kullanici tablolarina
/// hicbir foreign key tanimlanmaz; tek iliski, tekrar calistirilan teste (ayni tablo) SET NULL baglantisidir.
/// </summary>
public class WorkoutPlanSimulationConfiguration : IEntityTypeConfiguration<WorkoutPlanSimulation>
{
    public void Configure(EntityTypeBuilder<WorkoutPlanSimulation> builder)
    {
        builder.ToTable("WorkoutPlanSimulation", "workout");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Name).HasMaxLength(200);

        builder.Property(x => x.Status)
            .HasConversion<string>()
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(x => x.ProfileJson).IsRequired().HasColumnType("jsonb");
        builder.Property(x => x.ResultJson).HasColumnType("jsonb");
        builder.Property(x => x.QualityChecksJson).HasColumnType("jsonb");
        builder.Property(x => x.EvaluationProblemItemsJson).HasColumnType("jsonb");

        builder.Property(x => x.Goal).HasConversion<string>().IsRequired().HasMaxLength(50);
        builder.Property(x => x.ExperienceLevel).HasConversion<string>().IsRequired().HasMaxLength(50);
        builder.Property(x => x.TrainingLocation).HasConversion<string>().IsRequired().HasMaxLength(50);
        builder.Property(x => x.Rating).HasConversion<string>().HasMaxLength(50);

        builder.Property(x => x.AlgorithmVersion).HasMaxLength(50);
        builder.Property(x => x.OptionsFingerprint).HasMaxLength(64);
        builder.Property(x => x.ErrorMessage).HasMaxLength(2000);
        builder.Property(x => x.EvaluationMissingPoints).HasMaxLength(4000);
        builder.Property(x => x.EvaluationExerciseSuggestions).HasMaxLength(4000);
        builder.Property(x => x.EvaluationComment).HasMaxLength(4000);

        builder.Property(x => x.AttemptCount).IsRequired().HasDefaultValue(0);
        builder.Property(x => x.PassedCheckCount).IsRequired().HasDefaultValue(0);
        builder.Property(x => x.WarningCheckCount).IsRequired().HasDefaultValue(0);
        builder.Property(x => x.FailedCheckCount).IsRequired().HasDefaultValue(0);

        // Kuyruk: islenmeyi bekleyen en eski kayit.
        builder.HasIndex(x => new { x.Status, x.Id });

        // Gecmis testler listesi (en yeni once) ve surum filtresi.
        builder.HasIndex(x => x.CreatedAt);
        builder.HasIndex(x => x.AlgorithmVersion);

        builder.HasOne<WorkoutPlanSimulation>()
            .WithMany()
            .HasForeignKey(x => x.SourceSimulationId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
