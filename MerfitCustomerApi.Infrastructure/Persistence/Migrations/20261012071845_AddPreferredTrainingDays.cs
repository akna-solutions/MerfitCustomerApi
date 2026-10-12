using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MerfitCustomerApi.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPreferredTrainingDays : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PreferredTrainingDays",
                schema: "profile",
                table: "UserProfile",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PreferredTrainingDays",
                schema: "profile",
                table: "UserProfile");
        }
    }
}
