using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace MerfitCustomerApi.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TrainingEngine : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Canli veritabaninda bu indeks bulunmayabilir; yoksa atla.
            migrationBuilder.Sql("DROP INDEX IF EXISTS workout.\"IX_WorkoutSetLog_WorkoutSessionExerciseId\";");

            // Canli veritabaninda bu indeks bulunmayabilir; yoksa atla.
            migrationBuilder.Sql("DROP INDEX IF EXISTS workout.\"IX_WorkoutPlan_UserId\";");

            migrationBuilder.AddColumn<bool>(
                name: "IsCompleted",
                schema: "workout",
                table: "WorkoutSetLog",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsWarmup",
                schema: "workout",
                table: "WorkoutSetLog",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "Rir",
                schema: "workout",
                table: "WorkoutSetLog",
                type: "integer",
                nullable: true);

            migrationBuilder.AlterColumn<long>(
                name: "WorkoutId",
                schema: "workout",
                table: "WorkoutSession",
                type: "bigint",
                nullable: true,
                oldClrType: typeof(long),
                oldType: "bigint");

            migrationBuilder.AddColumn<string>(
                name: "AlternativeExerciseIds",
                schema: "workout",
                table: "WorkoutPlanExercise",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SelectionReason",
                schema: "workout",
                table: "WorkoutPlanExercise",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "SuggestedWeightKg",
                schema: "workout",
                table: "WorkoutPlanExercise",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "TargetMuscleGroupId",
                schema: "workout",
                table: "WorkoutPlanExercise",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TargetRir",
                schema: "workout",
                table: "WorkoutPlanExercise",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WeightGuidance",
                schema: "workout",
                table: "WorkoutPlanExercise",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AlterColumn<long>(
                name: "WorkoutId",
                schema: "workout",
                table: "WorkoutPlanDay",
                type: "bigint",
                nullable: true,
                oldClrType: typeof(long),
                oldType: "bigint");

            migrationBuilder.AddColumn<string>(
                name: "Focus",
                schema: "workout",
                table: "WorkoutPlanDay",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Title",
                schema: "workout",
                table: "WorkoutPlanDay",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ArchivedAt",
                schema: "workout",
                table: "WorkoutPlan",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "BlockWeeks",
                schema: "workout",
                table: "WorkoutPlan",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ChangeSummary",
                schema: "workout",
                table: "WorkoutPlan",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EngineVersion",
                schema: "workout",
                table: "WorkoutPlan",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDeload",
                schema: "workout",
                table: "WorkoutPlan",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<long>(
                name: "PreviousPlanId",
                schema: "workout",
                table: "WorkoutPlan",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ProgramVersion",
                schema: "workout",
                table: "WorkoutPlan",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Source",
                schema: "workout",
                table: "WorkoutPlan",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Catalog");

            migrationBuilder.AddColumn<string>(
                name: "SplitType",
                schema: "workout",
                table: "WorkoutPlan",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Status",
                schema: "workout",
                table: "WorkoutPlan",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Active");

            migrationBuilder.AddColumn<decimal>(
                name: "LoadIncrementKg",
                schema: "workout",
                table: "Exercise",
                type: "numeric(6,2)",
                precision: 6,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MechanicType",
                schema: "workout",
                table: "Exercise",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MovementPattern",
                schema: "workout",
                table: "Exercise",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ExerciseEquipment",
                schema: "workout",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ExerciseId = table.Column<long>(type: "bigint", nullable: false),
                    EquipmentId = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedUser = table.Column<long>(type: "bigint", nullable: true),
                    UpdatedUser = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExerciseEquipment", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ExerciseEquipment_Equipment_EquipmentId",
                        column: x => x.EquipmentId,
                        principalSchema: "workout",
                        principalTable: "Equipment",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ExerciseEquipment_Exercise_ExerciseId",
                        column: x => x.ExerciseId,
                        principalSchema: "workout",
                        principalTable: "Exercise",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ExerciseMuscleGroup",
                schema: "workout",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ExerciseId = table.Column<long>(type: "bigint", nullable: false),
                    MuscleGroupId = table.Column<long>(type: "bigint", nullable: false),
                    Role = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedUser = table.Column<long>(type: "bigint", nullable: true),
                    UpdatedUser = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExerciseMuscleGroup", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ExerciseMuscleGroup_Exercise_ExerciseId",
                        column: x => x.ExerciseId,
                        principalSchema: "workout",
                        principalTable: "Exercise",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ExerciseMuscleGroup_MuscleGroup_MuscleGroupId",
                        column: x => x.MuscleGroupId,
                        principalSchema: "workout",
                        principalTable: "MuscleGroup",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserTrainingPreference",
                schema: "profile",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<long>(type: "bigint", nullable: false),
                    Focus = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    DaysPerWeek = table.Column<int>(type: "integer", nullable: true),
                    PriorityMuscleGroupIds = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    PreferredExerciseIds = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    AvoidedExerciseIds = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    LimitedMuscleGroupIds = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    LimitationsNote = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedUser = table.Column<long>(type: "bigint", nullable: true),
                    UpdatedUser = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserTrainingPreference", x => x.Id);
                    table.CheckConstraint("CK_UserTrainingPreference_DaysPerWeek", "\"DaysPerWeek\" IS NULL OR (\"DaysPerWeek\" >= 2 AND \"DaysPerWeek\" <= 7)");
                    table.ForeignKey(
                        name: "FK_UserTrainingPreference_ApplicationUser_UserId",
                        column: x => x.UserId,
                        principalSchema: "authentication",
                        principalTable: "ApplicationUser",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WorkoutSessionFeedback",
                schema: "workout",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    WorkoutSessionId = table.Column<long>(type: "bigint", nullable: false),
                    UserId = table.Column<long>(type: "bigint", nullable: false),
                    Difficulty = table.Column<int>(type: "integer", nullable: true),
                    Fatigue = table.Column<int>(type: "integer", nullable: true),
                    Recovery = table.Column<int>(type: "integer", nullable: true),
                    Satisfaction = table.Column<int>(type: "integer", nullable: true),
                    HasDiscomfort = table.Column<bool>(type: "boolean", nullable: false),
                    DiscomfortExerciseIds = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedUser = table.Column<long>(type: "bigint", nullable: true),
                    UpdatedUser = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkoutSessionFeedback", x => x.Id);
                    table.CheckConstraint("CK_WorkoutSessionFeedback_Difficulty", "\"Difficulty\" IS NULL OR (\"Difficulty\" >= 1 AND \"Difficulty\" <= 5)");
                    table.CheckConstraint("CK_WorkoutSessionFeedback_Fatigue", "\"Fatigue\" IS NULL OR (\"Fatigue\" >= 1 AND \"Fatigue\" <= 5)");
                    table.CheckConstraint("CK_WorkoutSessionFeedback_Recovery", "\"Recovery\" IS NULL OR (\"Recovery\" >= 1 AND \"Recovery\" <= 5)");
                    table.CheckConstraint("CK_WorkoutSessionFeedback_Satisfaction", "\"Satisfaction\" IS NULL OR (\"Satisfaction\" >= 1 AND \"Satisfaction\" <= 5)");
                    table.ForeignKey(
                        name: "FK_WorkoutSessionFeedback_ApplicationUser_UserId",
                        column: x => x.UserId,
                        principalSchema: "authentication",
                        principalTable: "ApplicationUser",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WorkoutSessionFeedback_WorkoutSession_WorkoutSessionId",
                        column: x => x.WorkoutSessionId,
                        principalSchema: "workout",
                        principalTable: "WorkoutSession",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            // Mevcut kayitlarda ayni (egzersiz, set no) iki kez varsa gecmisi silmemek icin benzersiz indeks
            // olusturulmaz; ayni adla benzersiz olmayan indeks acilir ve servis seviyesindeki upsert devam eder.
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF EXISTS (
                        SELECT 1 FROM workout."WorkoutSetLog"
                        GROUP BY "WorkoutSessionExerciseId", "SetNumber"
                        HAVING COUNT(*) > 1) THEN
                        RAISE NOTICE 'WorkoutSetLog duplicate set numbers found; creating non-unique index.';
                        CREATE INDEX IF NOT EXISTS "IX_WorkoutSetLog_WorkoutSessionExerciseId_SetNumber"
                            ON workout."WorkoutSetLog" ("WorkoutSessionExerciseId", "SetNumber");
                    ELSE
                        CREATE UNIQUE INDEX IF NOT EXISTS "IX_WorkoutSetLog_WorkoutSessionExerciseId_SetNumber"
                            ON workout."WorkoutSetLog" ("WorkoutSessionExerciseId", "SetNumber");
                    END IF;
                END $$;
                """);

            // Eski planlar: pasif olanlar arsivlenmis versiyon sayilir; aktif olan "Active" kalir.
            migrationBuilder.Sql("""
                UPDATE workout."WorkoutPlan" SET "Status" = 'Archived' WHERE "IsActive" = false AND "Status" = 'Active';
                """);

            migrationBuilder.AddCheckConstraint(
                name: "CK_WorkoutSetLog_Rir_Range",
                schema: "workout",
                table: "WorkoutSetLog",
                sql: "\"Rir\" IS NULL OR (\"Rir\" >= 0 AND \"Rir\" <= 10)");

            migrationBuilder.CreateIndex(
                name: "IX_WorkoutPlanExercise_TargetMuscleGroupId",
                schema: "workout",
                table: "WorkoutPlanExercise",
                column: "TargetMuscleGroupId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkoutPlan_PreviousPlanId",
                schema: "workout",
                table: "WorkoutPlan",
                column: "PreviousPlanId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkoutPlan_UserId_Status",
                schema: "workout",
                table: "WorkoutPlan",
                columns: new[] { "UserId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ExerciseEquipment_EquipmentId",
                schema: "workout",
                table: "ExerciseEquipment",
                column: "EquipmentId");

            migrationBuilder.CreateIndex(
                name: "IX_ExerciseEquipment_ExerciseId_EquipmentId",
                schema: "workout",
                table: "ExerciseEquipment",
                columns: new[] { "ExerciseId", "EquipmentId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ExerciseMuscleGroup_ExerciseId_MuscleGroupId",
                schema: "workout",
                table: "ExerciseMuscleGroup",
                columns: new[] { "ExerciseId", "MuscleGroupId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ExerciseMuscleGroup_MuscleGroupId",
                schema: "workout",
                table: "ExerciseMuscleGroup",
                column: "MuscleGroupId");

            migrationBuilder.CreateIndex(
                name: "IX_UserTrainingPreference_UserId",
                schema: "profile",
                table: "UserTrainingPreference",
                column: "UserId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkoutSessionFeedback_UserId_CreatedAt",
                schema: "workout",
                table: "WorkoutSessionFeedback",
                columns: new[] { "UserId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkoutSessionFeedback_WorkoutSessionId",
                schema: "workout",
                table: "WorkoutSessionFeedback",
                column: "WorkoutSessionId",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_WorkoutPlan_WorkoutPlan_PreviousPlanId",
                schema: "workout",
                table: "WorkoutPlan",
                column: "PreviousPlanId",
                principalSchema: "workout",
                principalTable: "WorkoutPlan",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_WorkoutPlanExercise_MuscleGroup_TargetMuscleGroupId",
                schema: "workout",
                table: "WorkoutPlanExercise",
                column: "TargetMuscleGroupId",
                principalSchema: "workout",
                principalTable: "MuscleGroup",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_WorkoutPlan_WorkoutPlan_PreviousPlanId",
                schema: "workout",
                table: "WorkoutPlan");

            migrationBuilder.DropForeignKey(
                name: "FK_WorkoutPlanExercise_MuscleGroup_TargetMuscleGroupId",
                schema: "workout",
                table: "WorkoutPlanExercise");

            migrationBuilder.DropTable(
                name: "ExerciseEquipment",
                schema: "workout");

            migrationBuilder.DropTable(
                name: "ExerciseMuscleGroup",
                schema: "workout");

            migrationBuilder.DropTable(
                name: "UserTrainingPreference",
                schema: "profile");

            migrationBuilder.DropTable(
                name: "WorkoutSessionFeedback",
                schema: "workout");

            migrationBuilder.DropIndex(
                name: "IX_WorkoutSetLog_WorkoutSessionExerciseId_SetNumber",
                schema: "workout",
                table: "WorkoutSetLog");

            migrationBuilder.DropCheckConstraint(
                name: "CK_WorkoutSetLog_Rir_Range",
                schema: "workout",
                table: "WorkoutSetLog");

            migrationBuilder.DropIndex(
                name: "IX_WorkoutPlanExercise_TargetMuscleGroupId",
                schema: "workout",
                table: "WorkoutPlanExercise");

            migrationBuilder.DropIndex(
                name: "IX_WorkoutPlan_PreviousPlanId",
                schema: "workout",
                table: "WorkoutPlan");

            migrationBuilder.DropIndex(
                name: "IX_WorkoutPlan_UserId_Status",
                schema: "workout",
                table: "WorkoutPlan");

            migrationBuilder.DropColumn(
                name: "IsCompleted",
                schema: "workout",
                table: "WorkoutSetLog");

            migrationBuilder.DropColumn(
                name: "IsWarmup",
                schema: "workout",
                table: "WorkoutSetLog");

            migrationBuilder.DropColumn(
                name: "Rir",
                schema: "workout",
                table: "WorkoutSetLog");

            migrationBuilder.DropColumn(
                name: "AlternativeExerciseIds",
                schema: "workout",
                table: "WorkoutPlanExercise");

            migrationBuilder.DropColumn(
                name: "SelectionReason",
                schema: "workout",
                table: "WorkoutPlanExercise");

            migrationBuilder.DropColumn(
                name: "SuggestedWeightKg",
                schema: "workout",
                table: "WorkoutPlanExercise");

            migrationBuilder.DropColumn(
                name: "TargetMuscleGroupId",
                schema: "workout",
                table: "WorkoutPlanExercise");

            migrationBuilder.DropColumn(
                name: "TargetRir",
                schema: "workout",
                table: "WorkoutPlanExercise");

            migrationBuilder.DropColumn(
                name: "WeightGuidance",
                schema: "workout",
                table: "WorkoutPlanExercise");

            migrationBuilder.DropColumn(
                name: "Focus",
                schema: "workout",
                table: "WorkoutPlanDay");

            migrationBuilder.DropColumn(
                name: "Title",
                schema: "workout",
                table: "WorkoutPlanDay");

            migrationBuilder.DropColumn(
                name: "ArchivedAt",
                schema: "workout",
                table: "WorkoutPlan");

            migrationBuilder.DropColumn(
                name: "BlockWeeks",
                schema: "workout",
                table: "WorkoutPlan");

            migrationBuilder.DropColumn(
                name: "ChangeSummary",
                schema: "workout",
                table: "WorkoutPlan");

            migrationBuilder.DropColumn(
                name: "EngineVersion",
                schema: "workout",
                table: "WorkoutPlan");

            migrationBuilder.DropColumn(
                name: "IsDeload",
                schema: "workout",
                table: "WorkoutPlan");

            migrationBuilder.DropColumn(
                name: "PreviousPlanId",
                schema: "workout",
                table: "WorkoutPlan");

            migrationBuilder.DropColumn(
                name: "ProgramVersion",
                schema: "workout",
                table: "WorkoutPlan");

            migrationBuilder.DropColumn(
                name: "Source",
                schema: "workout",
                table: "WorkoutPlan");

            migrationBuilder.DropColumn(
                name: "SplitType",
                schema: "workout",
                table: "WorkoutPlan");

            migrationBuilder.DropColumn(
                name: "Status",
                schema: "workout",
                table: "WorkoutPlan");

            migrationBuilder.DropColumn(
                name: "LoadIncrementKg",
                schema: "workout",
                table: "Exercise");

            migrationBuilder.DropColumn(
                name: "MechanicType",
                schema: "workout",
                table: "Exercise");

            migrationBuilder.DropColumn(
                name: "MovementPattern",
                schema: "workout",
                table: "Exercise");

            migrationBuilder.AlterColumn<long>(
                name: "WorkoutId",
                schema: "workout",
                table: "WorkoutSession",
                type: "bigint",
                nullable: false,
                defaultValue: 0L,
                oldClrType: typeof(long),
                oldType: "bigint",
                oldNullable: true);

            migrationBuilder.AlterColumn<long>(
                name: "WorkoutId",
                schema: "workout",
                table: "WorkoutPlanDay",
                type: "bigint",
                nullable: false,
                defaultValue: 0L,
                oldClrType: typeof(long),
                oldType: "bigint",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkoutSetLog_WorkoutSessionExerciseId",
                schema: "workout",
                table: "WorkoutSetLog",
                column: "WorkoutSessionExerciseId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkoutPlan_UserId",
                schema: "workout",
                table: "WorkoutPlan",
                column: "UserId");
        }
    }
}
