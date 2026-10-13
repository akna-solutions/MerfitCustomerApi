using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace MerfitCustomerApi.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMiniGameSessions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MiniGameSession",
                schema: "gamification",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<long>(type: "bigint", nullable: false),
                    GameKey = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    EngineVersion = table.Column<int>(type: "integer", nullable: false),
                    Seed = table.Column<long>(type: "bigint", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    StartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ClaimedScore = table.Column<int>(type: "integer", nullable: true),
                    VerifiedScore = table.Column<int>(type: "integer", nullable: true),
                    VerifiedTicks = table.Column<int>(type: "integer", nullable: true),
                    InputCount = table.Column<int>(type: "integer", nullable: true),
                    TierPoints = table.Column<int>(type: "integer", nullable: false),
                    AwardedPoints = table.Column<int>(type: "integer", nullable: false),
                    RejectReason = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedUser = table.Column<long>(type: "bigint", nullable: true),
                    UpdatedUser = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MiniGameSession", x => x.Id);
                    table.CheckConstraint("CK_MiniGameSession_Points_NonNegative", "\"TierPoints\" >= 0 AND \"AwardedPoints\" >= 0 AND \"AwardedPoints\" <= \"TierPoints\"");
                    table.ForeignKey(
                        name: "FK_MiniGameSession_ApplicationUser_UserId",
                        column: x => x.UserId,
                        principalSchema: "authentication",
                        principalTable: "ApplicationUser",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MiniGameSession_Status_CompletedAt",
                schema: "gamification",
                table: "MiniGameSession",
                columns: new[] { "Status", "CompletedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_MiniGameSession_UserId_Active",
                schema: "gamification",
                table: "MiniGameSession",
                column: "UserId",
                unique: true,
                filter: "\"Status\" = 'Started'");

            migrationBuilder.CreateIndex(
                name: "IX_MiniGameSession_UserId_StartedAt",
                schema: "gamification",
                table: "MiniGameSession",
                columns: new[] { "UserId", "StartedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MiniGameSession",
                schema: "gamification");
        }
    }
}
