using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MerfitCustomerApi.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPersonalizedNutritionPlan : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Allergies",
                schema: "profile",
                table: "UserProfile",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DietaryPreference",
                schema: "profile",
                table: "UserProfile",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExcludedFoodIds",
                schema: "profile",
                table: "UserProfile",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "NutritionOnly",
                schema: "profile",
                table: "PersonalizationJob",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "Allergens",
                schema: "nutrition",
                table: "Food",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Category",
                schema: "nutrition",
                table: "Food",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DietTags",
                schema: "nutrition",
                table: "Food",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MealSuitability",
                schema: "nutrition",
                table: "Food",
                type: "integer",
                nullable: true);

            // Benzersiz index'ten once, ayni kullanicinin birden fazla aktif plani varsa en yenisi
            // (en buyuk Id) aktif birakilir, digerleri pasiflestirilir. Hicbir kayit silinmez.
            migrationBuilder.Sql(@"
UPDATE nutrition.""NutritionPlan"" AS p
SET ""IsActive"" = FALSE,
    ""EndDate"" = COALESCE(p.""EndDate"", NOW()),
    ""UpdatedAt"" = NOW()
WHERE p.""IsActive""
  AND EXISTS (
      SELECT 1 FROM nutrition.""NutritionPlan"" AS newer
      WHERE newer.""UserId"" = p.""UserId"" AND newer.""IsActive"" AND newer.""Id"" > p.""Id"");");

            migrationBuilder.CreateIndex(
                name: "UX_NutritionPlan_UserId_Active",
                schema: "nutrition",
                table: "NutritionPlan",
                column: "UserId",
                unique: true,
                filter: "\"IsActive\"");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_NutritionPlan_UserId_Active",
                schema: "nutrition",
                table: "NutritionPlan");

            migrationBuilder.DropColumn(
                name: "Allergies",
                schema: "profile",
                table: "UserProfile");

            migrationBuilder.DropColumn(
                name: "DietaryPreference",
                schema: "profile",
                table: "UserProfile");

            migrationBuilder.DropColumn(
                name: "ExcludedFoodIds",
                schema: "profile",
                table: "UserProfile");

            migrationBuilder.DropColumn(
                name: "NutritionOnly",
                schema: "profile",
                table: "PersonalizationJob");

            migrationBuilder.DropColumn(
                name: "Allergens",
                schema: "nutrition",
                table: "Food");

            migrationBuilder.DropColumn(
                name: "Category",
                schema: "nutrition",
                table: "Food");

            migrationBuilder.DropColumn(
                name: "DietTags",
                schema: "nutrition",
                table: "Food");

            migrationBuilder.DropColumn(
                name: "MealSuitability",
                schema: "nutrition",
                table: "Food");
        }
    }
}
