using MerfitCustomerApi.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MerfitCustomerApi.Infrastructure.Configurations;

/// <summary>NotificationTemplate tablo yapilandirmasi.</summary>
public class NotificationTemplateConfiguration : IEntityTypeConfiguration<NotificationTemplate>
{
    public void Configure(EntityTypeBuilder<NotificationTemplate> builder)
    {
        builder.ToTable("NotificationTemplate", "notification");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Name).IsRequired().HasMaxLength(150);
        builder.Property(x => x.Type).HasConversion<string>().IsRequired().HasMaxLength(50);
        builder.Property(x => x.TitleTemplate).IsRequired().HasMaxLength(200);
        builder.Property(x => x.BodyTemplate).IsRequired().HasMaxLength(500);
        builder.Property(x => x.DataJson).IsRequired(false).HasMaxLength(2000);
        builder.Property(x => x.IsActive).IsRequired();

        builder.HasIndex(x => x.Name).IsUnique();
    }
}

/// <summary>NotificationRule tablo yapilandirmasi.</summary>
public class NotificationRuleConfiguration : IEntityTypeConfiguration<NotificationRule>
{
    public void Configure(EntityTypeBuilder<NotificationRule> builder)
    {
        builder.ToTable("NotificationRule", "notification", t =>
            t.HasCheckConstraint("CK_NotificationRule_DaysOfWeekMask", "\"DaysOfWeekMask\" >= 0 AND \"DaysOfWeekMask\" <= 127"));
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Name).IsRequired().HasMaxLength(150);
        builder.Property(x => x.Description).IsRequired(false).HasMaxLength(500);
        builder.Property(x => x.Kind).HasConversion<string>().IsRequired().HasMaxLength(50);
        builder.Property(x => x.Status).HasConversion<string>().IsRequired().HasMaxLength(50);
        builder.Property(x => x.MealType).HasConversion<string>().IsRequired(false).HasMaxLength(50);
        builder.Property(x => x.DaysOfWeekMask).IsRequired();
        builder.Property(x => x.OnlyUsersWithPlan).IsRequired();

        builder.HasIndex(x => x.Status);
    }
}

