using System.Globalization;
using MerfitCustomerApi.Business.Dtos.Customer.Nutrition;
using MerfitCustomerApi.Business.Dtos.Customer.NutritionAssistant;

namespace MerfitCustomerApi.Business.Services.NutritionAssistant;

/// <summary>
/// Gunluk hedeflerle kaydedilen tuketimi karsilastirip beslenme ekranindaki degerlendirmeyi ve hizli
/// sorulari uretir. Saf (yan etkisiz) ve deterministiktir; beslenme karti, sohbet acilisi ve modele
/// giden baglam ayni esikleri kullanir.
///
/// Kayit olmayan ogun "tuketilmedi" sayilmaz: makro yorumlari mutlak eksige degil, kaydedilen kalori
/// oranina gore yapilir (protein, o ana kadar yenen kaloriye oranla geride mi?). Kalori hedefinin
/// %25'inden azi kaydedildiyse makro yorumu yapilmaz.
/// </summary>
public static class NutritionInsightEvaluator
{
    /// <summary>Kalori ve protein bu aralikta, karbonhidrat/yag <see cref="MacroOnTargetLower"/>..<see cref="MacroHigh"/> araligindaysa hedefler tamamlanmis sayilir.</summary>
    public const double CompletedLower = 0.90;
    public const double CompletedUpper = 1.10;
    public const double MacroOnTargetLower = 0.80;

    /// <summary>Kalori bu oranin ustundeyse hedef asilmis sayilir.</summary>
    public const double CaloriesExceeded = 1.10;

    /// <summary>Yag veya karbonhidrat bu oranin ustundeyse "yuksek" sayilir.</summary>
    public const double MacroHigh = 1.15;

    /// <summary>Makro yorumu icin kaydedilmesi gereken en az kalori orani.</summary>
    public const double MinCalorieRatioForMacros = 0.25;

    /// <summary>Protein orani kalori oraninin bu kadar gerisindeyse ve eksik en az <see cref="MinProteinGapGrams"/> g ise "geride" sayilir.</summary>
    public const double ProteinLagMargin = 0.15;
    public const int MinProteinGapGrams = 10;

    /// <summary>Kalori orani bu degerin ustundeyse "buyuk bolumu tamamlandi" sayilir.</summary>
    public const double CaloriesMostlyDone = 0.75;

    /// <summary>Karbonhidrat orani kalori oranina bu kadar yakinsa "uyumlu" sayilir.</summary>
    public const double CarbsOnTrackMargin = 0.15;

    public static class Statuses
    {
        public const string NoData = "noData";
        public const string NoTargets = "noTargets";
        public const string GoalsCompleted = "goalsCompleted";
        public const string CaloriesExceeded = "caloriesExceeded";
        public const string ProteinBehind = "proteinBehind";
        public const string FatsHigh = "fatsHigh";
        public const string CarbsHigh = "carbsHigh";
        public const string CaloriesMostlyDone = "caloriesMostlyDone";
        public const string CarbsOnTrack = "carbsOnTrack";
        public const string InProgress = "inProgress";
    }

    public static class Questions
    {
        public const string CompleteProtein = "Bugün protein hedefimi nasıl tamamlayabilirim?";
        public const string WhatFitsGoal = "Kalan kalorime uygun ne yiyebilirim?";
        public const string Dinner = "Akşam yemeğinde ne tercih etmeliyim?";
        public const string CalorieStatus = "Bugünkü beslenmemi değerlendir.";
        public const string PostWorkout = "Antrenman sonrası ne yemeliyim?";
        public const string AfterExceeded = "Kalori hedefimi aştım, günün geri kalanında neye dikkat etmeliyim?";
        public const string LighterChoices = "Daha hafif hangi seçenekleri tercih edebilirim?";
        public const string RemainingMeal = "Kalan hedeflerime uygun bir öğün önerir misin?";
        public const string Snack = "Ara öğün için ne önerirsin?";
        public const string Tomorrow = "Yarın için nasıl bir plan yapmalıyım?";
    }

    private static readonly CultureInfo Tr = CultureInfo.GetCultureInfo("tr-TR");

    public static int ConsumedCalories(CustomerNutritionResponse day) => day.Meals.Sum(m => m.Calories);

