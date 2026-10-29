using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MerfitCustomerApi.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class HomeCalendarCampaigns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "WeightReminderSnoozedUntil",
                schema: "profile",
                table: "UserPreference",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "SeenAt",
                schema: "gamification",
                table: "UserAchievement",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Body",
                schema: "content",
                table: "AppContent",
                type: "character varying(8000)",
                maxLength: 8000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Category",
                schema: "content",
                table: "AppContent",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SortOrder",
                schema: "content",
                table: "AppContent",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            // Mevcut kazanimlar zaten kullaniciya gosterilmis sayilir; yalnizca bundan sonraki yeni kazanimlarda
            // kutlama (konfeti) gosterilir.
            migrationBuilder.Sql("UPDATE gamification.\"UserAchievement\" SET \"SeenAt\" = \"EarnedAt\" WHERE \"SeenAt\" IS NULL;");

            // Tekil indeks: eszamanli isteklerde ayni basarinin iki kez yazilmasini engeller. Veritabaninda zaten
            // mukerrer satir varsa hicbir satir silinmez; indeks atlanir ve eski UserId indeksi korunur (NOTICE).
            migrationBuilder.Sql(@"
DO $$
BEGIN
    IF EXISTS (
        SELECT 1 FROM gamification.""UserAchievement""
        GROUP BY ""UserId"", ""AchievementId"" HAVING COUNT(*) > 1
    ) THEN
        RAISE NOTICE 'UserAchievement tablosunda mukerrer kazanim var; IX_UserAchievement_UserId_AchievementId olusturulmadi.';
    ELSE
        CREATE UNIQUE INDEX IF NOT EXISTS ""IX_UserAchievement_UserId_AchievementId""
            ON gamification.""UserAchievement"" (""UserId"", ""AchievementId"");
        DROP INDEX IF EXISTS gamification.""IX_UserAchievement_UserId"";
    END IF;
END $$;");

            migrationBuilder.CreateIndex(
                name: "IX_AppContent_Type_IsActive_SortOrder",
                schema: "content",
                table: "AppContent",
                columns: new[] { "Type", "IsActive", "SortOrder" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("CREATE INDEX IF NOT EXISTS \"IX_UserAchievement_UserId\" ON gamification.\"UserAchievement\" (\"UserId\");");
            migrationBuilder.Sql("DROP INDEX IF EXISTS gamification.\"IX_UserAchievement_UserId_AchievementId\";");

            migrationBuilder.DropIndex(
                name: "IX_AppContent_Type_IsActive_SortOrder",
                schema: "content",
                table: "AppContent");

            migrationBuilder.DropColumn(
                name: "WeightReminderSnoozedUntil",
                schema: "profile",
                table: "UserPreference");

            migrationBuilder.DropColumn(
                name: "SeenAt",
                schema: "gamification",
                table: "UserAchievement");

            migrationBuilder.DropColumn(
                name: "Body",
                schema: "content",
                table: "AppContent");

            migrationBuilder.DropColumn(
                name: "Category",
                schema: "content",
                table: "AppContent");

            migrationBuilder.DropColumn(
                name: "SortOrder",
                schema: "content",
                table: "AppContent");
        }
    }
}
