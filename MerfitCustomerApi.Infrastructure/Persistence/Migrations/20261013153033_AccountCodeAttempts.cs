using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MerfitCustomerApi.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AccountCodeAttempts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_UserAccountToken_TokenHash",
                schema: "authentication",
                table: "UserAccountToken");

            migrationBuilder.AddColumn<int>(
                name: "AttemptCount",
                schema: "authentication",
                table: "UserAccountToken",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            // Baglantili akistan kalan, henuz kullanilmamis jetonlar artik kodla eslesemez; iptal edilir.
            // Kullanici uygulamadan yeni kod ister.
            migrationBuilder.Sql(
                "UPDATE authentication.\"UserAccountToken\" SET \"RevokedAt\" = now() " +
                "WHERE \"UsedAt\" IS NULL AND \"RevokedAt\" IS NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AttemptCount",
                schema: "authentication",
                table: "UserAccountToken");

            migrationBuilder.CreateIndex(
                name: "IX_UserAccountToken_TokenHash",
                schema: "authentication",
                table: "UserAccountToken",
                column: "TokenHash",
                unique: true);
        }
    }
}
