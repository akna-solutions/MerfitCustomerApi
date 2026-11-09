using MerfitCustomerApi.Domain.Entities;
using MerfitCustomerApi.Domain.Entities.Enums;

namespace MerfitCustomerApi.Business.Common;

/// <summary>
/// Profilin musait gunlerini dogrular ve algoritmanin belirleyecegi haftalik gun sayisini profile yazar.
/// Gun sayisini kullanici secmez; beslenme ve ilerleme hesaplari plan uretilmeden once de dogru sayiyi
/// kullansin diye <see cref="UserProfile.TrainingDaysPerWeek"/> burada guncellenir.
/// </summary>
public static class TrainingSchedule
{
    public const int MinAvailableDays = TrainingDayPlanner.MinTrainingDays;

    /// <summary>
    /// "Mon".."Sun" kodlarini dogrular. Bos liste gecerlidir (tum hafta musait). Gecersiz kod veya
    /// 1 gun secimi hata mesajiyla birlikte false doner.
    /// </summary>
    public static bool TryParseAvailableDays(IEnumerable<string>? codes, out List<DayOfWeek> days, out string? error)
    {
        days = new List<DayOfWeek>();
        error = null;
        foreach (var code in codes ?? Enumerable.Empty<string>())
        {
            if (!TrainingDayCodes.TryParseCode(code, out var day))
            {
                error = "Gecersiz antrenman gunu. Gecerli degerler: Mon, Tue, Wed, Thu, Fri, Sat, Sun.";
                return false;
            }
            days.Add(day);
        }

        days = days.Distinct().OrderBy(TrainingDayCodes.MondayFirstIndex).ToList();
        if (days.Count is > 0 and < MinAvailableDays)
        {
            error = $"En az {MinAvailableDays} musait gun secilmelidir.";
            return false;
        }
        return true;
    }

    /// <summary>Musait gunleri profile yazar (bos = tum hafta) ve gun sayisini yeniden hesaplar.</summary>
    public static void SetAvailableDays(UserProfile profile, IReadOnlyCollection<DayOfWeek> days)
    {
        profile.PreferredTrainingDays = days.Count > 0 ? TrainingDayCodes.Format(days) : null;
        Recalculate(profile);
    }

    /// <summary>Hedef/deneyim/musait gunlere gore algoritmanin kullanacagi gunler.</summary>
    public static IReadOnlyList<DayOfWeek> PlannedDays(UserProfile profile) =>
        TrainingDayPlanner.ChooseDays(
            TrainingDayCodes.ParseStored(profile.PreferredTrainingDays),
            TrainingDayPlanner.TargetTrainingDays(FitnessGoals.Normalize(profile.Goal), profile.ExperienceLevel ?? ExperienceLevel.Beginner));

    /// <summary><see cref="UserProfile.TrainingDaysPerWeek"/> degerini algoritmanin gun sayisiyla esitler.</summary>
    public static void Recalculate(UserProfile profile) => profile.TrainingDaysPerWeek = PlannedDays(profile).Count;
}