/// <summary>NotificationRuleTemplate tablo yapilandirmasi.</summary>
public class NotificationRuleTemplateConfiguration : IEntityTypeConfiguration<NotificationRuleTemplate>
{
    public void Configure(EntityTypeBuilder<NotificationRuleTemplate> builder)
    {
        builder.ToTable("NotificationRuleTemplate", "notification");
        builder.HasKey(x => x.Id);

        builder.HasIndex(x => new { x.NotificationRuleId, x.NotificationTemplateId }).IsUnique();

        builder.HasOne<NotificationRule>()
            .WithMany()
            .HasForeignKey(x => x.NotificationRuleId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<NotificationTemplate>()
            .WithMany()
            .HasForeignKey(x => x.NotificationTemplateId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>NotificationCampaign tablo yapilandirmasi.</summary>
public class NotificationCampaignConfiguration : IEntityTypeConfiguration<NotificationCampaign>
{
    public void Configure(EntityTypeBuilder<NotificationCampaign> builder)
    {
        builder.ToTable("NotificationCampaign", "notification");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Name).IsRequired().HasMaxLength(150);
        builder.Property(x => x.Type).HasConversion<string>().IsRequired().HasMaxLength(50);
        builder.Property(x => x.Status).HasConversion<string>().IsRequired().HasMaxLength(50);
        builder.Property(x => x.Segment).HasConversion<string>().IsRequired().HasMaxLength(50);
        builder.Property(x => x.Title).IsRequired().HasMaxLength(200);
        builder.Property(x => x.Body).IsRequired().HasMaxLength(500);
        builder.Property(x => x.DataJson).IsRequired(false).HasMaxLength(2000);
        builder.Property(x => x.LastError).IsRequired(false).HasMaxLength(1000);
        builder.Property(x => x.AttemptCount).IsRequired().HasDefaultValue(0);

        // Zamani gelen kampanyalari bulmak icin.
        builder.HasIndex(x => new { x.Status, x.ScheduledAtUtc });

        builder.HasOne<NotificationTemplate>()
            .WithMany()
            .HasForeignKey(x => x.NotificationTemplateId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

/// <summary>NotificationCampaignUser tablo yapilandirmasi.</summary>
public class NotificationCampaignUserConfiguration : IEntityTypeConfiguration<NotificationCampaignUser>
{
    public void Configure(EntityTypeBuilder<NotificationCampaignUser> builder)
    {
        builder.ToTable("NotificationCampaignUser", "notification");
        builder.HasKey(x => x.Id);

        builder.HasIndex(x => new { x.NotificationCampaignId, x.UserId }).IsUnique();

        builder.HasOne<NotificationCampaign>()
            .WithMany()
            .HasForeignKey(x => x.NotificationCampaignId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

/// <summary>NotificationDispatch (gonderim kuyrugu) tablo yapilandirmasi.</summary>
public class NotificationDispatchConfiguration : IEntityTypeConfiguration<NotificationDispatch>
{
    public void Configure(EntityTypeBuilder<NotificationDispatch> builder)
    {
        builder.ToTable("NotificationDispatch", "notification");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Type).HasConversion<string>().IsRequired().HasMaxLength(50);
        builder.Property(x => x.Source).HasConversion<string>().IsRequired().HasMaxLength(50);
        builder.Property(x => x.Status).HasConversion<string>().IsRequired().HasMaxLength(50);
        builder.Property(x => x.StatusReason).IsRequired(false).HasMaxLength(50);
        builder.Property(x => x.DedupeKey).IsRequired().HasMaxLength(200);
        builder.Property(x => x.Title).IsRequired().HasMaxLength(200);
        builder.Property(x => x.Body).IsRequired().HasMaxLength(500);
        builder.Property(x => x.DataJson).IsRequired(false).HasMaxLength(2000);
        builder.Property(x => x.LastError).IsRequired(false).HasMaxLength(1000);
        builder.Property(x => x.AttemptCount).IsRequired().HasDefaultValue(0);

        // Idempotency: ayni kullanici/kaynak/donem icin ikinci kayit veritabaninda reddedilir.
        builder.HasIndex(x => x.DedupeKey).IsUnique();
        // Gondericinin sahiplenme sorgusu.
        builder.HasIndex(x => new { x.Status, x.NextAttemptAtUtc });
        builder.HasIndex(x => new { x.NotificationCampaignId, x.Status });
        builder.HasIndex(x => new { x.UserId, x.Status, x.ProcessedAtUtc });
        builder.HasIndex(x => x.NotificationRuleId);
        builder.HasIndex(x => x.LeaderboardPeriodId);

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<NotificationRule>()
            .WithMany()
            .HasForeignKey(x => x.NotificationRuleId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne<NotificationCampaign>()
            .WithMany()
            .HasForeignKey(x => x.NotificationCampaignId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<NotificationTemplate>()
            .WithMany()
            .HasForeignKey(x => x.NotificationTemplateId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne<LeaderboardPeriod>()
            .WithMany()
            .HasForeignKey(x => x.LeaderboardPeriodId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Notification>()
            .WithMany()
            .HasForeignKey(x => x.NotificationId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

/// <summary>NotificationDeliveryAttempt tablo yapilandirmasi.</summary>
public class NotificationDeliveryAttemptConfiguration : IEntityTypeConfiguration<NotificationDeliveryAttempt>
{
    public void Configure(EntityTypeBuilder<NotificationDeliveryAttempt> builder)
    {
        builder.ToTable("NotificationDeliveryAttempt", "notification");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Status).HasConversion<string>().IsRequired().HasMaxLength(50);
        builder.Property(x => x.ProviderMessageId).IsRequired(false).HasMaxLength(100);
        builder.Property(x => x.ErrorCode).IsRequired(false).HasMaxLength(100);
        builder.Property(x => x.ErrorMessage).IsRequired(false).HasMaxLength(500);

        builder.HasIndex(x => x.NotificationDispatchId);
        // Makbuzu henuz kontrol edilmemis kabul edilmis denemeleri bulmak icin.
        builder.HasIndex(x => new { x.Status, x.ReceiptCheckedAtUtc });

        builder.HasOne<NotificationDispatch>()
            .WithMany()
            .HasForeignKey(x => x.NotificationDispatchId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<UserDevice>()
            .WithMany()
            .HasForeignKey(x => x.UserDeviceId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

/// <summary>MonthlyRewardNotificationRun tablo yapilandirmasi.</summary>
public class MonthlyRewardNotificationRunConfiguration : IEntityTypeConfiguration<MonthlyRewardNotificationRun>
{
    public void Configure(EntityTypeBuilder<MonthlyRewardNotificationRun> builder)
    {
        builder.ToTable("MonthlyRewardNotificationRun", "notification");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Status).HasConversion<string>().IsRequired().HasMaxLength(50);
        builder.Property(x => x.LastError).IsRequired(false).HasMaxLength(1000);
        builder.Property(x => x.AttemptCount).IsRequired().HasDefaultValue(0);

        // Donem basina tek isleme kaydi.
        builder.HasIndex(x => x.LeaderboardPeriodId).IsUnique();

        builder.HasOne<LeaderboardPeriod>()
            .WithMany()
            .HasForeignKey(x => x.LeaderboardPeriodId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
