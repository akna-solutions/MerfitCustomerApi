using System.Text.Json;
using System.Text.Json.Serialization;

namespace MerfitCustomerApi.Business.Services.Store;

/// <summary>Urun varyant grubu (Product.VariantsJson); mobil uygulamadaki VariantGroup ile ayni sekildedir.</summary>
public class StoreVariantGroup
{
    public string Id { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;

    /// <summary>"size" | "color" | "package" | "option".</summary>
    public string Kind { get; set; } = "option";

    public List<StoreVariantOption> Options { get; set; } = new();
}

public class StoreVariantOption
{
    public string Id { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;

    /// <summary>Secenegin birim fiyata (hem liste hem satis fiyatina) ekledigi tutar (TL); negatif olabilir.</summary>
    public decimal PriceDelta { get; set; }

    /// <summary>false ise secenek satisa kapalidir.</summary>
    public bool InStock { get; set; } = true;

    /// <summary>Renk secenegi icin #RRGGBB.</summary>
    public string? Swatch { get; set; }
}

public class StoreSpec
{
    public string Label { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}

/// <summary>Bir varyant seciminin sunucudaki cozumu.</summary>
public sealed record StoreVariantSelection(bool IsValid, string? Error, decimal PriceDelta, string Label, string Key, Dictionary<string, string> Selected);

/// <summary>Store JSON kolonlarinin (jsonb) okunup yazilmasi. Bozuk JSON hata yerine bos liste olarak okunur.</summary>
public static class StoreJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static List<T> ReadList<T>(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new List<T>();
        try
        {
            return JsonSerializer.Deserialize<List<T>>(json, Options) ?? new List<T>();
        }
        catch (JsonException)
        {
            return new List<T>();
        }
    }

    public static Dictionary<string, string> ReadMap(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new Dictionary<string, string>();
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(json, Options) ?? new Dictionary<string, string>();
        }
        catch (JsonException)
        {
            return new Dictionary<string, string>();
        }
    }

    public static string Write<T>(T value) => JsonSerializer.Serialize(value, Options);
}
