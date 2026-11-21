using MerfitCustomerApi.Domain.Entities;
using MerfitCustomerApi.Domain.Entities.Enums;

namespace MerfitCustomerApi.Business.Services.Training;

/// <summary>
/// Veritabanindaki Exercise satirini motorun kullandigi <see cref="CatalogExercise"/>'a cevirir. Eksik metadata
/// (bilesik/izolasyon, patern, ekipman baglantisi, artis adimi) acik ve test edilen kurallarla tahmin edilir;
/// her tahmin isaretlenir. Yeni egzersiz olusturmaz, veritabanina yazmaz.
/// </summary>
public static class ExerciseMetadataResolver
{
    private static readonly string[] IsolationKeywords =
    {
        "curl", "extension", "raise", "fly", "flye", "kickback", "crossover", "shrug", "calf", "pec deck", "pullover",
        "skull", "kaldirma", "acma", "uzatma", "bukme", "adduction", "abduction", "adductor", "abductor", "crunch", "plank",
    };

    private static readonly string[] CompoundKeywords =
    {
        "press", "squat", "deadlift", "row", "pull up", "pullup", "pull-up", "chin", "lunge", "dip", "hip thrust", "thruster",
        "clean", "snatch", "push up", "pushup", "sinav", "barfiks", "step up", "good morning", "kurek", "cekis", "itis",
        "pulldown", "pull down", "rdl", "romanian", "swing", "glute bridge", "kalca kopru",
    };

    /// <summary>Vucut agirligi ifadeleri (WorkoutSessionService ile ayni liste).</summary>
    public static readonly string[] BodyweightMarkers =
    {
        "bodyweight", "body weight", "body_weight", "vucut agirligi", "ekipmansiz", "none", "yok",
    };

    public sealed record EquipmentRow(long Id, string Name, string Slug);

    public sealed record MuscleRow(long Id, string Name, string Slug);

    public sealed record MetadataInput(
        Exercise Exercise,
        IReadOnlyDictionary<long, MuscleRegion> RegionByMuscleGroupId,
        IReadOnlyList<long> SecondaryMuscleGroupIds,
        IReadOnlyList<long> LinkedEquipmentIds,
        IReadOnlyList<EquipmentRow> AllEquipment,
        bool IsTimeBased);

    public static CatalogExercise Resolve(MetadataInput input)
    {
        var e = input.Exercise;
        var name = MuscleRegionMapper.Normalize(e.Name);
        var primaryRegion = input.RegionByMuscleGroupId.GetValueOrDefault(e.PrimaryMuscleGroupId, MuscleRegion.Other);
        var secondary = input.SecondaryMuscleGroupIds
            .Where(id => id != e.PrimaryMuscleGroupId)
            .Select(id => input.RegionByMuscleGroupId.GetValueOrDefault(id, MuscleRegion.Other))
            .Where(r => r != MuscleRegion.Other && r != primaryRegion)
            .Distinct()
            .ToList();

        var (mechanic, mechanicInferred) = ResolveMechanic(e.MechanicType, name, secondary.Count, primaryRegion);
        var (pattern, patternInferred) = ResolvePattern(e.MovementPattern, name, mechanic, primaryRegion);

        var equipmentText = MuscleRegionMapper.Normalize(e.EquipmentType);
        var isBodyweightText = string.IsNullOrWhiteSpace(e.EquipmentType)
            || BodyweightMarkers.Any(m => MuscleRegionMapper.ContainsWord(equipmentText, m));

        IReadOnlyList<long> required;
        bool unresolved = false;
        bool equipmentInferred = false;
        if (input.LinkedEquipmentIds.Count > 0)
        {
            required = input.LinkedEquipmentIds.Distinct().OrderBy(x => x).ToList();
        }
        else if (isBodyweightText)
        {
            required = Array.Empty<long>();
            equipmentInferred = !string.IsNullOrWhiteSpace(e.EquipmentType);
        }
        else
        {
            equipmentInferred = true;
            (required, unresolved) = MatchEquipmentText(e.EquipmentType!, input.AllEquipment);
        }

        var loadType = ResolveLoadType(equipmentText + name, isBodyweightText && input.LinkedEquipmentIds.Count == 0);
        var isBodyweight = loadType == LoadType.Bodyweight;
        var increment = e.LoadIncrementKg is > 0
            ? e.LoadIncrementKg.Value
            : DefaultIncrement(loadType, mechanic, primaryRegion);

        return new CatalogExercise
        {
            Id = e.Id,
            Name = e.Name,
            Difficulty = e.Difficulty,
            PrimaryMuscleGroupId = e.PrimaryMuscleGroupId,
            PrimaryRegion = primaryRegion,
            SecondaryRegions = secondary,
            Mechanic = mechanic,
            MechanicInferred = mechanicInferred,
            Pattern = pattern,
            PatternInferred = patternInferred,
            LoadType = loadType,
            LoadIncrementKg = increment,
            RequiredEquipmentIds = required,
            HasUnresolvedEquipment = unresolved,
            EquipmentInferred = equipmentInferred,
            IsBodyweight = isBodyweight,
            IsTimeBased = input.IsTimeBased,
        };
    }

