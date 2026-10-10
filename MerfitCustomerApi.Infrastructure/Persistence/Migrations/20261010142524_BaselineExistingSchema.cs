using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MerfitCustomerApi.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Mevcut veritabani semasinin EF Core migration gecmisine alindigi baslangic (baseline) noktasi.
    ///
    /// Bu migration'dan once repository'de hic migration yoktu; tablolar migration disinda olusturulmustu.
    /// Mevcut tablolari silmemek veya yeniden olusturmamak icin Up/Down bilincli olarak BOSTUR: mevcut
    /// bir veritabaninda "dotnet ef database update" calistirildiginda yalnizca __EFMigrationsHistory'ye
    /// kayit eklenir. Designer dosyasindaki model, bu noktadaki semanin referansidir; sonraki migration'lar
    /// (orn. AddWorkoutAttributeScores) yalnizca bu referansa gore eklenen degisiklikleri icerir.
    /// </summary>
    public partial class BaselineExistingSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