    public static NutritionInsightDto Evaluate(CustomerNutritionResponse day)
    {
        if (day.Meals.Count == 0)
        {
            return Insight(Statuses.NoData,
                "Bu gün için henüz öğün kaydı yok. Öğünlerini ekledikçe hedeflerine göre değerlendirme yapacağım.",
                Questions.WhatFitsGoal);
        }

        if (day.DailyCalories <= 0)
        {
            return Insight(Statuses.NoTargets,
                "Günlük hedeflerin henüz hesaplanmadı. Profil bilgilerini tamamladığında değerlendirme yapabilirim.",
                Questions.WhatFitsGoal);
        }

        var kcal = ConsumedCalories(day);
        var calRatio = Ratio(kcal, day.DailyCalories);
        var protein = day.Macros.Protein;
        var carbs = day.Macros.Carbs;
        var fats = day.Macros.Fats;
        var proteinRatio = Ratio(protein.Consumed, protein.Target);
        var carbsRatio = Ratio(carbs.Consumed, carbs.Target);
        var fatsRatio = Ratio(fats.Consumed, fats.Target);

        var goalsCompleted = calRatio is >= CompletedLower and <= CompletedUpper
            && (protein.Target <= 0 || proteinRatio >= CompletedLower)
            && (carbs.Target <= 0 || carbsRatio is >= MacroOnTargetLower and <= MacroHigh)
            && (fats.Target <= 0 || fatsRatio is >= MacroOnTargetLower and <= MacroHigh);
        if (goalsCompleted)
        {
            return Insight(Statuses.GoalsCompleted,
                $"Bugünkü beslenme hedeflerini tamamladın: {N(kcal)} / {N(day.DailyCalories)} kcal, protein {protein.Consumed} / {protein.Target} g.",
                Questions.Tomorrow);
        }

        if (calRatio > CaloriesExceeded)
        {
            return Insight(Statuses.CaloriesExceeded,
                $"Günlük kalori hedefini yaklaşık {N(kcal - day.DailyCalories)} kcal aştın ({N(kcal)} / {N(day.DailyCalories)} kcal).",
                Questions.AfterExceeded);
        }

        var enoughForMacros = calRatio >= MinCalorieRatioForMacros;

        var proteinGap = protein.Target - protein.Consumed;
        if (enoughForMacros && protein.Target > 0
            && proteinRatio < calRatio - ProteinLagMargin
            && proteinGap >= MinProteinGapGrams)
        {
            return Insight(Statuses.ProteinBehind,
                $"Protein hedefinin gerisindesin: {protein.Consumed} / {protein.Target} g. Yaklaşık {proteinGap} g daha proteine ihtiyacın var.",
                Questions.CompleteProtein);
        }

        if (enoughForMacros && fats.Target > 0 && fatsRatio > MacroHigh)
        {
            return Insight(Statuses.FatsHigh,
                $"Yağ alımın günlük hedefinin üzerinde: {fats.Consumed} / {fats.Target} g.",
                Questions.LighterChoices);
        }

        if (enoughForMacros && carbs.Target > 0 && carbsRatio > MacroHigh)
        {
            return Insight(Statuses.CarbsHigh,
                $"Karbonhidrat alımın günlük hedefinin üzerinde: {carbs.Consumed} / {carbs.Target} g.",
                Questions.LighterChoices);
        }

        if (calRatio >= CaloriesMostlyDone)
        {
            return Insight(Statuses.CaloriesMostlyDone,
                $"Günlük kalori hedefinin büyük bölümünü tamamladın (%{Math.Round(calRatio * 100)}). Yaklaşık {N(day.DailyCalories - kcal)} kcal kaldı.",
                Questions.RemainingMeal);
        }

        if (enoughForMacros && carbs.Target > 0 && Math.Abs(carbsRatio - calRatio) <= CarbsOnTrackMargin)
        {
            return Insight(Statuses.CarbsOnTrack,
                $"Karbonhidrat hedefinle uyumlu ilerliyorsun: {carbs.Consumed} / {carbs.Target} g.",
                Questions.RemainingMeal);
        }

        return Insight(Statuses.InProgress,
            $"Şu ana kadar {N(kcal)} / {N(day.DailyCalories)} kcal kaydettin. Öğünlerini ekledikçe değerlendirmeyi güncelleyeceğim.",
            Questions.CalorieStatus);
    }

    /// <summary>
    /// Kullanicinin o anki durumuna gore en fazla <paramref name="max"/> hizli soru: once degerlendirmeye bagli
    /// soru, sonra gunun saatine, kaydedilmemis ogunlere ve antrenman durumuna gore sorular.
    /// </summary>
    /// <param name="localHour">Gun bugunse yerel saat; gecmis bir gunse null.</param>
    public static List<string> SuggestQuestions(
        CustomerNutritionResponse day,
        NutritionInsightDto insight,
        int? localHour,
        bool workedOutToday,
        IEnumerable<string>? exclude = null,
        int max = 4)
    {
        var candidates = new List<string>();
        if (insight.SuggestedQuestion is { } first) candidates.Add(first);

        var isToday = localHour.HasValue;
        var hasDinner = day.Meals.Any(m => m.Type == "Akşam Yemeği");

        if (isToday && workedOutToday) candidates.Add(Questions.PostWorkout);
        if (isToday && localHour >= 15 && !hasDinner) candidates.Add(Questions.Dinner);
        if (insight.Status != Statuses.ProteinBehind && insight.Status != Statuses.GoalsCompleted
            && day.Macros.Protein.Target > 0 && day.Macros.Protein.Consumed < day.Macros.Protein.Target)
        {
            candidates.Add(Questions.CompleteProtein);
        }
        if (day.Meals.Count > 0) candidates.Add(Questions.CalorieStatus);
        candidates.Add(Questions.WhatFitsGoal);
        if (isToday && localHour < 15) candidates.Add(Questions.Snack);
        if (!isToday) candidates.Add(Questions.Tomorrow);
        if (isToday && !workedOutToday) candidates.Add(Questions.PostWorkout);

        var excluded = new HashSet<string>(exclude ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase);
        return candidates
            .Where(q => !excluded.Contains(q.Trim()))
            .Distinct()
            .Take(max)
            .ToList();
    }

    private static NutritionInsightDto Insight(string status, string message, string? question) =>
        new() { Status = status, Message = message, SuggestedQuestion = question };

    private static double Ratio(int consumed, int target) => target <= 0 ? 0 : (double)consumed / target;

    private static string N(int value) => value.ToString("N0", Tr);
}
