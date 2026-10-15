using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MerfitCustomerApi.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class HypertrophyBodyComposition : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "RepsMax",
                schema: "workout",
                table: "WorkoutPlanExercise",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "CardioWorkoutId",
                schema: "workout",
                table: "WorkoutPlanDay",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LowBodyFatWarningAcknowledgedAt",
                schema: "profile",
                table: "UserGoal",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BodyFatEstimationMethod",
                schema: "profile",
                table: "BodyMeasurement",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "EstimatedBodyFatPercentage",
                schema: "profile",
                table: "BodyMeasurement",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "NeckCm",
                schema: "profile",
                table: "BodyMeasurement",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkoutPlanDay_CardioWorkoutId",
                schema: "workout",
                table: "WorkoutPlanDay",
                column: "CardioWorkoutId");

            migrationBuilder.AddForeignKey(
                name: "FK_WorkoutPlanDay_Workout_CardioWorkoutId",
                schema: "workout",
                table: "WorkoutPlanDay",
                column: "CardioWorkoutId",
                principalSchema: "workout",
                principalTable: "Workout",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_WorkoutPlanDay_Workout_CardioWorkoutId",
                schema: "workout",
                table: "WorkoutPlanDay");

            migrationBuilder.DropIndex(
                name: "IX_WorkoutPlanDay_CardioWorkoutId",
                schema: "workout",
                table: "WorkoutPlanDay");

            migrationBuilder.DropColumn(
                name: "RepsMax",
                schema: "workout",
                table: "WorkoutPlanExercise");

            migrationBuilder.DropColumn(
                name: "CardioWorkoutId",
                schema: "workout",
                table: "WorkoutPlanDay");

            migrationBuilder.DropColumn(
                name: "LowBodyFatWarningAcknowledgedAt",
                schema: "profile",
                table: "UserGoal");

            migrationBuilder.DropColumn(
                name: "BodyFatEstimationMethod",
                schema: "profile",
                table: "BodyMeasurement");

            migrationBuilder.DropColumn(
                name: "EstimatedBodyFatPercentage",
                schema: "profile",
                table: "BodyMeasurement");

            migrationBuilder.DropColumn(
                name: "NeckCm",
                schema: "profile",
                table: "BodyMeasurement");
        }
    }
}
