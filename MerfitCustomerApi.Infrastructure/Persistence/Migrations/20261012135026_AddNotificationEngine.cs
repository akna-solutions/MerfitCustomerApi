using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace MerfitCustomerApi.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddNotificationEngine : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_UserDevice_UserId",
                schema: "authentication",
                table: "UserDevice");

            migrationBuilder.AddColumn<string>(
                name: "TimeZone",
                schema: "profile",
                table: "UserPreference",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "PushEnabled",
                schema: "profile",
                table: "UserNotificationSetting",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<TimeOnly>(
                name: "QuietHoursEnd",
                schema: "profile",
                table: "UserNotificationSetting",
                type: "time without time zone",
                nullable: true);

            migrationBuilder.AddColumn<TimeOnly>(
                name: "QuietHoursStart",
                schema: "profile",
                table: "UserNotificationSetting",
                type: "time without time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DeactivatedAt",
                schema: "authentication",
                table: "UserDevice",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeactivationReason",
                schema: "authentication",
                table: "UserDevice",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "InstallationId",
                schema: "authentication",
                table: "UserDevice",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "MonthlyRewardNotificationRun",
                schema: "notification",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    LeaderboardPeriodId = table.Column<long>(type: "bigint", nullable: false),
                    Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    RecipientCount = table.Column<int>(type: "integer", nullable: true),
                    CreatedDispatchCount = table.Column<int>(type: "integer", nullable: true),
                    StartedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CompletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastError = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    LockToken = table.Column<Guid>(type: "uuid", nullable: true),
                    LockedUntilUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedUser = table.Column<long>(type: "bigint", nullable: true),
                    UpdatedUser = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MonthlyRewardNotificationRun", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MonthlyRewardNotificationRun_LeaderboardPeriod_LeaderboardP~",
                        column: x => x.LeaderboardPeriodId,
                        principalSchema: "gamification",
                        principalTable: "LeaderboardPeriod",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "NotificationRule",
                schema: "notification",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Kind = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    LocalTime = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    DaysOfWeekMask = table.Column<int>(type: "integer", nullable: false),
                    MealType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    OnlyUsersWithPlan = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedUser = table.Column<long>(type: "bigint", nullable: true),
                    UpdatedUser = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotificationRule", x => x.Id);
                    table.CheckConstraint("CK_NotificationRule_DaysOfWeekMask", "\"DaysOfWeekMask\" >= 0 AND \"DaysOfWeekMask\" <= 127");
                });

            migrationBuilder.CreateTable(
                name: "NotificationTemplate",
                schema: "notification",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    Type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    TitleTemplate = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    BodyTemplate = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    DataJson = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedUser = table.Column<long>(type: "bigint", nullable: true),
                    UpdatedUser = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotificationTemplate", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "NotificationCampaign",
                schema: "notification",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    Type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Segment = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    NotificationTemplateId = table.Column<long>(type: "bigint", nullable: true),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Body = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    DataJson = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    ScheduledAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    StartedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CompletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CancelledAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RecipientCount = table.Column<int>(type: "integer", nullable: true),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    LastError = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    LockToken = table.Column<Guid>(type: "uuid", nullable: true),
                    LockedUntilUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedUser = table.Column<long>(type: "bigint", nullable: true),
                    UpdatedUser = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotificationCampaign", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NotificationCampaign_NotificationTemplate_NotificationTempl~",
                        column: x => x.NotificationTemplateId,
                        principalSchema: "notification",
                        principalTable: "NotificationTemplate",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "NotificationRuleTemplate",
                schema: "notification",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    NotificationRuleId = table.Column<long>(type: "bigint", nullable: false),
                    NotificationTemplateId = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedUser = table.Column<long>(type: "bigint", nullable: true),
                    UpdatedUser = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotificationRuleTemplate", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NotificationRuleTemplate_NotificationRule_NotificationRuleId",
                        column: x => x.NotificationRuleId,
                        principalSchema: "notification",
                        principalTable: "NotificationRule",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_NotificationRuleTemplate_NotificationTemplate_NotificationT~",
                        column: x => x.NotificationTemplateId,
                        principalSchema: "notification",
                        principalTable: "NotificationTemplate",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "NotificationCampaignUser",
                schema: "notification",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    NotificationCampaignId = table.Column<long>(type: "bigint", nullable: false),
                    UserId = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedUser = table.Column<long>(type: "bigint", nullable: true),
                    UpdatedUser = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotificationCampaignUser", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NotificationCampaignUser_ApplicationUser_UserId",
                        column: x => x.UserId,
                        principalSchema: "authentication",
                        principalTable: "ApplicationUser",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_NotificationCampaignUser_NotificationCampaign_NotificationC~",
                        column: x => x.NotificationCampaignId,
                        principalSchema: "notification",
                        principalTable: "NotificationCampaign",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "NotificationDispatch",
                schema: "notification",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<long>(type: "bigint", nullable: false),
                    Type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Source = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    NotificationRuleId = table.Column<long>(type: "bigint", nullable: true),
                    NotificationCampaignId = table.Column<long>(type: "bigint", nullable: true),
                    NotificationTemplateId = table.Column<long>(type: "bigint", nullable: true),
                    LeaderboardPeriodId = table.Column<long>(type: "bigint", nullable: true),
                    DedupeKey = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Body = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    DataJson = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    LocalDate = table.Column<DateOnly>(type: "date", nullable: true),
                    ScheduledForUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    StatusReason = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    NextAttemptAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LockToken = table.Column<Guid>(type: "uuid", nullable: true),
                    LockedUntilUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    NotificationId = table.Column<long>(type: "bigint", nullable: true),
                    ProcessedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastError = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedUser = table.Column<long>(type: "bigint", nullable: true),
                    UpdatedUser = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotificationDispatch", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NotificationDispatch_ApplicationUser_UserId",
                        column: x => x.UserId,
                        principalSchema: "authentication",
                        principalTable: "ApplicationUser",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_NotificationDispatch_LeaderboardPeriod_LeaderboardPeriodId",
                        column: x => x.LeaderboardPeriodId,
                        principalSchema: "gamification",
                        principalTable: "LeaderboardPeriod",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_NotificationDispatch_NotificationCampaign_NotificationCampa~",
                        column: x => x.NotificationCampaignId,
                        principalSchema: "notification",
                        principalTable: "NotificationCampaign",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_NotificationDispatch_NotificationRule_NotificationRuleId",
                        column: x => x.NotificationRuleId,
                        principalSchema: "notification",
                        principalTable: "NotificationRule",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_NotificationDispatch_NotificationTemplate_NotificationTempl~",
                        column: x => x.NotificationTemplateId,
                        principalSchema: "notification",
                        principalTable: "NotificationTemplate",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_NotificationDispatch_Notification_NotificationId",
                        column: x => x.NotificationId,
                        principalSchema: "notification",
                        principalTable: "Notification",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "NotificationDeliveryAttempt",
                schema: "notification",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    NotificationDispatchId = table.Column<long>(type: "bigint", nullable: false),
                    UserDeviceId = table.Column<long>(type: "bigint", nullable: false),
                    AttemptNumber = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ProviderMessageId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ErrorCode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ErrorMessage = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ReceiptCheckedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedUser = table.Column<long>(type: "bigint", nullable: true),
                    UpdatedUser = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotificationDeliveryAttempt", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NotificationDeliveryAttempt_NotificationDispatch_Notificati~",
                        column: x => x.NotificationDispatchId,
                        principalSchema: "notification",
                        principalTable: "NotificationDispatch",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_NotificationDeliveryAttempt_UserDevice_UserDeviceId",
                        column: x => x.UserDeviceId,
                        principalSchema: "authentication",
                        principalTable: "UserDevice",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            // UserDevice daha once hic doldurulmuyordu; yine de tekrarlanan token varsa en yeni kayit kalir.
            migrationBuilder.Sql("""
                DELETE FROM authentication."UserDevice" AS a
                USING authentication."UserDevice" AS b
                WHERE a."DeviceToken" = b."DeviceToken" AND a."Id" < b."Id";
                """);

            migrationBuilder.CreateIndex(
                name: "IX_UserDevice_DeviceToken",
                schema: "authentication",
                table: "UserDevice",
                column: "DeviceToken",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserDevice_UserId_IsActive",
                schema: "authentication",
                table: "UserDevice",
                columns: new[] { "UserId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_MonthlyRewardNotificationRun_LeaderboardPeriodId",
                schema: "notification",
                table: "MonthlyRewardNotificationRun",
                column: "LeaderboardPeriodId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_NotificationCampaign_NotificationTemplateId",
                schema: "notification",
                table: "NotificationCampaign",
                column: "NotificationTemplateId");

            migrationBuilder.CreateIndex(
                name: "IX_NotificationCampaign_Status_ScheduledAtUtc",
                schema: "notification",
                table: "NotificationCampaign",
                columns: new[] { "Status", "ScheduledAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_NotificationCampaignUser_NotificationCampaignId_UserId",
                schema: "notification",
                table: "NotificationCampaignUser",
                columns: new[] { "NotificationCampaignId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_NotificationCampaignUser_UserId",
                schema: "notification",
                table: "NotificationCampaignUser",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_NotificationDeliveryAttempt_NotificationDispatchId",
                schema: "notification",
                table: "NotificationDeliveryAttempt",
                column: "NotificationDispatchId");

            migrationBuilder.CreateIndex(
                name: "IX_NotificationDeliveryAttempt_Status_ReceiptCheckedAtUtc",
                schema: "notification",
                table: "NotificationDeliveryAttempt",
                columns: new[] { "Status", "ReceiptCheckedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_NotificationDeliveryAttempt_UserDeviceId",
                schema: "notification",
                table: "NotificationDeliveryAttempt",
                column: "UserDeviceId");

            migrationBuilder.CreateIndex(
                name: "IX_NotificationDispatch_DedupeKey",
                schema: "notification",
                table: "NotificationDispatch",
                column: "DedupeKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_NotificationDispatch_LeaderboardPeriodId",
                schema: "notification",
                table: "NotificationDispatch",
                column: "LeaderboardPeriodId");

            migrationBuilder.CreateIndex(
                name: "IX_NotificationDispatch_NotificationCampaignId_Status",
                schema: "notification",
                table: "NotificationDispatch",
                columns: new[] { "NotificationCampaignId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_NotificationDispatch_NotificationId",
                schema: "notification",
                table: "NotificationDispatch",
                column: "NotificationId");

            migrationBuilder.CreateIndex(
                name: "IX_NotificationDispatch_NotificationRuleId",
                schema: "notification",
                table: "NotificationDispatch",
                column: "NotificationRuleId");

            migrationBuilder.CreateIndex(
                name: "IX_NotificationDispatch_NotificationTemplateId",
                schema: "notification",
                table: "NotificationDispatch",
                column: "NotificationTemplateId");

            migrationBuilder.CreateIndex(
                name: "IX_NotificationDispatch_Status_NextAttemptAtUtc",
                schema: "notification",
                table: "NotificationDispatch",
                columns: new[] { "Status", "NextAttemptAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_NotificationDispatch_UserId_Status_ProcessedAtUtc",
                schema: "notification",
                table: "NotificationDispatch",
                columns: new[] { "UserId", "Status", "ProcessedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_NotificationRule_Status",
                schema: "notification",
                table: "NotificationRule",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_NotificationRuleTemplate_NotificationRuleId_NotificationTem~",
                schema: "notification",
                table: "NotificationRuleTemplate",
                columns: new[] { "NotificationRuleId", "NotificationTemplateId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_NotificationRuleTemplate_NotificationTemplateId",
                schema: "notification",
                table: "NotificationRuleTemplate",
                column: "NotificationTemplateId");

            migrationBuilder.CreateIndex(
                name: "IX_NotificationTemplate_Name",
                schema: "notification",
                table: "NotificationTemplate",
                column: "Name",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MonthlyRewardNotificationRun",
                schema: "notification");

            migrationBuilder.DropTable(
                name: "NotificationCampaignUser",
                schema: "notification");

            migrationBuilder.DropTable(
                name: "NotificationDeliveryAttempt",
                schema: "notification");

            migrationBuilder.DropTable(
                name: "NotificationRuleTemplate",
                schema: "notification");

            migrationBuilder.DropTable(
                name: "NotificationDispatch",
                schema: "notification");

            migrationBuilder.DropTable(
                name: "NotificationCampaign",
                schema: "notification");

            migrationBuilder.DropTable(
                name: "NotificationRule",
                schema: "notification");

            migrationBuilder.DropTable(
                name: "NotificationTemplate",
                schema: "notification");

            migrationBuilder.DropIndex(
                name: "IX_UserDevice_DeviceToken",
                schema: "authentication",
                table: "UserDevice");

            migrationBuilder.DropIndex(
                name: "IX_UserDevice_UserId_IsActive",
                schema: "authentication",
                table: "UserDevice");

            migrationBuilder.DropColumn(
                name: "TimeZone",
                schema: "profile",
                table: "UserPreference");

            migrationBuilder.DropColumn(
                name: "PushEnabled",
                schema: "profile",
                table: "UserNotificationSetting");

            migrationBuilder.DropColumn(
                name: "QuietHoursEnd",
                schema: "profile",
                table: "UserNotificationSetting");

            migrationBuilder.DropColumn(
                name: "QuietHoursStart",
                schema: "profile",
                table: "UserNotificationSetting");

            migrationBuilder.DropColumn(
                name: "DeactivatedAt",
                schema: "authentication",
                table: "UserDevice");

            migrationBuilder.DropColumn(
                name: "DeactivationReason",
                schema: "authentication",
                table: "UserDevice");

            migrationBuilder.DropColumn(
                name: "InstallationId",
                schema: "authentication",
                table: "UserDevice");

            migrationBuilder.CreateIndex(
                name: "IX_UserDevice_UserId",
                schema: "authentication",
                table: "UserDevice",
                column: "UserId");
        }
    }
}
