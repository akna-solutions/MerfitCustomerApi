namespace MerfitCustomerApi.Business.Dtos.Customer.Calendar;

/// <summary>
/// GET /api/progress/calendar - ilerleme ekranindaki aylik takvim ve ana sayfadaki haftalik aktivite bolumu icin
/// tarih araligindaki her gunun ozeti (tek istekte; gun basina ayri istek atilmaz).
/// </summary>
public class CustomerActivityCalendarResponse
{
    /// <summary>"yyyy-MM-dd" (dahil).</summary>
    public string From { get; set; } = string.Empty;

    /// <summary>"yyyy-MM-dd" (dahil).</summary>
    public string To { get; set; } = string.Empty;

    /// <summary>Kullanicinin yerel "bugun"u ("yyyy-MM-dd"); gelecekteki gunler bu tarihten sonradir.</summary>
    public string Today { get; set; } = string.Empty;

    /// <summary>Antrenman saatlerinin yerel gune cevrilmesinde kullanilan IANA saat dilimi.</summary>
    public string TimeZone { get; set; } = string.Empty;

    /// <summary>Gunluk kalori hedefi (NutritionGoal); hedef henuz hesaplanmadiysa null.</summary>
    public int? DailyCalorieTarget { get; set; }

    /// <summary>Kalori hedefinin "tamamlandi" sayildigi alt/ust oran (NutritionInsightEvaluator ile ayni).</summary>
    public double CalorieGoalLowerRatio { get; set; }
    public double CalorieGoalUpperRatio { get; set; }

    public List<CustomerCalendarDayDto> Days { get; set; } = new();
}

public class CustomerCalendarDayDto
{
    /// <summary>"yyyy-MM-dd".</summary>
    public string Date { get; set; } = string.Empty;

    public bool IsToday { get; set; }

    /// <summary>Bugunden sonraki gun; tamamlanmis aktivite gosterilmez.</summary>
    public bool IsFuture { get; set; }

    /// <summary>Aktif programda bu gun antrenman gunu mu; program yoksa null (bilinmiyor).</summary>
    public bool? IsPlannedWorkoutDay { get; set; }

    public CustomerCalendarWorkoutDto Workout { get; set; } = new();

    public CustomerCalendarNutritionDto Nutrition { get; set; } = new();

    public CustomerCalendarActivityDto Activity { get; set; } = new();
}

public class CustomerCalendarWorkoutDto
{
    /// <summary>O gun (yerel saatle) en az bir antrenman tamamlandi mi.</summary>
    public bool Completed { get; set; }

    public int TotalDurationSeconds { get; set; }

    public List<CustomerCalendarWorkoutSessionDto> Sessions { get; set; } = new();
}

public class CustomerCalendarWorkoutSessionDto
{
    public long Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public DateTime CompletedAt { get; set; }
    public int? DurationSeconds { get; set; }
    public int? CaloriesBurned { get; set; }

    /// <summary>En az bir seti kaydedilen egzersiz sayisi.</summary>
    public int ExerciseCount { get; set; }
    public int SetCount { get; set; }
}

/// <summary>Besinlerden ALINAN kalori ve makrolar (harcanan kalori degil).</summary>
public class CustomerCalendarNutritionDto
{
    public bool Logged { get; set; }
    public int ConsumedCalories { get; set; }
    public int Protein { get; set; }
    public int Carbs { get; set; }
    public int Fat { get; set; }

    /// <summary>ConsumedCalories / gunluk hedef; hedef yoksa null.</summary>
    public double? CalorieRatio { get; set; }

    /// <summary>Alinan kalori hedefin alt/ust oranlari arasindaysa true.</summary>
    public bool CalorieGoalMet { get; set; }

    public List<CustomerCalendarMealDto> Meals { get; set; } = new();
}

public class CustomerCalendarMealDto
{
    /// <summary>"Kahvaltı" | "Öğle Yemeği" | "Akşam Yemeği" | "Atıştırmalık".</summary>
    public string Type { get; set; } = string.Empty;
    public int Calories { get; set; }
    public int ItemCount { get; set; }
}

/// <summary>
/// Antrenmanlarda HARCANAN tahmini kalori ve aktif sure. Uygulamada adim verisi tutulmadigi icin adim alani yoktur.
/// </summary>
public class CustomerCalendarActivityDto
{
    public int CaloriesBurned { get; set; }
    public int ActiveMinutes { get; set; }
}
