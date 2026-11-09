using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace MerfitCustomerApi.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class WorkoutPlanLab : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "WorkoutPlanSimulation",
                schema: "workout",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ProfileJson = table.Column<string>(type: "jsonb", nullable: false),
                    Goal = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ExperienceLevel = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    TrainingDaysPerWeek = table.Column<int>(type: "integer", nullable: false),
                    WorkoutDurationMin = table.Column<int>(type: "integer", nullable: true),
                    TrainingLocation = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    IsPlus = table.Column<bool>(type: "boolean", nullable: false),
                    SourceSimulationId = table.Column<long>(type: "bigint", nullable: true),
                    AlgorithmVersion = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    OptionsFingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ResultJson = table.Column<string>(type: "jsonb", nullable: true),
                    QualityChecksJson = table.Column<string>(type: "jsonb", nullable: true),
                    PassedCheckCount = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    WarningCheckCount = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    FailedCheckCount = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    ErrorMessage = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    StartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Rating = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    EvaluationMissingPoints = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    EvaluationProblemItemsJson = table.Column<string>(type: "jsonb", nullable: true),
                    EvaluationExerciseSuggestions = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    EvaluationComment = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    EvaluatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    EvaluatedBy = table.Column<long>(type: "bigint", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedUser = table.Column<long>(type: "bigint", nullable: true),
                    UpdatedUser = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkoutPlanSimulation", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkoutPlanSimulation_WorkoutPlanSimulation_SourceSimulatio~",
                        column: x => x.SourceSimulationId,
                        principalSchema: "workout",
                        principalTable: "WorkoutPlanSimulation",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WorkoutPlanSimulation_AlgorithmVersion",
                schema: "workout",
                table: "WorkoutPlanSimulation",
                column: "AlgorithmVersion");

            migrationBuilder.CreateIndex(
                name: "IX_WorkoutPlanSimulation_CreatedAt",
                schema: "workout",
                table: "WorkoutPlanSimulation",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_WorkoutPlanSimulation_SourceSimulationId",
                schema: "workout",
                table: "WorkoutPlanSimulation",
                column: "SourceSimulationId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkoutPlanSimulation_Status_Id",
                schema: "workout",
                table: "WorkoutPlanSimulation",
                columns: new[] { "Status", "Id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WorkoutPlanSimulation",
                schema: "workout");
        }
    }
}
