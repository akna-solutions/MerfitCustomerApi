using MerfitCustomerApi.Domain.Entities;
using MerfitCustomerApi.Domain.Entities.Enums;
using MerfitCustomerApi.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace MerfitCustomerApi.Business.Services.Progress;

/// <summary>
/// Kilo kaydi ve hedef kilo icin ProfileService, ProgressService, DashboardService ve AuthService'in
/// ortak kullandigi kurallar. SaveChanges cagirmaz; cagiran servis kendi isleminde kaydeder.
/// </summary>
public static class WeightTracking
{
    /// <summary>
    /// Gunde tek kilo kaydi tutulur: ayni gune ikinci giris oncekini gunceller. Boy biliniyorsa BMI de yazilir.
    /// </summary>
    public static async Task<BodyMeasurement> UpsertDailyWeightAsync(
        IUnitOfWork unitOfWork, long userId, DateOnly date, decimal weightKg, decimal? heightCm, CancellationToken cancellationToken)
    {
        var dayStart = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var dayEnd = dayStart.AddDays(1);
        var repository = unitOfWork.Repository<BodyMeasurement>();

        var measurement = await repository.GetQueryable(asNoTracking: false)
            .Where(m => m.UserId == userId && m.RecordedAt >= dayStart && m.RecordedAt < dayEnd)
            .OrderByDescending(m => m.Id)
            .FirstOrDefaultAsync(cancellationToken);

        var bmi = heightCm is > 0 ? Math.Round(weightKg / (heightCm.Value / 100m * (heightCm.Value / 100m)), 1) : (decimal?)null;

        if (measurement is null)
        {
            measurement = new BodyMeasurement
            {
                UserId = userId,
                RecordedAt = dayStart,
                WeightKg = weightKg,
                BMI = bmi,
                CreatedAt = DateTime.UtcNow,
            };
            await repository.AddAsync(measurement, cancellationToken);
        }
        else
        {
            measurement.WeightKg = weightKg;
            measurement.BMI = bmi;
            measurement.UpdatedAt = DateTime.UtcNow;
        }

        return measurement;
    }

    /// <summary>
    /// Aktif hedefin hedef kilosu yoksa profilden otomatik hesaplayip hem hedefe hem profile yazar;
    /// baslangic kilosu yoksa mevcut kiloyu baslangic kabul eder. Bir sey degistiyse true doner.
    /// </summary>
    public static bool EnsureWeightGoal(UserGoal goal, UserProfile profile, decimal? currentWeightKg)
    {
        var changed = false;
        var weight = currentWeightKg ?? profile.WeightKg;

        if (goal.StartingWeightKg is null && weight is not null)
        {
            goal.StartingWeightKg = weight;
            changed = true;
        }

        if (goal.TargetWeightKg is null)
        {
            // Hedef, hedefin basladigi kiloya gore hesaplanir (sonradan girilen kilolar hedefi kaydirmaz).
            var target = profile.TargetWeightKg
                ?? TargetWeightCalculator.Calculate(goal.GoalType, goal.StartingWeightKg ?? weight, profile.HeightCm, profile.ExperienceLevel);
            if (target is not null)
            {
                goal.TargetWeightKg = target;
                profile.TargetWeightKg = target;
                changed = true;
            }
        }

        if (changed)
        {
            goal.UpdatedAt = DateTime.UtcNow;
        }

        return changed;
    }

    /// <summary>
    /// Hedef degistiginde yeni hedef bugunden baslar: baslangic kilosu mevcut kilo olur, hedef kilo yeni
    /// hedefe gore yeniden hesaplanir (kullanici ayni istekte elle hedef kilo verdiyse o sonra yazilir).
    /// </summary>
    public static void ResetForNewGoal(UserGoal goal, UserProfile profile, FitnessGoal goalType, decimal? currentWeightKg)
    {
        var weight = currentWeightKg ?? profile.WeightKg;
        goal.GoalType = goalType;
        goal.StartingWeightKg = weight;
        goal.CurrentWeightKg = weight;
        goal.ProgressPercent = null;
        goal.TargetWeightKg = TargetWeightCalculator.Calculate(goalType, weight, profile.HeightCm, profile.ExperienceLevel);
        profile.TargetWeightKg = goal.TargetWeightKg;
        goal.UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Baslangictan hedefe kat edilen yol (%0-100). Kilo koruma hedefinde (baslangic = hedef)
    /// hedefe 1 kg'dan yakinsa %100, uzaklastikca azalir.
    /// </summary>
    public static int CalculateProgressPercent(decimal startingWeight, decimal currentWeight, decimal targetWeight)
    {
        var total = targetWeight - startingWeight;
        if (Math.Abs(total) < 0.5m)
        {
            var drift = Math.Abs(currentWeight - targetWeight);
            return drift <= 1m ? 100 : (int)Math.Max(0, Math.Round(100 - (drift - 1m) * 20));
        }

        var ratio = (currentWeight - startingWeight) / total;
        return (int)Math.Round(Math.Clamp(ratio, 0m, 1m) * 100);
    }

    /// <summary>"lose" | "gain" | "maintain": hedefe ulasmak icin kilonun hangi yone gitmesi gerektigi.</summary>
    public static string Direction(decimal startingWeight, decimal targetWeight) =>
        targetWeight < startingWeight - 0.25m ? "lose" : targetWeight > startingWeight + 0.25m ? "gain" : "maintain";
}
