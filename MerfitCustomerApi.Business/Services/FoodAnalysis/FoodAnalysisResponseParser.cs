using System.Globalization;
using System.Text;
using System.Text.Json;

namespace MerfitCustomerApi.Business.Services.FoodAnalysis;

/// <summary>
/// Goruntu modelinin yapilandirilmis ciktisini dogrular. Modelin serbest metni hicbir zaman kullaniciya ya da
/// veritabanina gitmez; yalnizca semaya uyan, aralik ve tutarlilik kontrollerinden gecen alanlar kullanilir.
/// Gecersiz tek bir kalem atlanir ve uyari eklenir; yanit butunuyle okunamiyorsa
/// <see cref="FoodAnalysisInvalidResponseException"/> firlatilir.
/// </summary>
public static class FoodAnalysisResponseParser
{
    public const int MaxNameLength = 80;
    public const decimal MaxGrams = 3000m;
    private const int MaxUnidentified = 5;

    public static ParsedFoodAnalysis Parse(string json, int maxItems)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(StripCodeFence(json), new JsonDocumentOptions { MaxDepth = 16 });
        }
        catch (JsonException ex)
        {
            throw new FoodAnalysisInvalidResponseException("invalid_json", ex);
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                throw new FoodAnalysisInvalidResponseException("root_not_object");
            }
            if (!root.TryGetProperty("containsFood", out var containsFoodElement)
                || containsFoodElement.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            {
                throw new FoodAnalysisInvalidResponseException("missing_containsFood");
            }

            var warnings = new List<string>();
            var items = new List<ParsedFoodItem>();
            var rawItemCount = 0;
            if (root.TryGetProperty("items", out var itemsElement))
            {
                if (itemsElement.ValueKind != JsonValueKind.Array)
                {
                    throw new FoodAnalysisInvalidResponseException("items_not_array");
                }
                foreach (var element in itemsElement.EnumerateArray())
                {
                    rawItemCount++;
                    if (items.Count >= maxItems) continue;
                    if (TryParseItem(element) is { } item) items.Add(item);
                }
            }

            var invalidCount = Math.Min(rawItemCount, maxItems) - items.Count;
            if (invalidCount > 0)
            {
                warnings.Add(invalidCount == 1
                    ? "Bir yiyeceğin değerleri tutarsız geldiği için listeden çıkarıldı."
                    : $"{invalidCount} yiyeceğin değerleri tutarsız geldiği için listeden çıkarıldı.");
            }
            if (rawItemCount > maxItems)
            {
                warnings.Add($"Fotoğrafta çok sayıda yiyecek var; yalnızca ilk {maxItems} tanesi listelendi.");
            }

            var containsFood = containsFoodElement.GetBoolean();
            if (containsFood && rawItemCount > 0 && items.Count == 0)
            {
                // Model yiyecek buldugunu soyleyip hicbir gecerli kalem vermediyse sonuc guvenilmez.
                throw new FoodAnalysisInvalidResponseException("no_valid_items");
            }

            var unidentified = new List<string>();
            if (root.TryGetProperty("unidentifiedItems", out var unidentifiedElement) && unidentifiedElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var element in unidentifiedElement.EnumerateArray())
                {
                    if (unidentified.Count >= MaxUnidentified) break;
                    if (element.ValueKind == JsonValueKind.String && CleanText(element.GetString()) is { Length: > 0 } text)
                    {
                        unidentified.Add(text);
                    }
                }
            }

            var mealName = root.TryGetProperty("mealName", out var mealNameElement) && mealNameElement.ValueKind == JsonValueKind.String
                ? CleanText(mealNameElement.GetString())
                : string.Empty;
            if (mealName.Length == 0)
            {
                mealName = CleanText(string.Join(", ", items.Select(i => i.Name)));
            }

            var overall = ReadNumber(root, "overallConfidence") is { } c
                ? NormalizeConfidence(c)
                : items.Count == 0 ? 0 : Math.Round(items.Average(i => i.Confidence), 2);

            return new ParsedFoodAnalysis(containsFood && items.Count > 0, mealName, items, unidentified, overall ?? 0, warnings);
        }
    }

    private static ParsedFoodItem? TryParseItem(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object) return null;

        var name = element.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String ? CleanText(n.GetString()) : string.Empty;
        if (name.Length == 0) return null;
        var genericName = element.TryGetProperty("genericName", out var g) && g.ValueKind == JsonValueKind.String ? CleanText(g.GetString()) : string.Empty;

        if (ReadNumber(element, "estimatedGrams") is not { } grams || grams < 1 || grams > MaxGrams) return null;
        if (ReadNumber(element, "caloriesPer100g") is not { } kcal || kcal < 0 || kcal > 900) return null;
        if (ReadNumber(element, "proteinPer100g") is not { } protein || protein < 0 || protein > 100) return null;
        if (ReadNumber(element, "carbsPer100g") is not { } carbs || carbs < 0 || carbs > 100) return null;
        if (ReadNumber(element, "fatPer100g") is not { } fat || fat < 0 || fat > 100) return null;
        if (protein + carbs + fat > 105) return null;

        // Enerji tutarliligi (Atwater): 4 kcal/g protein ve karbonhidrat, 9 kcal/g yag. Lif ve alkol kucuk sapma yaratir.
        var atwater = 4 * protein + 4 * carbs + 9 * fat;
        if (Math.Abs(kcal - atwater) > Math.Max(40m, kcal * 0.4m)) return null;

        var min = ReadNumber(element, "minGrams") ?? grams;
        var max = ReadNumber(element, "maxGrams") ?? grams;
        min = Math.Clamp(min, 1, grams);
        max = Math.Clamp(max, grams, MaxGrams);

        var uncertainty = element.TryGetProperty("portionUncertainty", out var u) && u.ValueKind == JsonValueKind.String
            ? u.GetString()?.Trim().ToLowerInvariant()
            : null;
        if (uncertainty is not ("low" or "medium" or "high"))
        {
            uncertainty = "medium";
        }

        var note = element.TryGetProperty("note", out var noteElement) && noteElement.ValueKind == JsonValueKind.String
            ? CleanText(noteElement.GetString(), 160)
            : string.Empty;

        return new ParsedFoodItem(
            name,
            genericName.Length == 0 ? name : genericName,
            Math.Round(grams, 0),
            Math.Round(min, 0),
            Math.Round(max, 0),
            Math.Round(kcal, 1),
            Math.Round(protein, 1),
            Math.Round(carbs, 1),
            Math.Round(fat, 1),
            NormalizeConfidence(ReadNumber(element, "confidence")) ?? 0.5m,
            uncertainty,
            note.Length == 0 ? null : note);
    }

    /// <summary>Guven 0-1 araligina indirgenir; model yuzde (orn. 85) dondururse 0.85 kabul edilir.</summary>
    private static decimal? NormalizeConfidence(decimal? value) => value switch
    {
        null => null,
        < 0 => 0,
        <= 1 => Math.Round(value.Value, 2),
        <= 100 => Math.Round(value.Value / 100, 2),
        _ => 1,
    };

    private static decimal? ReadNumber(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value)) return null;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var number)) return number;
        // Bazi modeller sayiyi metin olarak dondurur ("150").
        if (value.ValueKind == JsonValueKind.String
            && decimal.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }
        return null;
    }

    /// <summary>Kontrol karakterlerini atar, bosluklari sadelestirir ve uzunlugu sinirlar.</summary>
    internal static string CleanText(string? text, int maxLength = MaxNameLength)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        var builder = new StringBuilder(text.Length);
        var lastWasSpace = false;
        foreach (var ch in text.Trim())
        {
            if (char.IsControl(ch) || char.IsWhiteSpace(ch))
            {
                if (!lastWasSpace && builder.Length > 0) builder.Append(' ');
                lastWasSpace = true;
                continue;
            }
            if (ch is '<' or '>') continue;
            builder.Append(ch);
            lastWasSpace = false;
        }
        var clean = builder.ToString().Trim();
        return clean.Length > maxLength ? clean[..maxLength].TrimEnd() : clean;
    }

    private static string StripCodeFence(string json)
    {
        var text = (json ?? string.Empty).Trim();
        if (!text.StartsWith("```", StringComparison.Ordinal)) return text;
        var firstNewLine = text.IndexOf('\n');
        var lastFence = text.LastIndexOf("```", StringComparison.Ordinal);
        return firstNewLine > 0 && lastFence > firstNewLine ? text[(firstNewLine + 1)..lastFence].Trim() : text;
    }
}

