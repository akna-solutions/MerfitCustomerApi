using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MerfitCustomerApi.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class EquipmentFlagAndAvailableDays : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "HasEquipment",
                schema: "profile",
                table: "UserProfile",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            // Mevcut kullanicilar: salon secen veya en az bir ekipman isaretlemis olan herkes ekipmanli sayilir.
            // UserEquipment kayitlari silinmez (geri donus ve raporlama icin korunur).
            migrationBuilder.Sql("""
                UPDATE profile."UserProfile" p
                SET "HasEquipment" = TRUE
                WHERE p."TrainingLocation" = 'Gym'
                   OR EXISTS (SELECT 1 FROM profile."UserEquipment" ue WHERE ue."UserId" = p."UserId");
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "HasEquipment",
                schema: "profile",
                table: "UserProfile");
        }
    }
}
