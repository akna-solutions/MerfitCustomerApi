using System.Globalization;
using System.Net;
using System.Text.Json;
using MerfitCustomerApi.Business.Interfaces.Services;
using MerfitCustomerApi.Domain.Exceptions;
using Microsoft.Extensions.Logging;

namespace MerfitCustomerApi.Business.Services.Foods;

/// <summary>
/// Open Food Facts (ucretsiz, acik veri) uzerinden barkodla urun sorgular. Yalnizca urunun kendi
/// besin tablosundaki 100 g/ml degerlerini kullanir; eksik kalori bilgisini tahmin etmez.
/// HttpClient'in BaseAddress/User-Agent/Timeout ayarlari Program.cs'te OpenFoodFactsOptions'tan yapilir.
/// </summary>
public class OpenFoodFactsProductLookup : IFoodProductLookup
{
    private const string Fields =
        "code,product_name,product_name_tr,generic_name,brands,nutriments,image_front_small_url,product_quantity_unit";

    /// <summary>100 g basina saf yagin enerjisi ~900 kcal; ustu hatali veri kabul edilir.</summary>
    private const decimal MaxPlausibleKcalPer100 = 900m;

    private const decimal KilojoulesPerKilocalorie = 4.184m;

    private readonly HttpClient _httpClient;
    private readonly ILogger<OpenFoodFactsProductLookup> _logger;

    public OpenFoodFactsProductLookup(HttpClient httpClient, ILogger<OpenFoodFactsProductLookup> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<ExternalFoodProduct?> FindByBarcodeAsync(string barcode, CancellationToken cancellationToken = default)
    {
        HttpResponseMessage response;
        try
        {
            response = await _httpClient.GetAsync($"api/v2/product/{barcode}.json?fields={Fields}", cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "Open Food Facts'e ulasilamadi. Barkod: {Barcode}", barcode);
            throw new ExternalServiceUnavailableException("Barkod servisine su an ulasilamiyor. Lutfen daha sonra tekrar deneyin.", ex);
        }

        using (response)
        {
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Open Food Facts {StatusCode} dondu. Barkod: {Barcode}", (int)response.StatusCode, barcode);
                throw new ExternalServiceUnavailableException("Barkod servisine su an ulasilamiyor. Lutfen daha sonra tekrar deneyin.");
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            return Parse(barcode, document.RootElement);
        }
    }

    /// <summary>Open Food Facts v2 urun yanitini ExternalFoodProduct'a cevirir; kullanilamaz veride null doner.</summary>
    private static ExternalFoodProduct? Parse(string barcode, JsonElement root)
    {
        if (root.TryGetProperty("status", out var status) && ReadDecimal(status) == 0m)
        {
            return null;
        }

        if (!root.TryGetProperty("product", out var product) || product.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var name = FirstNonEmpty(ReadString(product, "product_name_tr"), ReadString(product, "product_name"), ReadString(product, "generic_name"));
        if (name is null)
        {
            return null;
        }

        if (!product.TryGetProperty("nutriments", out var nutriments) || nutriments.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var kcal = ReadNutriment(nutriments, "energy-kcal_100g");
        if (kcal is null)
        {
            var kilojoules = ReadNutriment(nutriments, "energy-kj_100g") ?? ReadNutriment(nutriments, "energy_100g");
            kcal = kilojoules / KilojoulesPerKilocalorie;
        }

        if (kcal is null or < 0 or > MaxPlausibleKcalPer100)
        {
            return null;
        }

        var protein = ReadNutriment(nutriments, "proteins_100g");
        var carbs = ReadNutriment(nutriments, "carbohydrates_100g");
        var fat = ReadNutriment(nutriments, "fat_100g");
        if (protein < 0 || carbs < 0 || fat < 0)
        {
            return null;
        }

        var sodiumGrams = ReadNutriment(nutriments, "sodium_100g");
        var brand = ReadString(product, "brands")?.Split(',')[0].Trim();
        var unit = string.Equals(ReadString(product, "product_quantity_unit"), "ml", StringComparison.OrdinalIgnoreCase) ? "ml" : "g";

        return new ExternalFoodProduct
        {
            Barcode = barcode,
            Name = Truncate(name, 200),
            Brand = string.IsNullOrEmpty(brand) ? null : Truncate(brand, 200),
            Unit = unit,
            CaloriesPer100 = Math.Round(kcal.Value, 2),
            ProteinPer100 = Math.Round(protein ?? 0, 2),
            CarbsPer100 = Math.Round(carbs ?? 0, 2),
            FatPer100 = Math.Round(fat ?? 0, 2),
            FiberPer100 = RoundOrNull(ReadNutriment(nutriments, "fiber_100g")),
            SugarPer100 = RoundOrNull(ReadNutriment(nutriments, "sugars_100g")),
            SodiumMgPer100 = sodiumGrams is null ? null : Math.Round(sodiumGrams.Value * 1000m, 2),
            ImageUrl = ReadString(product, "image_front_small_url") is { Length: <= 500 } image ? image : null,
        };
    }

    private static decimal? ReadNutriment(JsonElement nutriments, string key)
    {
        return nutriments.TryGetProperty(key, out var value) ? ReadDecimal(value) : null;
    }

    /// <summary>Open Food Facts degerleri bazen sayi, bazen metin olarak doner.</summary>
    private static decimal? ReadDecimal(JsonElement value)
    {
        return value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetDecimal(out var number) => number,
            JsonValueKind.String when decimal.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) => parsed,
            _ => null,
        };
    }

    private static string? ReadString(JsonElement element, string key)
    {
        if (!element.TryGetProperty(key, out var value) || value.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var text = value.GetString()?.Trim();
        return string.IsNullOrEmpty(text) ? null : text;
    }

    private static string? FirstNonEmpty(params string?[] values) => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));

    private static string Truncate(string value, int maxLength) => value.Length <= maxLength ? value : value[..maxLength];

    private static decimal? RoundOrNull(decimal? value) => value is null or < 0 ? null : Math.Round(value.Value, 2);
}
