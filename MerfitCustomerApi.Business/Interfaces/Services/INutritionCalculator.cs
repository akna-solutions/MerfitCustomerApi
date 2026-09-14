using MerfitCustomerApi.Business.Services.Nutrition;
using MerfitCustomerApi.Domain.Entities;

namespace MerfitCustomerApi.Business.Interfaces.Services;

/// <summary>
/// Kullanicinin profil bilgilerinden (yas, cinsiyet, boy, kilo, aktivite seviyesi, fitness hedefi)
/// gunluk kalori/protein/karbonhidrat/yag/su hedeflerini hesaplayan, saf ve deterministik
/// (yapay zeka / harici API kullanmayan) servis sozlesmesi. Sadece matematiksel hesaplamadan
/// sorumludur; NutritionGoal kaydini olusturmak veya kaydetmek cagiran taraflarin gorevidir.
/// </summary>
public interface INutritionCalculator
{
    /// <summary>
    /// Verilen profil icin Mifflin-St Jeor BMR formulu + aktivite carpani + hedefe gore kalori
    /// duzeltmesi kullanarak gunluk beslenme hedeflerini hesaplar. Profildeki eksik (null)
    /// alanlar icin guvenli varsayilan degerler kullanilir; istisna firlatmaz.
    /// </summary>
    /// <param name="profile">Hesaplama icin kullanilacak kullanici profili.</param>
    NutritionCalculationResult Calculate(UserProfile profile);
}
