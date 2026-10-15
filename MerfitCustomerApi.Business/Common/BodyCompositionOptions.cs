using MerfitCustomerApi.Domain.Entities.Enums;

namespace MerfitCustomerApi.Business.Common;

/// <summary>
/// appsettings.json icindeki "BodyComposition" bolumu: vucut yag orani degerlendirmesinde kullanilan
/// yas/cinsiyete gore araliklar, olcumlerin gecerlilik suresi ve yontem hata paylari.
///
/// Varsayilan araliklar: Gallagher D. ve ark., "Healthy percentage body fat ranges: an approach for
/// developing guidelines based on body mass index", Am J Clin Nutr 2000;72(3):694-701. Bu calisma BMI
/// 18.5 ve 25'e karsilik gelen yag oranlarini yas ve cinsiyete gore raporlar. Alt sinirin altindaki deger
/// "dusuk", ust sinirin ustundeki deger "yuksek" kabul edilir. Calisma 20-79 yas arasini kapsar: 18-19
/// yas 20-39 bandina, 80 yas ve ustu 60-79 bandina dahil edilir; 18 yas altinda degerlendirme yapilmaz.
/// Bu araliklar tani koymak icin degil, hedefi yeniden degerlendirme onerisi icin kullanilir.
/// </summary>
public class BodyCompositionOptions
{
    public const string SectionName = "BodyComposition";

    /// <summary>Degerlendirme yapilabilecek en dusuk yas. Altinda dusuk yag orani sonucu uretilmez.</summary>
    public int MinimumAssessmentAge { get; set; } = 18;

    /// <summary>Girilen/olculen yag oraninin "yakin tarihli" sayildigi en uzun sure (gun).</summary>
    public int MeasuredBodyFatMaxAgeDays { get; set; } = 90;

    /// <summary>Cevre olcumlerinin tahmin icin kullanilabildigi en uzun sure (gun).</summary>
    public int CircumferenceMaxAgeDays { get; set; } = 90;

    /// <summary>
    /// US Navy cevre yonteminin yaklasik hata payi (yuzde puan). Hodgdon ve Beckett (1984) referans
    /// yontemlere gore tahmin hatasini yaklasik 3-4 puan olarak bildirir. Kullaniciya tahminle birlikte
    /// gosterilir.
    /// </summary>
    public decimal EstimatedMarginOfErrorPercent { get; set; } = 3.5m;

    /// <summary>
    /// Kullanicinin girdigi (tarti/BIA gibi cihazdan okunan) degerin varsayilan hata payi (yuzde puan).
    /// Ev tipi biyoelektrik empedans cihazlarinin sapmasi genellikle bu mertebededir.
    /// </summary>
    public decimal MeasuredMarginOfErrorPercent { get; set; } = 4m;

    /// <summary>
    /// Yapilandirmadan gelen yas/cinsiyet araliklari. Bos birakilirsa <see cref="DefaultRanges"/> kullanilir.
    /// (Liste baglamasi varsayilan ogelerin uzerine ekleme yaptigi icin varsayilanlar ayri tutulur;
    /// yapilandirmada verilen liste varsayilanlarin tamamen yerine gecer.)
    /// </summary>
    public List<BodyFatRange> Ranges { get; set; } = new();

    /// <summary>Gallagher ve ark. (2000) tablosundan turetilen varsayilan araliklar.</summary>
    public static IReadOnlyList<BodyFatRange> DefaultRanges { get; } = new List<BodyFatRange>
    {
        new() { Gender = Gender.Male, MinAge = 18, MaxAge = 39, LowBelow = 8m, HighAbove = 20m },
        new() { Gender = Gender.Male, MinAge = 40, MaxAge = 59, LowBelow = 11m, HighAbove = 22m },
        new() { Gender = Gender.Male, MinAge = 60, MaxAge = 120, LowBelow = 13m, HighAbove = 25m },
        new() { Gender = Gender.Female, MinAge = 18, MaxAge = 39, LowBelow = 21m, HighAbove = 33m },
        new() { Gender = Gender.Female, MinAge = 40, MaxAge = 59, LowBelow = 23m, HighAbove = 34m },
        new() { Gender = Gender.Female, MinAge = 60, MaxAge = 120, LowBelow = 24m, HighAbove = 36m },
    };

    /// <summary>Degerlendirmede kullanilan araliklar.</summary>
    public IReadOnlyList<BodyFatRange> EffectiveRanges => Ranges is { Count: > 0 } ? Ranges : DefaultRanges;
}

/// <summary>Bir yas/cinsiyet bandi icin yag orani araligi (yuzde).</summary>
public class BodyFatRange
{
    public Gender Gender { get; set; }

    /// <summary>Bandin en dusuk yasi (dahil).</summary>
    public int MinAge { get; set; }

    /// <summary>Bandin en yuksek yasi (dahil).</summary>
    public int MaxAge { get; set; }

    /// <summary>Bu degerin altindaki yag orani dusuk aralikta kabul edilir.</summary>
    public decimal LowBelow { get; set; }

    /// <summary>Bu degerin ustundeki yag orani yuksek aralikta kabul edilir.</summary>
    public decimal HighAbove { get; set; }
}