    internal static (ExerciseMechanicType, bool) ResolveMechanic(ExerciseMechanicType? stored, string normalizedName, int secondaryCount, MuscleRegion primary)
    {
        if (stored.HasValue)
        {
            return (stored.Value, false);
        }

        if (IsolationKeywords.Any(k => MuscleRegionMapper.ContainsWord(normalizedName, k)))
        {
            return (ExerciseMechanicType.Isolation, true);
        }

        if (CompoundKeywords.Any(k => MuscleRegionMapper.ContainsWord(normalizedName, k)) || secondaryCount > 0)
        {
            return (ExerciseMechanicType.Compound, true);
        }

        // Bilinmiyorsa temkinli varsayim: izolasyon (gunun sonuna yerlesir, daha yuksek tekrar).
        return (ExerciseMechanicType.Isolation, true);
    }

    internal static (MovementPattern?, bool) ResolvePattern(MovementPattern? stored, string n, ExerciseMechanicType mechanic, MuscleRegion primary)
    {
        if (stored.HasValue)
        {
            return (stored.Value, false);
        }

        bool Has(params string[] keys) => keys.Any(k => MuscleRegionMapper.ContainsWord(n, k));

        MovementPattern? pattern = null;
        if (primary == MuscleRegion.Cardio) pattern = MovementPattern.Conditioning;
        else if (mechanic == ExerciseMechanicType.Isolation) pattern = primary == MuscleRegion.Core ? MovementPattern.Core : MovementPattern.Isolation;
        else if (Has("lunge", "split squat", "step up", "bulgarian", "hamle")) pattern = MovementPattern.Lunge;
        else if (Has("squat", "leg press", "hack", "thruster")) pattern = MovementPattern.Squat;
        else if (Has("deadlift", "rdl", "romanian", "hip thrust", "good morning", "swing", "glute bridge", "kalca kopru", "hinge")) pattern = MovementPattern.Hinge;
        else if (Has("pull up", "pullup", "chin", "pulldown", "pull down", "barfiks", "lat")) pattern = MovementPattern.VerticalPull;
        else if (Has("row", "kurek")) pattern = MovementPattern.HorizontalPull;
        else if (Has("overhead", "shoulder press", "military", "omuz press", "arnold", "pike")) pattern = MovementPattern.VerticalPush;
        else if (Has("bench", "push up", "pushup", "sinav", "chest press", "dip", "gogus press", "floor press")) pattern = MovementPattern.HorizontalPush;
        else if (Has("carry", "farmer")) pattern = MovementPattern.Carry;
        else if (primary == MuscleRegion.Core) pattern = MovementPattern.Core;
        else if (Has("press"))
        {
            pattern = primary == MuscleRegion.Shoulders ? MovementPattern.VerticalPush
                : MuscleRegionMapper.IsLowerBody(primary) ? MovementPattern.Squat
                : MovementPattern.HorizontalPush;
        }

        return (pattern, pattern.HasValue);
    }

