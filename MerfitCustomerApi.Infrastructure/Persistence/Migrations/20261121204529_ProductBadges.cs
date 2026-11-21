using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MerfitCustomerApi.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ProductBadges : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "HighlightBadge",
                schema: "store",
                table: "Product",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_Product_HighlightBadge_Valid",
                schema: "store",
                table: "Product",
                sql: "\"HighlightBadge\" IS NULL OR \"HighlightBadge\" IN ('advantageous', 'mbfit_pick')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Product_HighlightBadge_Valid",
                schema: "store",
                table: "Product");

            migrationBuilder.DropColumn(
                name: "HighlightBadge",
                schema: "store",
                table: "Product");
        }
    }
}
