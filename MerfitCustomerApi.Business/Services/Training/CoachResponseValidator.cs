using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MerfitCustomerApi.Business.Services.Training;

/// <summary>
/// AI yanitinin backend dogrulamasi. AI yalnizca motorun verdigi gercekleri aciklayabilir:
/// - Yanit JSON olmali: {"message": "...", "exerciseIds": [..]}.
/// - exerciseIds yalnizca baglamda verilen egzersizlerden olabilir.
/// - Metinde veritabanindaki ama baglamda olmayan bir egzersiz adi gecemez (uydurma / baska egzersiz onerisi).
/// - Metindeki her "X kg" degeri baglamdaki agirliklardan biri olmali (uydurma agirlik yok).
/// - Tibbi teshis ifadeleri kullanilamaz.
/// Dogrulanamayan yanit kullaniciya gosterilmez; kural tabanli metin doner.
/// </summary>
public static partial class CoachResponseValidator
{
    private static readonly string[] MedicalTerms =
    {
        "teshis", "tani koy", "yirtik", "tendinit", "fitik", "kirik", "menisk", "ilac", "agri kesici", "diagnos", "tear",
    };

    public sealed record Result(bool IsValid, string? Message, IReadOnlyList<long> ExerciseIds, string? Reason);

    public static Result Validate(
        string raw,
        IReadOnlySet<long> allowedExerciseIds,
        IReadOnlyDictionary<long, string> allCatalogNames,
        IReadOnlySet<decimal> allowedWeights,
        int maxLength = 1500)
    {
        var json = ExtractJson(raw);
        if (json is null)
        {
            return new Result(false, null, Array.Empty<long>(), "Yanıt JSON değil.");
        }

        string? message;
        List<long> ids;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            message = root.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String ? m.GetString() : null;
            ids = root.TryGetProperty("exerciseIds", out var e) && e.ValueKind == JsonValueKind.Array
                ? e.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.Number && x.TryGetInt64(out _)).Select(x => x.GetInt64()).Distinct().ToList()
                : new List<long>();
        }
        catch (JsonException)
        {
            return new Result(false, null, Array.Empty<long>(), "Yanıt JSON olarak okunamadı.");
        }

        if (string.IsNullOrWhiteSpace(message))
        {
            return new Result(false, null, Array.Empty<long>(), "Mesaj boş.");
        }

        message = message.Trim();
        if (message.Length > maxLength)
        {
            return new Result(false, null, Array.Empty<long>(), "Mesaj çok uzun.");
        }

        var unknownIds = ids.Where(id => !allowedExerciseIds.Contains(id)).ToList();
        if (unknownIds.Count > 0)
        {
            return new Result(false, null, Array.Empty<long>(), $"Bağlamda olmayan egzersiz kimliği: {string.Join(",", unknownIds)}.");
        }

        var normalized = MuscleRegionMapper.Normalize(message);
        foreach (var (id, name) in allCatalogNames)
        {
            if (allowedExerciseIds.Contains(id)) continue;
            var n = MuscleRegionMapper.Normalize(name).Trim();
            if (n.Length >= 5 && normalized.Contains(" " + n + " ", StringComparison.Ordinal))
            {
                // Ayni ad baglamdaki bir egzersizin adinin parcasiysa (orn. "Bench Press" / "Incline Bench Press") sorun yok.
                var partOfAllowed = allowedExerciseIds.Any(a => allCatalogNames.TryGetValue(a, out var an)
                    && MuscleRegionMapper.Normalize(an).Contains(" " + n + " ", StringComparison.Ordinal));
                if (!partOfAllowed)
                {
                    return new Result(false, null, Array.Empty<long>(), $"Bağlamda olmayan egzersiz adı: {name}.");
                }
            }
        }

        foreach (Match match in KgRegex().Matches(message))
        {
            var value = decimal.Parse(match.Groups[1].Value.Replace(',', '.'), CultureInfo.InvariantCulture);
            if (!allowedWeights.Contains(value))
            {
                return new Result(false, null, Array.Empty<long>(), $"Bağlamda olmayan ağırlık: {match.Value}.");
            }
        }

        if (MedicalTerms.Any(t => normalized.Contains(" " + t, StringComparison.Ordinal)))
        {
            return new Result(false, null, Array.Empty<long>(), "Tıbbi teşhis ifadesi.");
        }

        return new Result(true, message, ids, null);
    }

    private static string? ExtractJson(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var start = raw.IndexOf('{');
        var end = raw.LastIndexOf('}');
        return start >= 0 && end > start ? raw[start..(end + 1)] : null;
    }

    [GeneratedRegex(@"(\d+(?:[.,]\d+)?)\s*kg", RegexOptions.IgnoreCase)]
    private static partial Regex KgRegex();
}
