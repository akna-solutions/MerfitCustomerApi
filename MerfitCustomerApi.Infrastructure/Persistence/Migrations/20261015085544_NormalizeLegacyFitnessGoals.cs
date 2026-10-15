using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MerfitCustomerApi.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Veri migration'i: kullaniciya artik sunulmayan eski hedefleri (MaintainWeight, ImproveFitness,
    /// ImproveEndurance) Kas Gelistirme'ye (BuildMuscle) tasir. Enum degerleri silinmez; yalnizca
    /// kullanicinin GUNCEL hedefi degisir, gecmis kayitlar korunur.
    ///
    /// 1) Etkilenen her kullanici icin GoalHistory'ye (eski -> BuildMuscle) bir satir yazilir; Reason
    ///    alani migration isaretini tasir, Down bu satirlardan eski degeri geri yukler.
    /// 2) UserProfile.Goal ve AKTIF UserGoal.GoalType guncellenir (pasif hedef kayitlari tarihce olarak kalir).
    /// 3) Aktif plani olan tasinan kullanicilar icin, bekleyen/islenen isi yoksa, plan yenileme isi
    ///    (PersonalizationJob, Pending, NutritionOnly = false) kuyruga eklenir; mevcut planlar yeni plan
    ///    uretilene kadar oldugu gibi kalir.
    /// 4) Mevcut kisisel plan egzersizlerine tekrar araliginin ust siniri (RepsMax) eklenir: yalnizca
    ///    Reps, planin hedefine ait eski varsayilan alt sinira esitse (kullanici/yonetici duzenlemesine dokunulmaz).
    ///
    /// Hedef kilo (TargetWeightKg) SQL ile yeniden hesaplanmaz; kullanici hedefini guncellediginde veya
    /// profilini kaydettiginde uygulama tarafinda yeniden hesaplanir.
    /// </summary>
    public partial class NormalizeLegacyFitnessGoals : Migration
    {
        private const string Marker = "migration:NormalizeLegacyFitnessGoals";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql($"""
                INSERT INTO profile."GoalHistory" ("UserId", "OldGoal", "NewGoal", "ChangedAt", "Reason", "CreatedAt")
                SELECT p."UserId",
                       COALESCE(
                           CASE WHEN p."Goal" IN ('MaintainWeight', 'ImproveFitness', 'ImproveEndurance') THEN p."Goal" END,
                           g."GoalType"),
                       'BuildMuscle', now(), '{Marker}', now()
                FROM profile."UserProfile" p
                LEFT JOIN profile."UserGoal" g
                       ON g."UserId" = p."UserId" AND g."IsActive"
                      AND g."GoalType" IN ('MaintainWeight', 'ImproveFitness', 'ImproveEndurance')
                WHERE (p."Goal" IN ('MaintainWeight', 'ImproveFitness', 'ImproveEndurance') OR g."Id" IS NOT NULL)
                  AND NOT EXISTS (
                      SELECT 1 FROM profile."GoalHistory" h
                      WHERE h."UserId" = p."UserId" AND h."Reason" = '{Marker}');
                """);

            migrationBuilder.Sql("""
                UPDATE profile."UserProfile"
                SET "Goal" = 'BuildMuscle', "UpdatedAt" = now()
                WHERE "Goal" IN ('MaintainWeight', 'ImproveFitness', 'ImproveEndurance');
                """);

            migrationBuilder.Sql("""
                UPDATE profile."UserGoal"
                SET "GoalType" = 'BuildMuscle', "UpdatedAt" = now()
                WHERE "IsActive" AND "GoalType" IN ('MaintainWeight', 'ImproveFitness', 'ImproveEndurance');
                """);

            migrationBuilder.Sql($"""
                INSERT INTO profile."PersonalizationJob" ("UserId", "Status", "AttemptCount", "NutritionOnly", "CreatedAt")
                SELECT DISTINCT h."UserId", 'Pending', 0, false, now()
                FROM profile."GoalHistory" h
                WHERE h."Reason" = '{Marker}'
                  AND (EXISTS (SELECT 1 FROM workout."WorkoutPlan" wp WHERE wp."UserId" = h."UserId" AND wp."IsActive")
                       OR EXISTS (SELECT 1 FROM nutrition."NutritionPlan" np WHERE np."UserId" = h."UserId" AND np."IsActive"))
                  AND NOT EXISTS (
                      SELECT 1 FROM profile."PersonalizationJob" j
                      WHERE j."UserId" = h."UserId" AND j."Status" IN ('Pending', 'Processing'));
                """);

            migrationBuilder.Sql("""
                UPDATE workout."WorkoutPlanExercise" e
                SET "RepsMax" = CASE wp."Goal"
                        WHEN 'BuildMuscle' THEN 12
                        WHEN 'GetStronger' THEN 6
                        WHEN 'LoseWeight' THEN 15
                        WHEN 'ImproveEndurance' THEN 20
                        WHEN 'ImproveFitness' THEN 15
                        WHEN 'MaintainWeight' THEN 12
                    END
                FROM workout."WorkoutPlanDay" d
                JOIN workout."WorkoutPlan" wp ON wp."Id" = d."WorkoutPlanId"
                WHERE e."WorkoutPlanDayId" = d."Id"
                  AND e."RepsMax" IS NULL
                  AND e."DurationSeconds" IS NULL
                  AND e."Reps" = CASE wp."Goal"
                        WHEN 'BuildMuscle' THEN 8
                        WHEN 'GetStronger' THEN 3
                        WHEN 'LoseWeight' THEN 12
                        WHEN 'ImproveEndurance' THEN 15
                        WHEN 'ImproveFitness' THEN 10
                        WHEN 'MaintainWeight' THEN 10
                    END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Eski hedef yalnizca kullanici hedefini migration'dan sonra degistirmediyse geri yuklenir.
            migrationBuilder.Sql($"""
                WITH restorable AS (
                    SELECT h."UserId", h."OldGoal"
                    FROM profile."GoalHistory" h
                    WHERE h."Reason" = '{Marker}'
                      AND NOT EXISTS (
                          SELECT 1 FROM profile."GoalHistory" later
                          WHERE later."UserId" = h."UserId" AND later."Id" > h."Id")
                )
                UPDATE profile."UserProfile" p
                SET "Goal" = r."OldGoal", "UpdatedAt" = now()
                FROM restorable r
                WHERE p."UserId" = r."UserId" AND p."Goal" = 'BuildMuscle';
                """);

            migrationBuilder.Sql($"""
                WITH restorable AS (
                    SELECT h."UserId", h."OldGoal"
                    FROM profile."GoalHistory" h
                    WHERE h."Reason" = '{Marker}'
                      AND NOT EXISTS (
                          SELECT 1 FROM profile."GoalHistory" later
                          WHERE later."UserId" = h."UserId" AND later."Id" > h."Id")
                )
                UPDATE profile."UserGoal" g
                SET "GoalType" = r."OldGoal", "UpdatedAt" = now()
                FROM restorable r
                WHERE g."UserId" = r."UserId" AND g."IsActive" AND g."GoalType" = 'BuildMuscle';
                """);

            migrationBuilder.Sql($"""
                DELETE FROM profile."GoalHistory" WHERE "Reason" = '{Marker}';
                """);
        }
    }
}
