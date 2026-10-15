using MerfitCustomerApi.Business.Services.BodyComposition;
using MerfitCustomerApi.Business.Services.Nutrition;
using MerfitCustomerApi.Domain.Entities;

namespace MerfitCustomerApi.Business.Interfaces.Services;

/// <summary>
/// Kullanicinin profil bilgilerinden (yas, cinsiyet, boy, kilo, hedef kilo, aktivite seviyesi,
/// antrenman sikligi, fitness hedefi) gunluk kalori/protein/karbonhidrat/yag/su hedeflerini hesaplayan,
/// saf ve deterministik (yapay zeka / harici API kullanmayan) servis sozlesmesi. Sadece matematiksel
/// hesaplamadan sorumludur; NutritionGoal kaydini olusturmak veya kaydetmek cagiran taraflarin gorevidir.
/// </summary>
public interface INutritionCalculator
{
    /// <summary>
    /// Esnek hesaplama: profildeki eksik (null) veya aralik disi alanlar icin konfigure edilmis
    /// varsayilanlar kullanilir; istisna firlatmaz. Kayit aninda ilk hedefi olusturmak gibi "bir deger
    /// gostermek zorunda olunan" yerlerde kullanilir. Girdiler tamsa sonuc <see cref="Evaluate"/> ile aynidir.
    /// </summary>
    /// <param name="profile">Hesaplama icin kullanilacak kullanici profili.</param>
    /// <param name="bodyFat">
    /// Guncel vucut yag orani degerlendirmesi (opsiyonel). Kalori fazlasi/acigi kararinda kullanilir; null veya
    /// Unknown ise yag oranina bagli kurallar uygulanmaz (eksik veriyle dusuk/yuksek sonucu uydurulmaz).
    /// </param>
    NutritionCalculationResult Calculate(UserProfile profile, BodyFatAssessment? bodyFat = null);

    /// <summary>
    /// Kati hesaplama: kisisel plan uretimi icin. Kilo, boy, yas, hedef ve aktivite (seviye veya haftalik
    /// antrenman gunu) zorunludur ve makul araliklarda olmalidir; aksi halde sonuc yerine eksik/gecersiz
    /// alanlarin listesi doner. Hicbir varsayilan deger kullanilmaz.
    /// </summary>
    /// <param name="profile">Hesaplama icin kullanilacak kullanici profili.</param>
    /// <param name="bodyFat">Guncel vucut yag orani degerlendirmesi (opsiyonel), bkz. <see cref="Calculate"/>.</param>
    NutritionTargetEvaluation Evaluate(UserProfile profile, BodyFatAssessment? bodyFat = null);
}