    internal static LoadType ResolveLoadType(string normalizedText, bool bodyweight)
    {
        bool Has(params string[] keys) => keys.Any(k => MuscleRegionMapper.ContainsWord(normalizedText, k));
        if (Has("barbell", "halter", "olympic bar", "ez bar", "smith")) return Has("smith") ? LoadType.Machine : LoadType.Barbell;
        if (Has("dumbbell", "dambil", "dumbell")) return LoadType.Dumbbell;
        if (Has("kettlebell")) return LoadType.Kettlebell;
        if (Has("cable", "kablo", "pulley", "makara")) return LoadType.Cable;
        if (Has("machine", "makine", "leg press", "pec deck")) return LoadType.Machine;
        if (Has("band", "lastik", "direnc bandi")) return LoadType.Band;
        return bodyweight ? LoadType.Bodyweight : LoadType.Other;
    }

    internal static decimal DefaultIncrement(LoadType loadType, ExerciseMechanicType mechanic, MuscleRegion primary) => loadType switch
    {
        LoadType.Barbell => mechanic == ExerciseMechanicType.Compound && MuscleRegionMapper.IsLowerBody(primary) ? 5m : 2.5m,
        LoadType.Dumbbell => 2m,
        LoadType.Machine => mechanic == ExerciseMechanicType.Compound ? 5m : 2.5m,
        LoadType.Cable => 2.5m,
        LoadType.Kettlebell => 4m,
        LoadType.Bodyweight or LoadType.Band => 0m,
        _ => 2.5m,
    };

    /// <summary>
    /// EquipmentType serbest metnini (virgul, "/", ";", "+" ile ayrilmis) Equipment ad / slug'lariyla esler.
    /// Eslesmeyen parca varsa <c>unresolved=true</c> doner.
    /// </summary>
    internal static (IReadOnlyList<long> Ids, bool Unresolved) MatchEquipmentText(string text, IReadOnlyList<EquipmentRow> equipment)
    {
        var ids = new List<long>();
        var unresolved = false;
        var parts = text.Split(new[] { ',', '/', ';', '+', '|' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var part in parts)
        {
            var token = MuscleRegionMapper.Normalize(part).Trim();
            if (token.Length == 0 || BodyweightMarkers.Any(m => MuscleRegionMapper.Normalize(m).Trim() == token))
            {
                continue;
            }

            var match = equipment
                .Select(eq => new { eq.Id, Name = MuscleRegionMapper.Normalize(eq.Name).Trim(), Slug = MuscleRegionMapper.Normalize(eq.Slug?.Replace('-', ' ')).Trim() })
                .Where(eq => eq.Name == token || eq.Slug == token
                    || (eq.Name.Length >= 3 && (token.Contains(eq.Name) || eq.Name.Contains(token)))
                    || (eq.Slug.Length >= 3 && (token.Contains(eq.Slug) || eq.Slug.Contains(token))))
                .OrderByDescending(eq => eq.Name == token || eq.Slug == token)
                .ThenBy(eq => eq.Id)
                .FirstOrDefault();
            if (match is null)
            {
                unresolved = true;
            }
            else if (!ids.Contains(match.Id))
            {
                ids.Add(match.Id);
            }
        }

        return (ids, unresolved);
    }

    /// <summary>Kullanici bu egzersizi kendi ekipmaniyla yapabilir mi?</summary>
    public static bool IsAvailable(CatalogExercise exercise, IReadOnlySet<long> userEquipment, bool hasGymAccess)
    {
        if (exercise.HasUnresolvedEquipment && !hasGymAccess)
        {
            return false;
        }

        return exercise.RequiredEquipmentIds.All(userEquipment.Contains);
    }
}
