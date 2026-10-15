using MerfitCustomerApi.Domain.Entities.Enums;

namespace MerfitCustomerApi.Business.Services.BodyComposition;

/// <summary>
/// Cevre olcumlerinden vucut yag orani tahmini: US Navy (Hodgdon ve Beckett, 1984) yontemi. ABD
/// Savunma Bakanligi vucut kompozisyonu standardinda (DoD Instruction 1308.3) kullanilan formulun
/// santimetre ile yazilmis hali:
///
///     Erkek: %YO = 495 / (1.0324 - 0.19077 * log10(bel - boyun) + 0.15456 * log10(boy)) - 450
///     Kadin: %YO = 495 / (1.29579 - 0.35004 * log10(bel + kalca - boyun) + 0.22100 * log10(boy)) - 450
///
/// Formul yalnizca erkek/kadin icin tanimlidir; cinsiyet belirtilmemisse veya gerekli olcumlerden biri
/// eksikse tahmin uretilmez (null). BMI veya yalnizca kilo kullanilmaz. Referans yontemlere gore hata payi
/// yaklasik 3-4 yuzde puandir; sonuc her zaman "tahmini" olarak etiketlenir.
/// </summary>
public static class BodyFatEstimator
{
    /// <summary>Saklanan yontem adi.</summary>
    public const string UsNavyMethod = "UsNavy";

    /// <summary>Gecerli kabul edilen en dusuk tahmin. Altindaki sonuc olcum hatasi kabul edilir.</summary>
    public const decimal MinPlausiblePercentage = 2m;

    /// <summary>Gecerli kabul edilen en yuksek tahmin.</summary>
    public const decimal MaxPlausiblePercentage = 75m;

    /// <summary>Tahmin icin gereken olcumleri (cinsiyete gore) dondurur.</summary>
    public static IReadOnlyList<string> RequiredMeasurements(Gender? gender) => gender switch
    {
        Gender.Male => new[] { "height", "neck", "waist" },
        Gender.Female => new[] { "height", "neck", "waist", "hip" },
        _ => Array.Empty<string>(),
    };

    /// <summary>
    /// US Navy tahmini; 1 ondalik basamaga yuvarlanir. Hesaplanamiyorsa veya sonuc makul aralik disindaysa null.
    /// </summary>
    public static decimal? EstimateUsNavy(Gender? gender, decimal? heightCm, decimal? neckCm, decimal? waistCm, decimal? hipCm)
    {
        if (heightCm is not > 0 || neckCm is not > 0 || waistCm is not > 0)
        {
            return null;
        }

        double result;
        var height = (double)heightCm.Value;
        var neck = (double)neckCm.Value;
        var waist = (double)waistCm.Value;

        switch (gender)
        {
            case Gender.Male:
            {
                var girth = waist - neck;
                if (girth <= 0)
                {
                    return null;
                }

                result = 495d / (1.0324d - (0.19077d * Math.Log10(girth)) + (0.15456d * Math.Log10(height))) - 450d;
                break;
            }
            case Gender.Female:
            {
                if (hipCm is not > 0)
                {
                    return null;
                }

                var girth = waist + (double)hipCm.Value - neck;
                if (girth <= 0)
                {
                    return null;
                }

                result = 495d / (1.29579d - (0.35004d * Math.Log10(girth)) + (0.22100d * Math.Log10(height))) - 450d;
                break;
            }
            default:
                return null;
        }

        if (double.IsNaN(result) || double.IsInfinity(result))
        {
            return null;
        }

        var rounded = Math.Round((decimal)result, 1);
        return rounded is < MinPlausiblePercentage or > MaxPlausiblePercentage ? null : rounded;
    }
}
