using System.ComponentModel.DataAnnotations;
using MerfitCustomerApi.Business.Dtos.Customer.Nutrition;

namespace MerfitCustomerApi.Business.Dtos.Customer.FoodAnalysis;

/// <summary>
/// POST /api/food-analysis/analyze yaniti. Sabit sozlesme: alan adlari saglayicidan bagimsizdir. Tum degerler
/// tahminidir (<see cref="IsEstimate"/>); fotograftan gramaj ve yag kesin olculemez. Sonuc otomatik kaydedilmez;
/// kullanici porsiyonlari onaylayinca POST /api/food-analysis/{AnalysisId}/confirm cagrilir.
/// </summary>
public class FoodAnalysisResultDto
{
    /// <summary>Onay icin kullanilan analiz kimligi (yalnizca analizi yapan kullanici kullanabilir).</summary>
    public long AnalysisId { get; set; }

    /// <summary>Ogunun kisa adi (orn. "Mercimek çorbası ve pilav").</summary>
    public string FoodName { get; set; } = string.Empty;

    public List<FoodAnalysisItemDto> Items { get; set; } = new();

    /// <summary>Tahmini gramajlara gore toplamlar.</summary>
    public FoodAnalysisTotalsDto Totals { get; set; } = new();

    /// <summary>Genel tanima guveni (0-1).</summary>
    public decimal Confidence { get; set; }

    /// <summary>Degerler tahmini mi? Fotograf analizinde her zaman true.</summary>
    public bool IsEstimate { get; set; } = true;

    /// <summary>En az bir yemegin porsiyonu kullanici tarafindan dogrulanmali mi?</summary>
    public bool NeedsPortionConfirmation { get; set; }

    /// <summary>Kullaniciya gosterilecek uyarilar (tanimlanamayan yiyecekler, belirsiz porsiyon vb.).</summary>
    public List<string> Warnings { get; set; } = new();

    /// <summary>Fotografta tanimlanamayan yiyeceklerin kisa tarifleri (orn. "kase içinde koyu renkli sos").</summary>
    public List<string> UnidentifiedItems { get; set; } = new();

    /// <summary>Son 24 saatte kalan analiz hakki.</summary>
    public int RemainingAnalyses { get; set; }

    /// <summary>Bu analizin ogune kaydedilebilecegi son an (UTC).</summary>
    public DateTime ConfirmBefore { get; set; }
}

public class FoodAnalysisItemDto
{
    /// <summary>Analiz icindeki sira numarasi (1'den baslar); onayda yemegi tanimlar.</summary>
    public int ItemId { get; set; }

    public string FoodName { get; set; } = string.Empty;

    /// <summary>Tahmini yenen miktar (g).</summary>
    public decimal EstimatedGrams { get; set; }

    /// <summary>Gramaj tahmin araligi (g); fotograftan kesin olculemez.</summary>
    public decimal MinGrams { get; set; }

    public decimal MaxGrams { get; set; }

    /// <summary>EstimatedGrams icin degerler.</summary>
    public decimal Calories { get; set; }

    public decimal Protein { get; set; }

    public decimal Carbs { get; set; }

    public decimal Fat { get; set; }

    /// <summary>100 g icin degerler; mobil porsiyon degisince toplami bunlarla yeniden hesaplar.</summary>
    public FoodAnalysisNutrientsDto Per100g { get; set; } = new();

    /// <summary>Yemegin dogru tanindigina dair guven (0-1).</summary>
    public decimal Confidence { get; set; }

    /// <summary>Porsiyon tahmininin belirsizligi: "low" | "medium" | "high".</summary>
    public string PortionUncertainty { get; set; } = "medium";

    /// <summary>Kullanicidan porsiyonu dogrulamasi istenmeli mi (dusuk guven ya da yuksek belirsizlik)?</summary>
    public bool NeedsPortionConfirmation { get; set; }

    /// <summary>Tahmini deger mi? Katalog eslesmesinde de gramaj tahmini oldugu icin true kalir.</summary>
    public bool IsEstimate { get; set; } = true;

    /// <summary>Besin degerlerinin kaynagi: "catalog" (dogrulanmis besin katalogu) | "ai_estimate" (model tahmini).</summary>
    public string NutritionSource { get; set; } = FoodAnalysisNutritionSources.AiEstimate;

    /// <summary>Katalog eslesmesinde eslesen besinin adi.</summary>
    public string? MatchedFoodName { get; set; }

    /// <summary>Katalog eslesmesinde eslesen besinin kimligi (Food.Id).</summary>
    public long? MatchedFoodId { get; set; }

    /// <summary>Gorunmeyen yag/sos gibi belirsizlikler icin kisa not.</summary>
    public string? Note { get; set; }
}

public class FoodAnalysisNutrientsDto
{
    public decimal Calories { get; set; }
    public decimal Protein { get; set; }
    public decimal Carbs { get; set; }
    public decimal Fat { get; set; }
}

public class FoodAnalysisTotalsDto
{
    public decimal EstimatedGrams { get; set; }
    public decimal Calories { get; set; }
    public decimal Protein { get; set; }
    public decimal Carbs { get; set; }
    public decimal Fat { get; set; }
}

public static class FoodAnalysisNutritionSources
{
    public const string Catalog = "catalog";
    public const string AiEstimate = "ai_estimate";
}

/// <summary>
/// POST /api/food-analysis/{analysisId}/confirm govdesi. Besin degerleri istemciden alinmaz; sunucudaki analiz
/// kaydindan okunur. Istemci yalnizca hangi yemeklerin hangi gramajla kaydedilecegini gonderir.
/// </summary>
public class ConfirmFoodAnalysisRequest
{
    /// <summary>"yyyy-MM-dd"; verilmezse bugun kabul edilir.</summary>
    public DateOnly? Date { get; set; }

    /// <summary>"Breakfast" | "Lunch" | "Dinner" | "Snack".</summary>
    [Required(ErrorMessage = "MealType alani zorunludur.")]
    public string MealType { get; set; } = string.Empty;

    [Required(ErrorMessage = "En az bir yemek secilmelidir.")]
    [MinLength(1, ErrorMessage = "En az bir yemek secilmelidir.")]
    [MaxLength(20, ErrorMessage = "En fazla 20 yemek kaydedilebilir.")]
    public List<ConfirmFoodAnalysisItem> Items { get; set; } = new();
}

public class ConfirmFoodAnalysisItem
{
    [Range(1, 100, ErrorMessage = "Gecersiz yemek.")]
    public int ItemId { get; set; }

    /// <summary>Kullanicinin onayladigi gramaj (g).</summary>
    [Range(1, 3000, ErrorMessage = "Gramaj 1 ile 3000 arasinda olmalidir.")]
    public decimal Grams { get; set; }
}

/// <summary>Onay yaniti: gunluge eklenen kayitlar (mevcut CustomerMealEntryDto bicimi).</summary>
public class ConfirmFoodAnalysisResponse
{
    public long AnalysisId { get; set; }

    /// <summary>true: bu analiz daha once kaydedilmisti; yeni kayit olusturulmadi, ilk kaydin sonucu donuldu.</summary>
    public bool AlreadyLogged { get; set; }

    public List<CustomerMealEntryDto> Entries { get; set; } = new();
}
