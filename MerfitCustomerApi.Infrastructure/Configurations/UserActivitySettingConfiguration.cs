using MerfitCustomerApi.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MerfitCustomerApi.Infrastructure.Configurations;

/// <summary>UserActivitySetting tablosu: kullanici basina tek satir, hedef 1.000-100.000 araliginda.</summary>
public class UserActivitySettingConfiguration : IEntityTypeConfiguration<UserActivitySetting>
{
    public void Configure(EntityTypeBuilder<UserActivitySetting> builder)
    {
        builder.ToTable("UserActivitySetting", "activity", table =>
        {
            table.HasCheckConstraint("CK_UserActivitySetting_DailyStepGoal", "\"DailyStepGoal\" BETWEEN 1000 AND 100000");
        });

        builder.HasKey(x => x.Id);

        builder.HasIndex(x => x.UserId).IsUnique();

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
