using System.Globalization;
using System.Text;

namespace MerfitCustomerApi.Business.Services.Training;

/// <summary>
/// MuscleGroup ad / slug'ini kanonik bolgeye esler. Once yapilandirma (alias) sozlugune, sonra Turkce ve
/// Ingilizce anahtar kelimelere bakar. Eslesmeyen kas grubu <see cref="MuscleRegion.Other"/> olur ve raporlanir.
/// </summary>
public static class MuscleRegionMapper
{
    // Sira onemli: daha ozel ifadeler once (orn. "arka bacak" -> Hamstrings, "on kol" -> Forearms).
    private static readonly (MuscleRegion Region, string[] Keywords)[] Rules =
    {
        (MuscleRegion.Hamstrings, new[] { "hamstring", "arka bacak", "arka uyluk", "biceps femoris", "bacak arka" }),
        (MuscleRegion.Forearms, new[] { "forearm", "on kol", "onkol", "bilek" }),
        (MuscleRegion.Triceps, new[] { "tricep", "arka kol" }),
        (MuscleRegion.Biceps, new[] { "bicep", "pazu", "on kol ust" }),
        (MuscleRegion.Quadriceps, new[] { "quad", "on bacak", "on uyluk", "kuadriseps" }),
        (MuscleRegion.Glutes, new[] { "glute", "kalca", "gluteal", "popo" }),
        (MuscleRegion.Calves, new[] { "calf", "calves", "baldir", "kalf" }),
        (MuscleRegion.Chest, new[] { "chest", "gogus", "pectoral", "pec" }),
        (MuscleRegion.Shoulders, new[] { "shoulder", "omuz", "delt" }),
        (MuscleRegion.Back, new[] { "back", "sirt", "lat", "kanat", "trapez", "trap", "rhomboid", "bel" }),
        (MuscleRegion.Core, new[] { "core", "karin", "abs", "abdominal", "oblik", "oblique", "govde" }),
        (MuscleRegion.Cardio, new[] { "cardio", "kardiyo", "kondisyon" }),
        (MuscleRegion.FullBody, new[] { "full body", "fullbody", "tum vucut", "tam vucut", "full-body" }),
        (MuscleRegion.Quadriceps, new[] { "leg", "bacak", "uyluk" }),
        (MuscleRegion.Biceps, new[] { "arm", "kol" }),
    };

    public static MuscleRegion Map(string? name, string? slug, IReadOnlyDictionary<string, string>? aliases = null)
    {
        if (aliases is not null)
        {
            foreach (var key in new[] { slug, name })
            {
                if (!string.IsNullOrWhiteSpace(key)
                    && aliases.TryGetValue(key.Trim(), out var mapped)
                    && Enum.TryParse<MuscleRegion>(mapped, ignoreCase: true, out var region))
                {
                    return region;
                }
            }
        }

        var text = Normalize($"{name} {slug?.Replace('-', ' ').Replace('_', ' ')}");
        foreach (var (region, keywords) in Rules)
        {
            if (keywords.Any(k => ContainsWord(text, k)))
            {
                return region;
            }
        }

        return MuscleRegion.Other;
    }

    public static bool IsLarge(MuscleRegion region) => region is MuscleRegion.Chest or MuscleRegion.Back
        or MuscleRegion.Quadriceps or MuscleRegion.Hamstrings or MuscleRegion.Shoulders or MuscleRegion.Glutes;

    /// <summary>Kas gelisimi icin takip edilen bolgeler (kardiyo, tum vucut ve diger haric).</summary>
    public static readonly IReadOnlyList<MuscleRegion> Trainable = new[]
    {
        MuscleRegion.Chest, MuscleRegion.Back, MuscleRegion.Shoulders, MuscleRegion.Quadriceps, MuscleRegion.Hamstrings,
        MuscleRegion.Glutes, MuscleRegion.Biceps, MuscleRegion.Triceps, MuscleRegion.Calves, MuscleRegion.Core,
    };

    public static bool IsLowerBody(MuscleRegion region) => region is MuscleRegion.Quadriceps or MuscleRegion.Hamstrings
        or MuscleRegion.Glutes or MuscleRegion.Calves;

    public static string Label(MuscleRegion region) => region switch
    {
        MuscleRegion.Chest => "Göğüs",
        MuscleRegion.Back => "Sırt",
        MuscleRegion.Shoulders => "Omuz",
        MuscleRegion.Biceps => "Biceps",
        MuscleRegion.Triceps => "Triceps",
        MuscleRegion.Quadriceps => "Ön bacak (quadriceps)",
        MuscleRegion.Hamstrings => "Arka bacak (hamstring)",
        MuscleRegion.Glutes => "Kalça",
        MuscleRegion.Calves => "Baldır",
        MuscleRegion.Core => "Karın / core",
        MuscleRegion.Forearms => "Ön kol",
        MuscleRegion.FullBody => "Tüm vücut",
        MuscleRegion.Cardio => "Kardiyo",
        _ => "Diğer",
    };

    /// <summary>Kucuk harf, Turkce karakterleri ASCII'ye indirger, aksanlari kaldirir.</summary>
    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var lowered = value.ToLower(new CultureInfo("tr-TR"))
            .Replace('ı', 'i').Replace('ğ', 'g').Replace('ü', 'u').Replace('ş', 's').Replace('ö', 'o').Replace('ç', 'c');
        var decomposed = lowered.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var ch in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(char.IsLetterOrDigit(ch) ? ch : ' ');
            }
        }

        return " " + string.Join(' ', builder.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries)) + " ";
    }

    /// <summary>Kelime basinda eslesme ("lat" "lateral" icinde de eslesir; "kol" "kolon" icinde de) - bilincli olarak gevsek.</summary>
    internal static bool ContainsWord(string normalizedText, string keyword) =>
        normalizedText.Contains(" " + Normalize(keyword).Trim(), StringComparison.Ordinal);
}
