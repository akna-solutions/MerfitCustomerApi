using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace MerfitCustomerApi.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ProductReviews : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ProductReview",
                schema: "store",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ProductId = table.Column<long>(type: "bigint", nullable: false),
                    UserId = table.Column<long>(type: "bigint", nullable: false),
                    Rating = table.Column<int>(type: "integer", nullable: false),
                    Title = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Comment = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    IsVerifiedPurchase = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    HelpfulCount = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    NotHelpfulCount = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    EditedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModerationNote = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ModeratedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModeratedByUserId = table.Column<long>(type: "bigint", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedUser = table.Column<long>(type: "bigint", nullable: true),
                    UpdatedUser = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductReview", x => x.Id);
                    table.CheckConstraint("CK_ProductReview_Rating_Range", "\"Rating\" BETWEEN 1 AND 5");
                    table.CheckConstraint("CK_ProductReview_VoteCounts_NonNegative", "\"HelpfulCount\" >= 0 AND \"NotHelpfulCount\" >= 0");
                    table.ForeignKey(
                        name: "FK_ProductReview_ApplicationUser_UserId",
                        column: x => x.UserId,
                        principalSchema: "authentication",
                        principalTable: "ApplicationUser",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProductReview_Product_ProductId",
                        column: x => x.ProductId,
                        principalSchema: "store",
                        principalTable: "Product",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ProductReviewImage",
                schema: "store",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ReviewId = table.Column<long>(type: "bigint", nullable: false),
                    StorageKey = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ContentType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ByteSize = table.Column<int>(type: "integer", nullable: false),
                    Width = table.Column<int>(type: "integer", nullable: false),
                    Height = table.Column<int>(type: "integer", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    Content = table.Column<byte[]>(type: "bytea", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedUser = table.Column<long>(type: "bigint", nullable: true),
                    UpdatedUser = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductReviewImage", x => x.Id);
                    table.CheckConstraint("CK_ProductReviewImage_ByteSize_Positive", "\"ByteSize\" > 0");
                    table.ForeignKey(
                        name: "FK_ProductReviewImage_ProductReview_ReviewId",
                        column: x => x.ReviewId,
                        principalSchema: "store",
                        principalTable: "ProductReview",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ProductReviewReport",
                schema: "store",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ReviewId = table.Column<long>(type: "bigint", nullable: false),
                    ReporterUserId = table.Column<long>(type: "bigint", nullable: false),
                    Reason = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ResolvedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ResolvedByUserId = table.Column<long>(type: "bigint", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedUser = table.Column<long>(type: "bigint", nullable: true),
                    UpdatedUser = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductReviewReport", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProductReviewReport_ApplicationUser_ReporterUserId",
                        column: x => x.ReporterUserId,
                        principalSchema: "authentication",
                        principalTable: "ApplicationUser",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProductReviewReport_ProductReview_ReviewId",
                        column: x => x.ReviewId,
                        principalSchema: "store",
                        principalTable: "ProductReview",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ProductReviewVote",
                schema: "store",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ReviewId = table.Column<long>(type: "bigint", nullable: false),
                    UserId = table.Column<long>(type: "bigint", nullable: false),
                    IsHelpful = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedUser = table.Column<long>(type: "bigint", nullable: true),
                    UpdatedUser = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductReviewVote", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProductReviewVote_ApplicationUser_UserId",
                        column: x => x.UserId,
                        principalSchema: "authentication",
                        principalTable: "ApplicationUser",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProductReviewVote_ProductReview_ReviewId",
                        column: x => x.ReviewId,
                        principalSchema: "store",
                        principalTable: "ProductReview",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProductReview_ProductId_Status_CreatedAt",
                schema: "store",
                table: "ProductReview",
                columns: new[] { "ProductId", "Status", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ProductReview_ProductId_Status_HelpfulCount",
                schema: "store",
                table: "ProductReview",
                columns: new[] { "ProductId", "Status", "HelpfulCount" });

            migrationBuilder.CreateIndex(
                name: "IX_ProductReview_ProductId_Status_Rating",
                schema: "store",
                table: "ProductReview",
                columns: new[] { "ProductId", "Status", "Rating" });

            migrationBuilder.CreateIndex(
                name: "IX_ProductReview_ProductId_UserId_Active",
                schema: "store",
                table: "ProductReview",
                columns: new[] { "ProductId", "UserId" },
                unique: true,
                filter: "\"Status\" <> 'Deleted'");

            migrationBuilder.CreateIndex(
                name: "IX_ProductReview_Status_CreatedAt",
                schema: "store",
                table: "ProductReview",
                columns: new[] { "Status", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ProductReview_UserId",
                schema: "store",
                table: "ProductReview",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductReviewImage_ReviewId_SortOrder",
                schema: "store",
                table: "ProductReviewImage",
                columns: new[] { "ReviewId", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_ProductReviewImage_StorageKey",
                schema: "store",
                table: "ProductReviewImage",
                column: "StorageKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProductReviewReport_ReporterUserId",
                schema: "store",
                table: "ProductReviewReport",
                column: "ReporterUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductReviewReport_ReviewId_ReporterUserId",
                schema: "store",
                table: "ProductReviewReport",
                columns: new[] { "ReviewId", "ReporterUserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProductReviewReport_ReviewId_Status",
                schema: "store",
                table: "ProductReviewReport",
                columns: new[] { "ReviewId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ProductReviewReport_Status_CreatedAt",
                schema: "store",
                table: "ProductReviewReport",
                columns: new[] { "Status", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ProductReviewVote_ReviewId_IsHelpful",
                schema: "store",
                table: "ProductReviewVote",
                columns: new[] { "ReviewId", "IsHelpful" });

            migrationBuilder.CreateIndex(
                name: "IX_ProductReviewVote_ReviewId_UserId",
                schema: "store",
                table: "ProductReviewVote",
                columns: new[] { "ReviewId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProductReviewVote_UserId",
                schema: "store",
                table: "ProductReviewVote",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProductReviewImage",
                schema: "store");

            migrationBuilder.DropTable(
                name: "ProductReviewReport",
                schema: "store");

            migrationBuilder.DropTable(
                name: "ProductReviewVote",
                schema: "store");

            migrationBuilder.DropTable(
                name: "ProductReview",
                schema: "store");
        }
    }
}
