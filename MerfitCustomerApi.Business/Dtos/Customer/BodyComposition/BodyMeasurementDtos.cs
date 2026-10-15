using System.ComponentModel.DataAnnotations;
using MerfitCustomerApi.Business.Services.BodyComposition;

namespace MerfitCustomerApi.Business.Dtos.Customer.BodyComposition;

/// <summary>
/// POST /api/body-measurements govdesi. Her istek YENI bir olcum kaydi olusturur; eski kayitlar
/// guncellenmez veya silinmez. Tum degerler metrik birimdedir (kg, cm, %); en az bir alan dolu olmalidir.
/// Boy olcum kaydinda tutulmaz, profildeki boy kullanilir.
/// </summary>
public class CreateBodyMeasurementRequest
{
    /// <summary>"yyyy-MM-dd"; verilmezse simdiki zaman.</summary>
    public DateOnly? Date { get; set; }

    [Range(BodyMeasurementLimits.MinWeightKg, BodyMeasurementLimits.MaxWeightKg, ErrorMessage = "Kilo 20-400 kg arasında olmalıdır.")]
    public decimal? WeightKg { get; set; }

    /// <summary>Kullanicinin girdigi veya bir cihazdan okudugu yag orani (%). Tahmin buraya gonderilmez.</summary>
    [Range(BodyMeasurementLimits.MinBodyFatPercentage, BodyMeasurementLimits.MaxBodyFatPercentage, ErrorMessage = "Vücut yağ oranı %2-75 arasında olmalıdır.")]
    public decimal? BodyFatPercentage { get; set; }

    [Range(BodyMeasurementLimits.MinNeckCm, BodyMeasurementLimits.MaxNeckCm, ErrorMessage = "Boyun çevresi 20-80 cm arasında olmalıdır.")]
    public decimal? NeckCm { get; set; }

    [Range(BodyMeasurementLimits.MinChestCm, BodyMeasurementLimits.MaxChestCm, ErrorMessage = "Göğüs çevresi 50-250 cm arasında olmalıdır.")]
    public decimal? ChestCm { get; set; }

    [Range(BodyMeasurementLimits.MinWaistCm, BodyMeasurementLimits.MaxWaistCm, ErrorMessage = "Bel çevresi 40-250 cm arasında olmalıdır.")]
    public decimal? WaistCm { get; set; }

    [Range(BodyMeasurementLimits.MinHipCm, BodyMeasurementLimits.MaxHipCm, ErrorMessage = "Kalça çevresi 50-250 cm arasında olmalıdır.")]
    public decimal? HipCm { get; set; }

    [Range(BodyMeasurementLimits.MinArmCm, BodyMeasurementLimits.MaxArmCm, ErrorMessage = "Kol çevresi 15-80 cm arasında olmalıdır.")]
    public decimal? ArmCm { get; set; }

    [Range(BodyMeasurementLimits.MinThighCm, BodyMeasurementLimits.MaxThighCm, ErrorMessage = "Uyluk çevresi 25-150 cm arasında olmalıdır.")]
    public decimal? ThighCm { get; set; }
}

/// <summary>Tek bir olcum kaydi.</summary>
public class BodyMeasurementDto
{
    public long Id { get; set; }
    public DateTime RecordedAt { get; set; }
    public decimal? WeightKg { get; set; }

    /// <summary>Girilen/olculen yag orani.</summary>
    public decimal? BodyFatPercentage { get; set; }

    /// <summary>Cevre olcumlerinden hesaplanan tahmini yag orani; ekranda "tahmini" olarak etiketlenir.</summary>
    public decimal? EstimatedBodyFatPercentage { get; set; }

    /// <summary>Tahmin yontemi ("UsNavy") veya null.</summary>
    public string? BodyFatEstimationMethod { get; set; }

    public decimal? NeckCm { get; set; }
    public decimal? ChestCm { get; set; }
    public decimal? WaistCm { get; set; }
    public decimal? HipCm { get; set; }
    public decimal? ArmCm { get; set; }
    public decimal? ThighCm { get; set; }
    public decimal? Bmi { get; set; }
}

/// <summary>Guncel yag orani degerlendirmesi.</summary>
public class BodyFatAssessmentDto
{
    /// <summary>Degerlendirmede kullanilan yag orani; veri yoksa null.</summary>
    public decimal? Percentage { get; set; }

    /// <summary>"measured" | "estimated"; okuma yoksa null.</summary>
    public string? Source { get; set; }

    /// <summary>Tahmin yontemi ("UsNavy"); olculen degerde null.</summary>
    public string? Method { get; set; }

    /// <summary>Yontemin yaklasik hata payi (yuzde puan).</summary>
    public decimal? MarginOfErrorPercent { get; set; }

    public DateTime? RecordedAt { get; set; }

    /// <summary>"low" | "healthy" | "high" | "unknown".</summary>
    public string Category { get; set; } = "unknown";

    public decimal? LowThreshold { get; set; }
    public decimal? HighThreshold { get; set; }

    /// <summary>Degerlendirme icin eksik bilgiler ("bodyFat", "age", "gender", "height", "neck", "waist", "hip").</summary>
    public List<string> Missing { get; set; } = new();

    public static BodyFatAssessmentDto From(BodyFatAssessment assessment) => new()
    {
        Percentage = assessment.Reading?.Percentage,
        Source = assessment.Reading?.Source switch
        {
            BodyFatSource.Measured => "measured",
            BodyFatSource.Estimated => "estimated",
            _ => null,
        },
        Method = assessment.Reading?.Method,
        MarginOfErrorPercent = assessment.Reading?.MarginOfErrorPercent,
        RecordedAt = assessment.Reading?.RecordedAt,
        Category = assessment.Category.ToString().ToLowerInvariant(),
        LowThreshold = assessment.LowThreshold,
        HighThreshold = assessment.HighThreshold,
        Missing = assessment.Missing.ToList(),
    };
}

/// <summary>GET /api/body-measurements yaniti: olcum gecmisi (yeniden eskiye) ve guncel degerlendirme.</summary>
public class BodyMeasurementHistoryResponse
{
    /// <summary>Profildeki boy (cm); yag orani tahmini bu boyla yapilir.</summary>
    public decimal? HeightCm { get; set; }

    /// <summary>Profildeki cinsiyet ("male" | "female" | "other" | null). Kadinlarda tahmin icin kalca da gerekir.</summary>
    public string? Gender { get; set; }

    public List<BodyMeasurementDto> Measurements { get; set; } = new();

    public BodyFatAssessmentDto BodyFat { get; set; } = new();

    /// <summary>
    /// Kullanicinin hedefi Yag Kaybi ve yag orani dusuk aralikta gorunuyorsa true; uygulama bilgilendirme
    /// gosterir ve hedefi yeniden degerlendirmeyi onerir.
    /// </summary>
    public bool ShowLowBodyFatWarning { get; set; }

    /// <summary><see cref="ShowLowBodyFatWarning"/> true ise gosterilecek metin.</summary>
    public string? LowBodyFatWarningMessage { get; set; }
}
