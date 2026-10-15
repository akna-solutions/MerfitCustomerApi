using System.Globalization;
using System.Text;
using MerfitCustomerApi.Business.Common;
using MerfitCustomerApi.Business.Dtos.Customer.Nutrition;
using MerfitCustomerApi.Business.Dtos.Customer.NutritionAssistant;
using MerfitCustomerApi.Business.Services.Nutrition;
using MerfitCustomerApi.Domain.Entities;
using MerfitCustomerApi.Domain.Entities.Enums;

namespace MerfitCustomerApi.Business.Services.NutritionAssistant;

/// <summary>
/// Bir beslenme gunu icin asistanin ihtiyac duydugu en kucuk baglam. Her istekte veritabanindan yeniden
/// olusturulur ve saklanmaz. Yalnizca gercekten bilinen alanlar doldurulur; bilinmeyenler modele
/// "bilinmiyor" olarak gider, tahmin edilmez.
/// </summary>
public sealed class NutritionAssistantContext
{
    public DateOnly Date { get; init; }

    /// <summary>Gun kullanicinin yerel "bugun"u ise o anki yerel saat; degilse null.</summary>
    public TimeOnly? LocalTime { get; init; }

    public bool IsToday => LocalTime.HasValue;

    public string? FirstName { get; init; }

    public CustomerNutritionResponse Day { get; init; } = new();

    public NutritionInsightDto Insight { get; init; } = new();

    public FitnessGoal? Goal { get; init; }
    public Gender? Gender { get; init; }
    public int? Age { get; init; }
    public decimal? HeightCm { get; init; }
    public decimal? WeightKg { get; init; }
    public decimal? TargetWeightKg { get; init; }
    public ActivityLevel? ActivityLevel { get; init; }
    public int? TrainingDaysPerWeek { get; init; }
    public DietaryPreference? DietaryPreference { get; init; }
    public FoodAllergen? Allergies { get; init; }
    public IReadOnlyList<string> ExcludedFoods { get; init; } = Array.Empty<string>();

    public int CompletedWorkouts { get; init; }
    public bool WorkoutInProgress { get; init; }
    public int WorkoutMinutes { get; init; }
    public int WorkoutCaloriesBurned { get; init; }

    public bool WorkedOut => CompletedWorkouts > 0 || WorkoutInProgress;

    private static readonly CultureInfo Tr = CultureInfo.GetCultureInfo("tr-TR");

    private static readonly string[] MealOrder = { "Kahvaltı", "Öğle Yemeği", "Akşam Yemeği", "Atıştırmalık" };