/// <param name="ContainsFood">Fotografta gecerli en az bir yemek tanindi mi?</param>
public sealed record ParsedFoodAnalysis(
    bool ContainsFood,
    string MealName,
    IReadOnlyList<ParsedFoodItem> Items,
    IReadOnlyList<string> UnidentifiedItems,
    decimal OverallConfidence,
    IReadOnlyList<string> Warnings);

/// <summary>Dogrulanmis tek kalem; besin degerleri 100 g icindir.</summary>
public sealed record ParsedFoodItem(
    string Name,
    string GenericName,
    decimal EstimatedGrams,
    decimal MinGrams,
    decimal MaxGrams,
    decimal CaloriesPer100g,
    decimal ProteinPer100g,
    decimal CarbsPer100g,
    decimal FatPer100g,
    decimal Confidence,
    string PortionUncertainty,
    string? Note);

/// <summary>Model yaniti semaya uymuyor ya da okunamiyor. Mesaj yalnizca kisa bir kod tasir, yanit icerigi tasimaz.</summary>
public sealed class FoodAnalysisInvalidResponseException : Exception
{
    public FoodAnalysisInvalidResponseException(string reason, Exception? innerException = null)
        : base($"Gecersiz goruntu analizi yaniti: {reason}", innerException)
    {
        Reason = reason;
    }

    public string Reason { get; }
}
