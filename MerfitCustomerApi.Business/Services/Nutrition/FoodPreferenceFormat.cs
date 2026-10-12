using MerfitCustomerApi.Domain.Entities.Enums;

namespace MerfitCustomerApi.Business.Services.Nutrition;

/// <summary>
/// Kullanicinin beslenme tercihlerinin (dislanan besinler, alerjiler) veritabani ve API bicimleri
/// arasindaki donusumu. ProfileService ve NutritionPlanGenerator ayni kurali kullanir.
/// </summary>
public static class FoodPreferenceFormat
{
    /// <summary>Bir kullanicinin dislayabilecegi en fazla besin sayisi (UserProfile.ExcludedFoodIds kolon boyutuyla uyumlu).</summary>
    public const int MaxExcludedFoods = 100;

    private const char Separator = ',';

    /// <summary>"12,45" bicimindeki degeri kimlik kumesine cevirir; gecersiz parcalari yok sayar.</summary>
    public static IReadOnlySet<long> ParseExcludedFoodIds(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return new HashSet<long>();
        }

        var ids = new HashSet<long>();
        foreach (var part in value.Split(Separator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (long.TryParse(part, out var id) && id > 0)
            {
                ids.Add(id);
            }
        }

        return ids;
    }

    /// <summary>Kimlikleri tekillestirip artan sirada "12,45" bicimine cevirir; bossa null doner.</summary>
    public static string? FormatExcludedFoodIds(IEnumerable<long> ids)
    {
        var ordered = ids.Where(id => id > 0).Distinct().OrderBy(id => id).ToList();
        return ordered.Count == 0 ? null : string.Join(Separator, ordered);
    }

    /// <summary>Alerji bayraklarini API'de kullanilan camelCase ad listesine cevirir ("gluten", "treeNuts").</summary>
    public static List<string> AllergensToNames(FoodAllergen? allergens)
    {
        if (!allergens.HasValue || allergens.Value == FoodAllergen.None)
        {
            return new List<string>();
        }

        return Enum.GetValues<FoodAllergen>()
            .Where(flag => flag != FoodAllergen.None && allergens.Value.HasFlag(flag))
            .Select(flag => char.ToLowerInvariant(flag.ToString()[0]) + flag.ToString()[1..])
            .ToList();
    }

    /// <summary>
    /// API'den gelen alerji adlarini bayraklara cevirir. Bilinmeyen ad varsa false doner ve
    /// <paramref name="unknown"/> ilk gecersiz adi tasir.
    /// </summary>
    public static bool TryParseAllergens(IEnumerable<string> names, out FoodAllergen allergens, out string? unknown)
    {
        allergens = FoodAllergen.None;
        unknown = null;

        foreach (var name in names)
        {
            if (!Enum.TryParse<FoodAllergen>(name?.Trim(), ignoreCase: true, out var flag)
                || flag == FoodAllergen.None
                || !Enum.IsDefined(flag)
                || int.TryParse(name, out _))
            {
                unknown = name;
                return false;
            }

            allergens |= flag;
        }

        return true;
    }
}