    /// <summary>Modelin sistem baglamina eklenen, insan tarafindan okunabilir Turkce ozet.</summary>
    public string Render()
    {
        var sb = new StringBuilder();
        var day = Day;
        var kcal = NutritionInsightEvaluator.ConsumedCalories(day);

        sb.Append("Tarih: ").Append(Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        sb.AppendLine(IsToday
            ? $" (bugün, kullanıcının yerel saati {LocalTime!.Value:HH\\:mm})"
            : " (bugün değil; kullanıcı bu günün kayıtlarına bakıyor)");

        if (!string.IsNullOrWhiteSpace(FirstName)) sb.Append("Kullanıcının adı: ").AppendLine(FirstName);

        if (day.DailyCalories > 0)
        {
            sb.AppendLine($"Günlük hedefler: {N(day.DailyCalories)} kcal, protein {day.Macros.Protein.Target} g, karbonhidrat {day.Macros.Carbs.Target} g, yağ {day.Macros.Fats.Target} g, su {day.Water.TargetL.ToString("0.##", Tr)} L");
            sb.AppendLine($"Kaydedilen tüketim: {N(kcal)} kcal, protein {day.Macros.Protein.Consumed} g, karbonhidrat {day.Macros.Carbs.Consumed} g, yağ {day.Macros.Fats.Consumed} g, su {day.Water.ConsumedL.ToString("0.##", Tr)} L");
            sb.AppendLine($"Kalan (hedef - kaydedilen, negatifse aşım): {N(day.DailyCalories - kcal)} kcal, protein {day.Macros.Protein.Target - day.Macros.Protein.Consumed} g, karbonhidrat {day.Macros.Carbs.Target - day.Macros.Carbs.Consumed} g, yağ {day.Macros.Fats.Target - day.Macros.Fats.Consumed} g");
        }
        else
        {
            sb.AppendLine("Günlük hedefler: hesaplanmadı (bilinmiyor).");
        }

        if (day.Meals.Count == 0)
        {
            sb.AppendLine("Kaydedilen öğünler: bu gün için hiç öğün kaydı yok.");
        }
        else
        {
            sb.AppendLine("Kaydedilen öğünler:");
            foreach (var group in day.Meals.GroupBy(m => m.Type).OrderBy(g => Array.IndexOf(MealOrder, g.Key) is var i && i < 0 ? 99 : i))
            {
                sb.Append("- ").Append(group.Key).Append(": ")
                  .AppendLine(string.Join(", ", group.Select(m => $"{m.Name} ({m.Calories} kcal)")));
            }
        }

        var missing = MealOrder.Where(t => day.Meals.All(m => m.Type != t)).ToList();
        if (missing.Count > 0)
        {
            sb.Append("Kaydı olmayan öğünler: ").Append(string.Join(", ", missing))
              .AppendLine(" (kayıt olmaması tüketilmediği anlamına gelmez; varsayımda bulunma).");
        }

        sb.Append("Uygulamanın değerlendirmesi: ").AppendLine(Insight.Message);

        sb.Append("Profil: hedef ").Append(GoalLabel(Goal))
          .Append("; cinsiyet ").Append(GenderLabel(Gender))
          .Append("; yaş ").Append(Age?.ToString(CultureInfo.InvariantCulture) ?? "bilinmiyor")
          .Append("; boy ").Append(HeightCm.HasValue ? $"{HeightCm.Value:0.#} cm" : "bilinmiyor")
          .Append("; kilo ").Append(WeightKg.HasValue ? $"{WeightKg.Value:0.#} kg" : "bilinmiyor")
          .Append("; hedef kilo ").Append(TargetWeightKg.HasValue ? $"{TargetWeightKg.Value:0.#} kg" : "bilinmiyor")
          .Append("; aktivite ").Append(ActivityLabel(ActivityLevel))
          .Append("; haftalık antrenman günü ").AppendLine(TrainingDaysPerWeek?.ToString(CultureInfo.InvariantCulture) ?? "bilinmiyor");

        sb.Append("Beslenme tercihi: ").AppendLine(DietLabel(DietaryPreference));
        var allergens = FoodPreferenceFormat.AllergensToNames(Allergies);
        sb.Append("Alerjiler: ").AppendLine(Allergies is null
            ? "belirtilmemiş (alerji olmadığı kesin değil)"
            : allergens.Count == 0 ? "bilinen alerji yok" : string.Join(", ", allergens.Select(AllergenLabel)));
        if (ExcludedFoods.Count > 0)
        {
            sb.Append("Kullanıcının istemediği besinler: ").AppendLine(string.Join(", ", ExcludedFoods));
        }

        sb.Append("Antrenman (bu gün): ");
        if (CompletedWorkouts > 0)
        {
            sb.Append($"{CompletedWorkouts} tamamlanan antrenman");
            if (WorkoutMinutes > 0) sb.Append($", toplam {WorkoutMinutes} dk");
            if (WorkoutCaloriesBurned > 0) sb.Append($", yaklaşık {WorkoutCaloriesBurned} kcal yakıldı");
            sb.AppendLine();
        }
        else
        {
            sb.AppendLine(WorkoutInProgress ? "devam eden bir antrenman var" : "kayıtlı antrenman yok");
        }

        return sb.ToString();
    }

    public static int? AgeOn(DateTime? dateOfBirth, DateOnly date)
    {
        if (dateOfBirth is null) return null;
        var dob = DateOnly.FromDateTime(dateOfBirth.Value);
        var age = date.Year - dob.Year;
        if (date < dob.AddYears(age)) age--;
        return age is > 0 and < 120 ? age : null;
    }

    private static string N(int value) => value.ToString("N0", Tr);

    private static string GoalLabel(FitnessGoal? goal) => goal is null ? "bilinmiyor" : FitnessGoals.Normalize(goal.Value) switch
    {
        FitnessGoal.LoseWeight => "yağ kaybı",
        FitnessGoal.GetStronger => "güçlenmek",
        _ => "kas geliştirmek",
    };

    private static string GenderLabel(Gender? gender) => gender switch
    {
        Domain.Entities.Enums.Gender.Male => "erkek",
        Domain.Entities.Enums.Gender.Female => "kadın",
        Domain.Entities.Enums.Gender.Other => "diğer",
        _ => "bilinmiyor",
    };

    private static string ActivityLabel(ActivityLevel? level) => level switch
    {
        Domain.Entities.Enums.ActivityLevel.Sedentary => "hareketsiz",
        Domain.Entities.Enums.ActivityLevel.LightlyActive => "az aktif",
        Domain.Entities.Enums.ActivityLevel.ModeratelyActive => "orta aktif",
        Domain.Entities.Enums.ActivityLevel.VeryActive => "çok aktif",
        Domain.Entities.Enums.ActivityLevel.ExtraActive => "son derece aktif",
        _ => "bilinmiyor",
    };

    private static string DietLabel(DietaryPreference? preference) => preference switch
    {
        Domain.Entities.Enums.DietaryPreference.Vegetarian => "vejetaryen",
        Domain.Entities.Enums.DietaryPreference.Vegan => "vegan",
        Domain.Entities.Enums.DietaryPreference.None => "özel tercih yok",
        _ => "belirtilmemiş",
    };

    private static string AllergenLabel(string name) => name switch
    {
        "gluten" => "gluten",
        "milk" => "süt",
        "egg" => "yumurta",
        "treeNuts" => "sert kabuklu yemişler",
        "peanut" => "yer fıstığı",
        "soy" => "soya",
        "fish" => "balık",
        "shellfish" => "kabuklu deniz ürünleri",
        "sesame" => "susam",
        _ => name,
    };
}
