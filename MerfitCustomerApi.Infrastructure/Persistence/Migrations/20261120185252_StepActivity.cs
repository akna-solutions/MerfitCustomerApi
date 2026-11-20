using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace MerfitCustomerApi.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class StepActivity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "activity");

            migrationBuilder.CreateTable(
                name: "DailyActivity",
                schema: "activity",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<long>(type: "bigint", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    TotalSteps = table.Column<int>(type: "integer", nullable: false),
                    DistanceMeters = table.Column<int>(type: "integer", nullable: false),
                    CaloriesBurned = table.Column<int>(type: "integer", nullable: false),
                    DailyGoal = table.Column<int>(type: "integer", nullable: false),
                    HourlySteps = table.Column<int[]>(type: "integer[]", nullable: false),
                    UtcOffsetMinutes = table.Column<int>(type: "integer", nullable: false),
                    Source = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Platform = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    IsSuspicious = table.Column<bool>(type: "boolean", nullable: false),
                    SuspiciousReason = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    LastSyncedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedUser = table.Column<long>(type: "bigint", nullable: true),
                    UpdatedUser = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DailyActivity", x => x.Id);
                    table.CheckConstraint("CK_DailyActivity_HourlySteps_Length", "cardinality(\"HourlySteps\") = 24");
                    table.CheckConstraint("CK_DailyActivity_NonNegative", "\"TotalSteps\" >= 0 AND \"DistanceMeters\" >= 0 AND \"CaloriesBurned\" >= 0 AND \"DailyGoal\" > 0");
                    table.ForeignKey(
                        name: "FK_DailyActivity_ApplicationUser_UserId",
                        column: x => x.UserId,
                        principalSchema: "authentication",
                        principalTable: "ApplicationUser",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "UserActivitySetting",
                schema: "activity",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<long>(type: "bigint", nullable: false),
                    DailyStepGoal = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedUser = table.Column<long>(type: "bigint", nullable: true),
                    UpdatedUser = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserActivitySetting", x => x.Id);
                    table.CheckConstraint("CK_UserActivitySetting_DailyStepGoal", "\"DailyStepGoal\" BETWEEN 1000 AND 100000");
                    table.ForeignKey(
                        name: "FK_UserActivitySetting_ApplicationUser_UserId",
                        column: x => x.UserId,
                        principalSchema: "authentication",
                        principalTable: "ApplicationUser",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DailyActivity_UserId_Date",
                schema: "activity",
                table: "DailyActivity",
                columns: new[] { "UserId", "Date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserActivitySetting_UserId",
                schema: "activity",
                table: "UserActivitySetting",
                column: "UserId",
                unique: true);

            // Adim basarilari (mevcut rozet sistemi; puan Achievement.Points uzerinden, bir kez). Ayni kodla
            // daha once eklenmis bir basari varsa (orn. admin panelinden) tekrar eklenmez.
            migrationBuilder.Sql(StepAchievementsSeedSql);
        }

        /// <summary>Adim basarilari; admin panelinden duzenlenebilir. ConditionType'lar MerfitScoreService'te tanimlidir.</summary>
        public const string StepAchievementsSeedSql = """
            INSERT INTO gamification."Achievement" ("Code", "Title", "Description", "Icon", "Points", "ConditionType", "ConditionValue", "IsActive", "CreatedAt")
            SELECT v.code, v.title, v.description, 'steps', v.points, v.condition_type, v.condition_value, TRUE, now()
            FROM (VALUES
                ('STEPS_FIRST_1000', 'İlk 1.000 Adım', 'Adım sayarla ilk 1.000 adımını tamamla.', 10, 'steps_total', '1000'),
                ('STEPS_DAILY_5000', 'Günlük 5.000 Adım', 'Bir günde 5.000 adıma ulaş.', 15, 'steps_daily_max', '5000'),
                ('STEPS_DAILY_10000', 'Günlük 10.000 Adım', 'Bir günde 10.000 adıma ulaş.', 25, 'steps_daily_max', '10000'),
                ('STEPS_GOAL_STREAK_7', '7 Gün Hedef Serisi', 'Günlük adım hedefini 7 gün üst üste tamamla.', 50, 'step_goal_streak_days', '7'),
                ('STEPS_WEEKLY_50000', 'Haftada 50.000 Adım', 'Bir takvim haftasında (Pazartesi–Pazar) toplam 50.000 adım at.', 40, 'steps_weekly_max', '50000'),
                ('STEPS_DAILY_20000', 'Bir Günde 20.000 Adım', 'Tek bir günde 20.000 adıma ulaş.', 50, 'steps_daily_max', '20000')
            ) AS v(code, title, description, points, condition_type, condition_value)
            WHERE NOT EXISTS (SELECT 1 FROM gamification."Achievement" a WHERE a."Code" = v.code);
            """;

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DELETE FROM gamification."UserAchievement" WHERE "AchievementId" IN (
                    SELECT "Id" FROM gamification."Achievement" WHERE "ConditionType" IN ('steps_total', 'steps_daily_max', 'steps_weekly_max', 'step_goal_streak_days'));
                DELETE FROM gamification."Achievement" WHERE "ConditionType" IN ('steps_total', 'steps_daily_max', 'steps_weekly_max', 'step_goal_streak_days');
                """);

            migrationBuilder.DropTable(
                name: "DailyActivity",
                schema: "activity");

            migrationBuilder.DropTable(
                name: "UserActivitySetting",
                schema: "activity");
        }
    }
}
