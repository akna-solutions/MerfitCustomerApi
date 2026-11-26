using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MerfitCustomerApi.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PerformanceIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_WorkoutSession_UserId",
                schema: "workout",
                table: "WorkoutSession");

            migrationBuilder.DropIndex(
                name: "IX_WaterLog_UserId",
                schema: "nutrition",
                table: "WaterLog");

            migrationBuilder.DropIndex(
                name: "IX_Notification_UserId",
                schema: "notification",
                table: "Notification");

            migrationBuilder.DropIndex(
                name: "IX_Meal_UserId",
                schema: "nutrition",
                table: "Meal");

            migrationBuilder.DropIndex(
                name: "IX_BodyMeasurement_UserId",
                schema: "profile",
                table: "BodyMeasurement");

            migrationBuilder.CreateIndex(
                name: "IX_WorkoutSession_UserId_Status_CompletedAt",
                schema: "workout",
                table: "WorkoutSession",
                columns: new[] { "UserId", "Status", "CompletedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_WaterLog_UserId_Date",
                schema: "nutrition",
                table: "WaterLog",
                columns: new[] { "UserId", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_Notification_UserId_CreatedAt",
                schema: "notification",
                table: "Notification",
                columns: new[] { "UserId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Notification_UserId_IsRead",
                schema: "notification",
                table: "Notification",
                columns: new[] { "UserId", "IsRead" });

            migrationBuilder.CreateIndex(
                name: "IX_Meal_UserId_Date",
                schema: "nutrition",
                table: "Meal",
                columns: new[] { "UserId", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_BodyMeasurement_UserId_RecordedAt",
                schema: "profile",
                table: "BodyMeasurement",
                columns: new[] { "UserId", "RecordedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_WorkoutSession_UserId_Status_CompletedAt",
                schema: "workout",
                table: "WorkoutSession");

            migrationBuilder.DropIndex(
                name: "IX_WaterLog_UserId_Date",
                schema: "nutrition",
                table: "WaterLog");

            migrationBuilder.DropIndex(
                name: "IX_Notification_UserId_CreatedAt",
                schema: "notification",
                table: "Notification");

            migrationBuilder.DropIndex(
                name: "IX_Notification_UserId_IsRead",
                schema: "notification",
                table: "Notification");

            migrationBuilder.DropIndex(
                name: "IX_Meal_UserId_Date",
                schema: "nutrition",
                table: "Meal");

            migrationBuilder.DropIndex(
                name: "IX_BodyMeasurement_UserId_RecordedAt",
                schema: "profile",
                table: "BodyMeasurement");

            migrationBuilder.CreateIndex(
                name: "IX_WorkoutSession_UserId",
                schema: "workout",
                table: "WorkoutSession",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_WaterLog_UserId",
                schema: "nutrition",
                table: "WaterLog",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Notification_UserId",
                schema: "notification",
                table: "Notification",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Meal_UserId",
                schema: "nutrition",
                table: "Meal",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_BodyMeasurement_UserId",
                schema: "profile",
                table: "BodyMeasurement",
                column: "UserId");
        }
    }
}
