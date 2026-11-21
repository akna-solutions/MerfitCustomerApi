using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MerfitCustomerApi.Domain.Entities;

namespace MerfitCustomerApi.Infrastructure.Configurations;

/// <summary>
/// UserTrainingPreference (antrenman motoru tercihleri) yapilandirmasi. Profil semasinda tutulur.
/// </summary>
public class UserTrainingPreferenceConfiguration : IEntityTypeConfiguration<UserTrainingPreference>
{
    public void Configure(EntityTypeBuilder<UserTrainingPreference> builder)
    {
        builder.ToTable("UserTrainingPreference", "profile");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Focus)
            .HasConversion<string>()
            .IsRequired(false)
            .HasMaxLength(30);

        builder.ToTable(t => t.HasCheckConstraint("CK_UserTrainingPreference_DaysPerWeek", "\"DaysPerWeek\" IS NULL OR (\"DaysPerWeek\" >= 2 AND \"DaysPerWeek\" <= 7)"));

        builder.Property(x => x.PriorityMuscleGroupIds).IsRequired(false).HasMaxLength(500);
        builder.Property(x => x.PreferredExerciseIds).IsRequired(false).HasMaxLength(1000);
        builder.Property(x => x.AvoidedExerciseIds).IsRequired(false).HasMaxLength(1000);
        builder.Property(x => x.LimitedMuscleGroupIds).IsRequired(false).HasMaxLength(500);
        builder.Property(x => x.LimitationsNote).IsRequired(false).HasMaxLength(500);

        // Kullanici basina tek kayit.
        builder.HasIndex(x => x.UserId)
            .IsUnique();

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
