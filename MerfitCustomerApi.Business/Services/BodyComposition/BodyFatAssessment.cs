using MerfitCustomerApi.Business.Common;
using MerfitCustomerApi.Domain.Entities;
using MerfitCustomerApi.Domain.Entities.Enums;

namespace MerfitCustomerApi.Business.Services.BodyComposition;

/// <summary>Yag orani degerinin kaynagi.</summary>
public enum BodyFatSource
{
    /// <summary>Kullanicinin girdigi veya cihazla olculmus deger (BodyMeasurement.BodyFatPercentage).</summary>
    Measured,

    /// <summary>Cevre olcumlerinden hesaplanan tahmin (BodyMeasurement.EstimatedBodyFatPercentage).</summary>
    Estimated,
}

/// <summary>Yag oraninin yas/cinsiyete gore yapilandirilmis araliga gore konumu.</summary>
public enum BodyFatCategory
{
    /// <summary>Yeterli veri yok (yag orani, yas veya cinsiyet eksik); hicbir sonuc uretilmez.</summary>
    Unknown,
    Low,
    Healthy,
    High,
}

/// <summary>Degerlendirmede kullanilan tek yag orani okumasi.</summary>
public sealed record BodyFatReading(
    decimal Percentage,
    BodyFatSource Source,
    string? Method,
    DateTime RecordedAt,
    decimal MarginOfErrorPercent);

/// <summary>Kullanicinin guncel vucut yag orani degerlendirmesi.</summary>
public sealed class BodyFatAssessment
{
    public static readonly BodyFatAssessment Unknown = new() { Category = BodyFatCategory.Unknown };

    public BodyFatReading? Reading { get; init; }

    public BodyFatCategory Category { get; init; }

    /// <summary>Kullanilan bandin alt siniri (bunun altinda dusuk); band bulunamadiysa null.</summary>
    public decimal? LowThreshold { get; init; }

    /// <summary>Kullanilan bandin ust siniri (bunun ustunde yuksek); band bulunamadiysa null.</summary>
    public decimal? HighThreshold { get; init; }

    /// <summary>Degerlendirme yapilamadiysa eksik bilgiler ("bodyFat", "age", "gender", "height", "neck", "waist", "hip").</summary>
    public IReadOnlyList<string> Missing { get; init; } = Array.Empty<string>();

    public bool IsLow => Category == BodyFatCategory.Low;

    public bool IsHigh => Category == BodyFatCategory.High;
}

/// <summary>
/// Saf (veritabanindan bagimsiz) vucut yag orani degerlendirmesi. API, plan uretimi ve kayit akisi ayni
/// kurallari bu sinif uzerinden kullanir; mobil istemcideki karsiligi src/shared/bodyComposition'dadir.
///
/// Kurallar:
/// 1. Once son <see cref="BodyCompositionOptions.MeasuredBodyFatMaxAgeDays"/> gun icindeki en yeni
///    girilen/olculen yag orani kullanilir.
/// 2. Yoksa son <see cref="BodyCompositionOptions.CircumferenceMaxAgeDays"/> gun icindeki en yeni cevre
///    olcumunden US Navy tahmini kullanilir (kayitta saklanan tahmin; yoksa profildeki boyla hesaplanir).
/// 3. Yas (dogum tarihi), erkek/kadin cinsiyet veya bir okuma yoksa kategori <see cref="BodyFatCategory.Unknown"/>
///    olur: eksik bilgi yuzunden dusuk yag orani sonucu URETILMEZ.
/// 4. Okuma, yas/cinsiyet bandinin alt sinirinin altindaysa Low, ust sinirinin ustundeyse High.
/// </summary>
public static class BodyFatAssessor
{
    public static BodyFatAssessment Assess(
        Gender? gender,
        DateTime? dateOfBirth,
        decimal? heightCm,
        IEnumerable<BodyMeasurement> measurements,
        DateTime nowUtc,
        BodyCompositionOptions options)
    {
        var missing = new List<string>();
        var reading = SelectReading(gender, heightCm, measurements, nowUtc, options, missing);

        if (gender is not (Gender.Male or Gender.Female))
        {
            missing.Add("gender");
        }

        int? age = dateOfBirth.HasValue ? CalculateAge(dateOfBirth.Value, nowUtc) : null;
        if (age is null || age < options.MinimumAssessmentAge)
        {
            // Yas bilinmiyor veya degerlendirme yasinin altinda: aralik secilmez.
            missing.Add("age");
        }

        var range = age >= options.MinimumAssessmentAge && (gender is Gender.Male or Gender.Female)
            ? options.EffectiveRanges.FirstOrDefault(r => r.Gender == gender && age >= r.MinAge && age <= r.MaxAge)
            : null;

        if (reading is null || range is null)
        {
            return new BodyFatAssessment
            {
                Reading = reading,
                Category = BodyFatCategory.Unknown,
                LowThreshold = range?.LowBelow,
                HighThreshold = range?.HighAbove,
                Missing = missing.Distinct().ToList(),
            };
        }

        var category = reading.Percentage < range.LowBelow
            ? BodyFatCategory.Low
            : reading.Percentage > range.HighAbove
                ? BodyFatCategory.High
                : BodyFatCategory.Healthy;

        return new BodyFatAssessment
        {
            Reading = reading,
            Category = category,
            LowThreshold = range.LowBelow,
            HighThreshold = range.HighAbove,
        };
    }

