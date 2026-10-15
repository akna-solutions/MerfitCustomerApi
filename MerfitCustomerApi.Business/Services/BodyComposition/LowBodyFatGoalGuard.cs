using MerfitCustomerApi.Business.Common;
using MerfitCustomerApi.Domain.Entities.Enums;
using MerfitCustomerApi.Domain.Exceptions;

namespace MerfitCustomerApi.Business.Services.BodyComposition;

/// <summary>
/// Zorunlu is kurali: Yag Kaybi hedefi secilirken guncel olcumlere gore yag orani yas/cinsiyet icin
/// yapilandirilmis dusuk araligin altindaysa kullanici uyarilir ve hedef ancak acik onayla kaydedilir.
/// Kayit (onboarding) ve profilden hedef degistirme ayni kurali kullanir; istemcinin gonderdigi uyari
/// durumuna guvenilmez, degerlendirme sunucuda yeniden yapilir. Kural tani koymaz ve hedefi sessizce
/// degistirmez: kullanici ya hedefini yeniden degerlendirir ya da uyariyi onaylayip devam eder.
/// </summary>
public static class LowBodyFatGoalGuard
{
    /// <summary>Uyari metni (mobil uygulama ayni metni gosterir).</summary>
    public const string WarningMessage =
        "Ölçümlerine göre vücut yağ oranın düşük aralıkta görünüyor. Daha fazla kilo vermek kas kaybı ve sağlık sorunları riskini artırabilir. Hedefini yeniden değerlendirmeyi düşünmeni öneriyoruz. Ölçüm tahmini bir değer olabilir.";

    /// <summary>Secilen hedef icin uyari gerekiyor mu.</summary>
    public static bool RequiresWarning(FitnessGoal goal, BodyFatAssessment assessment) =>
        FitnessGoals.Normalize(goal) == FitnessGoal.LoseWeight && assessment.IsLow;

    /// <summary>
    /// Uyari gerekiyorsa ve kullanici acikca onaylamadiysa <see cref="LowBodyFatConfirmationRequiredException"/>
    /// firlatir. Onay gerekip verildiyse true doner (cagiran onay zamanini kaydeder); uyari gerekmiyorsa false.
    /// </summary>
    public static bool EnsureConfirmed(FitnessGoal goal, BodyFatAssessment assessment, bool? confirmed)
    {
        if (!RequiresWarning(goal, assessment))
        {
            return false;
        }

        if (confirmed != true)
        {
            throw new LowBodyFatConfirmationRequiredException(WarningMessage);
        }

        return true;
    }
}
