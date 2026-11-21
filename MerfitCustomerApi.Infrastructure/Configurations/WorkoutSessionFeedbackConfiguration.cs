using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MerfitCustomerApi.Domain.Entities;

namespace MerfitCustomerApi.Infrastructure.Configurations;

/// <summary>
/// WorkoutSessionFeedback (antrenman sonu kisa degerlendirme) yapilandirmasi.
/// </summary>
public class WorkoutSessionFeedbackConfiguration : IEntityTypeConfiguration<WorkoutSessionFeedback>
{
    public void Configure(EntityTypeBuilder<WorkoutSessionFeedback> builder)
    {
        var min = WorkoutSessionFeedback.MinScore;
        var max = WorkoutSessionFeedback.MaxScore;
        builder.ToTable("WorkoutSessionFeedback", "workout", t =>
        {
            t.HasCheckConstraint("CK_WorkoutSessionFeedback_Difficulty", $"\"Difficulty\" IS NULL OR (\"Difficulty\" >= {min} AND \"Difficulty\" <= {max})");
            t.HasCheckConstraint("CK_WorkoutSessionFeedback_Fatigue", $"\"Fatigue\" IS NULL OR (\"Fatigue\" >= {min} AND \"Fatigue\" <= {max})");
            t.HasCheckConstraint("CK_WorkoutSessionFeedback_Recovery", $"\"Recovery\" IS NULL OR (\"Recovery\" >= {min} AND \"Recovery\" <= {max})");
            t.HasCheckConstraint("CK_WorkoutSessionFeedback_Satisfaction", $"\"Satisfaction\" IS NULL OR (\"Satisfaction\" >= {min} AND \"Satisfaction\" <= {max})");
        });
        builder.HasKey(x => x.Id);

        builder.Property(x => x.DiscomfortExerciseIds).IsRequired(false).HasMaxLength(500);
        builder.Property(x => x.Note).IsRequired(false).HasMaxLength(500);

        // Oturum basina tek degerlendirme (ikinci gonderim guncelleme olur).
        builder.HasIndex(x => x.WorkoutSessionId)
            .IsUnique();

        // Kullanicinin son geri bildirimleri icin.
        builder.HasIndex(x => new { x.UserId, x.CreatedAt });

        builder.HasOne<WorkoutSession>()
            .WithMany()
            .HasForeignKey(x => x.WorkoutSessionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