    public static BodyFatAssessment Assess(
        UserProfile profile,
        IEnumerable<BodyMeasurement> measurements,
        DateTime nowUtc,
        BodyCompositionOptions options) =>
        Assess(profile.Gender, profile.DateOfBirth, profile.HeightCm, measurements, nowUtc, options);

    private static BodyFatReading? SelectReading(
        Gender? gender,
        decimal? heightCm,
        IEnumerable<BodyMeasurement> measurements,
        DateTime nowUtc,
        BodyCompositionOptions options,
        List<string> missing)
    {
        var ordered = measurements
            .Where(m => m.RecordedAt <= nowUtc.AddDays(1))
            .OrderByDescending(m => m.RecordedAt)
            .ThenByDescending(m => m.Id)
            .ToList();

        var measuredCutoff = nowUtc.AddDays(-options.MeasuredBodyFatMaxAgeDays);
        var measured = ordered.FirstOrDefault(m => m.BodyFatPercentage is > 0 && m.RecordedAt >= measuredCutoff);
        if (measured is not null)
        {
            return new BodyFatReading(
                measured.BodyFatPercentage!.Value, BodyFatSource.Measured, null, measured.RecordedAt, options.MeasuredMarginOfErrorPercent);
        }

        var circumferenceCutoff = nowUtc.AddDays(-options.CircumferenceMaxAgeDays);
        foreach (var measurement in ordered.Where(m => m.RecordedAt >= circumferenceCutoff))
        {
            var estimate = measurement.EstimatedBodyFatPercentage
                ?? BodyFatEstimator.EstimateUsNavy(gender, heightCm, measurement.NeckCm, measurement.WaistCm, measurement.HipCm);
            if (estimate is not null)
            {
                return new BodyFatReading(
                    estimate.Value,
                    BodyFatSource.Estimated,
                    measurement.BodyFatEstimationMethod ?? BodyFatEstimator.UsNavyMethod,
                    measurement.RecordedAt,
                    options.EstimatedMarginOfErrorPercent);
            }
        }

        missing.Add("bodyFat");
        if (heightCm is not > 0)
        {
            missing.Add("height");
        }

        var latest = ordered.FirstOrDefault(m => m.RecordedAt >= circumferenceCutoff);
        if (latest?.NeckCm is null) missing.Add("neck");
        if (latest?.WaistCm is null) missing.Add("waist");
        if (gender == Gender.Female && latest?.HipCm is null) missing.Add("hip");

        return null;
    }

    public static int CalculateAge(DateTime dateOfBirth, DateTime nowUtc)
    {
        var today = nowUtc.Date;
        var age = today.Year - dateOfBirth.Year;
        if (today < dateOfBirth.Date.AddYears(age))
        {
            age--;
        }

        return age;
    }
}
