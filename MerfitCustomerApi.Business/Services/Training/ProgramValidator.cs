using MerfitCustomerApi.Business.Common;

namespace MerfitCustomerApi.Business.Services.Training;

/// <summary>
/// Programin deterministik guvenlik ve tutarlilik kurallari. Motorun urettigi her program, kullanicinin elle
/// yaptigi her degisiklik ve AI'nin bahsettigi her egzersiz bu kurallardan gecer.
/// </summary>
public static class ProgramValidator
{
    public const int MinReps = 1;
    public const int MaxReps = 30;
    public const int MaxRir = 5;
    public const int MinRestSeconds = 30;
    public const int MaxRestSeconds = 300;

    public static IReadOnlyList<string> Validate(
        GeneratedProgram program,
        IReadOnlyDictionary<long, CatalogExercise> catalog,
        TrainingProfile profile,
        TrainingEngineOptions options)
    {
        var errors = new List<string>();
        if (program.Days.Count == 0)
        {
            errors.Add("Programda antrenman günü yok.");
        }

        foreach (var day in program.Days)
        {
            if (day.Exercises.Count == 0)
            {
                errors.Add($"{day.Template.Title}: egzersiz yok.");
            }

            foreach (var duplicate in day.Exercises.GroupBy(e => e.Exercise.Id).Where(g => g.Count() > 1))
            {
                errors.Add($"{day.Template.Title}: aynı egzersiz birden fazla kez seçilmiş (#{duplicate.Key}).");
            }

            foreach (var e in day.Exercises)
            {
                var id = e.Exercise.Id;
                if (!catalog.TryGetValue(id, out var known))
                {
                    errors.Add($"#{id} veritabanındaki aktif egzersizlerden biri değil.");
                    continue;
                }

                if (profile.AvoidedExerciseIds.Contains(id))
                {
                    errors.Add($"{known.Name} kaçınılan hareketler arasında.");
                }

                if (!ExerciseMetadataResolver.IsAvailable(known, profile.EquipmentIds, profile.HasGymAccess))
                {
                    errors.Add($"{known.Name} için gereken ekipman kullanıcıda yok.");
                }

                var p = e.Prescription;
                var minSets = program.IsDeload ? 1 : 1;
                if (p.Sets < minSets || p.Sets > options.MaxSetsPerExercise)
                {
                    errors.Add($"{known.Name}: set sayısı {minSets}-{options.MaxSetsPerExercise} aralığında olmalı.");
                }

                if (p.DurationSeconds is null && (p.RepsMin < MinReps || p.RepsMax > MaxReps || p.RepsMin > p.RepsMax))
                {
                    errors.Add($"{known.Name}: tekrar aralığı {MinReps}-{MaxReps} içinde ve alt sınır üst sınırdan küçük olmalı.");
                }

                if (p.TargetRir < 0 || p.TargetRir > MaxRir)
                {
                    errors.Add($"{known.Name}: hedef RIR 0-{MaxRir} aralığında olmalı.");
                }

                if (p.RestSeconds < MinRestSeconds || p.RestSeconds > MaxRestSeconds)
                {
                    errors.Add($"{known.Name}: dinlenme {MinRestSeconds}-{MaxRestSeconds} sn aralığında olmalı.");
                }

                if (e.SuggestedWeightKg is < 0)
                {
                    errors.Add($"{known.Name}: önerilen ağırlık negatif olamaz.");
                }

                if (e.WeightGuidance == Domain.Entities.Enums.WeightGuidanceType.RampUp && e.SuggestedWeightKg.HasValue)
                {
                    errors.Add($"{known.Name}: geçmiş olmadan ağırlık önerilemez.");
                }
            }
        }

        foreach (var v in program.WeeklyVolume)
        {
            var cap = MuscleRegionMapper.IsLarge(v.Region) ? options.MaxWeeklySetsLargeMuscle : options.MaxWeeklySetsSmallMuscle;
            if (v.PlannedTotalSets > cap + 0.5m)
            {
                errors.Add($"{MuscleRegionMapper.Label(v.Region)} haftalık hacmi ({v.PlannedTotalSets:0.#} set) üst sınırı ({cap}) aşıyor.");
            }
        }

        return errors;
    }
}
