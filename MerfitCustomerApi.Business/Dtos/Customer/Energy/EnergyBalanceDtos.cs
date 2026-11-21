namespace MerfitCustomerApi.Business.Dtos.Customer.Energy;

/// <summary>Durum renkleri. Mutlak saglik yargisi degil, kullanicinin hedefine gore isarettir.</summary>
public static class EnergyTones
{
    /// <summary>Hedefle uyumlu.</summary>
    public const string Green = "green";
    /// <summary>Notr bilgi / ilerleme (gun devam ediyor, veri eksik, dengeye yakin).</summary>
    public const string Blue = "blue";
    /// <summary>Kalori fazlasi (hedef kilo almak degilse ya da kontrollu fazlanin ustundeyse).</summary>
    public const string Orange = "orange";
    /// <summary>Hedef acisindan dikkat (orn. surdurulemeyecek kadar buyuk acik, kilo alma hedefinde acik).</summary>
    public const string Red = "red";
}

/// <summary>Gunun durum kodu.</summary>
public static class EnergyStatuses
{
    public const string Aligned = "aligned";
    public const string Neutral = "neutral";
    public const string Surplus = "surplus";
    public const string Attention = "attention";
    public const string InProgress = "in_progress";
    public const string InsufficientData = "insufficient_data";
}

/// <summary>GET /api/energy-balance/day - tek gunun kalori dengesi (ana sayfa karti ve detay sayfasi).</summary>
public class EnergyDayDto
{
    /// <summary>Kullanicinin yerel gunu (yyyy-MM-dd).</summary>
    public DateOnly Date { get; set; }
    public bool IsToday { get; set; }
    /// <summary>Bugun icin gunun gecen orani (0-1); gecmis gunlerde 1. Bugunun BMR'si bu oranla "su ana kadar" hesaplanir.</summary>
    public double ElapsedDayFraction { get; set; }

    /// <summary>Besinlerden alinan kalori (kcal).</summary>
    public int IntakeCalories { get; set; }
    /// <summary>Toplam harcanan kalori (kcal) = BMR + yuruyus + gunluk hareketlilik + antrenman + besinlerin termik etkisi. BMR hesaplanamazsa null.</summary>
    public int? ExpenditureCalories { get; set; }
    /// <summary>Alinan - harcanan. Pozitif: fazla, negatif: acik. Harcama hesaplanamazsa null.</summary>
    public int? Balance { get; set; }
    /// <summary>"surplus" | "deficit" | "balanced"; denge yoksa null.</summary>
    public string? BalanceKind { get; set; }

    /// <summary>Gunluk kalori hedefi (mevcut NutritionGoal); hesaplanmadiysa null.</summary>
    public int? TargetCalories { get; set; }
    /// <summary>Hedef - alinan (negatifse hedef asildi). Hedef yoksa null.</summary>
    public int? RemainingCalories { get; set; }
    /// <summary>Hedefin ongordugu gunluk denge (hedef - tahmini TDEE; orn. -500). Hesaplanamazsa null.</summary>
    public int? PlannedBalance { get; set; }
    /// <summary>"lose" | "maintain" | "gain" | "unknown".</summary>
    public string GoalDirection { get; set; } = "unknown";

    public string Status { get; set; } = EnergyStatuses.InsufficientData;
    public string Tone { get; set; } = EnergyTones.Blue;
    /// <summary>Dengenin hedefle iliskisini anlatan kisa aciklama.</summary>
    public string Headline { get; set; } = string.Empty;

    public EnergyExpenditureDto Expenditure { get; set; } = new();
    public EnergyNutritionDto Nutrition { get; set; } = new();
    public EnergyActivityDto Activity { get; set; } = new();
    public List<EnergyInsightDto> Insights { get; set; } = new();
    public EnergyDataQualityDto DataQuality { get; set; } = new();
}

/// <summary>Harcama dagilimi (kcal). Kalemler birbirini kapsamaz; toplamlari ExpenditureCalories'tir.</summary>
public class EnergyExpenditureDto
{
    /// <summary>Bazal metabolizma (Mifflin-St Jeor). Bugun icin su ana kadarki kisim; hesaplanamazsa null.</summary>
    public int? Bmr { get; set; }
    /// <summary>Tam gunluk BMR (bilgi amacli); hesaplanamazsa null.</summary>
    public int? BmrFullDay { get; set; }
    /// <summary>Saatlik kovada esik ustu adimlardan (kesintisiz yuruyus) gelen aktif kalori.</summary>
    public int Walking { get; set; }
    /// <summary>Kalan gun ici adimlardan gelen aktif kalori.</summary>
    public int DailyMovement { get; set; }
    /// <summary>Antrenmanlarin net (dinlenme ustu) kalorisi.</summary>
    public int Workout { get; set; }
    /// <summary>Diger kaynaklar: besinlerin termik etkisi (alinan kalorinin ThermicEffectRatio kadari).</summary>
    public int ThermicEffect { get; set; }
    /// <summary>Antrenmanla cakistigi icin adim kalorisinden dusulen kisim (iki kez sayilmadi).</summary>
    public int DeduplicatedStepCalories { get; set; }
    /// <summary>Hesap yontemi: "bmr_plus_active".</summary>
    public string Method { get; set; } = "bmr_plus_active";
}

public class EnergyNutritionDto
{
    public int Calories { get; set; }
    public int Protein { get; set; }
    public int Carbs { get; set; }
    public int Fat { get; set; }
    public int? ProteinTarget { get; set; }
    public int? CarbsTarget { get; set; }
    public int? FatTarget { get; set; }
    public int ItemCount { get; set; }
    /// <summary>Ogun turune gore (breakfast, lunch, dinner, snack); kaydi olmayan ogunler 0 ile doner.</summary>
    public List<EnergyMealDto> Meals { get; set; } = new();
}

public class EnergyMealDto
{
    /// <summary>breakfast | lunch | dinner | snack.</summary>
    public string Type { get; set; } = string.Empty;
    /// <summary>Gosterim adi (Kahvalti, Ogle Yemegi, ...).</summary>
    public string Label { get; set; } = string.Empty;
    public int Calories { get; set; }
    public int Protein { get; set; }
    public int Carbs { get; set; }
    public int Fat { get; set; }
    public int ItemCount { get; set; }
}

public class EnergyActivityDto
{
    public int Steps { get; set; }
    /// <summary>Kalori dengesine "yuruyus" olarak sayilan adimlar (antrenmanla cakisanlar haric).</summary>
    public int WalkingSteps { get; set; }
    public int DistanceMeters { get; set; }
    public int WorkoutMinutes { get; set; }
    public int WorkoutCalories { get; set; }
    /// <summary>Antrenman sirasinda atildigi icin adim kalorisine katilmayan adimlar.</summary>
    public int OverlappingSteps { get; set; }
    public List<EnergyWorkoutDto> Workouts { get; set; } = new();
}

public class EnergyWorkoutDto
{
    public long SessionId { get; set; }
    public string Title { get; set; } = string.Empty;
    /// <summary>strength | cardio | running | cycling | walking | mobility.</summary>
    public string Kind { get; set; } = string.Empty;
    public DateTime CompletedAt { get; set; }
    public int Minutes { get; set; }
    public int Calories { get; set; }
}

public class EnergyInsightDto
{
    public string Code { get; set; } = string.Empty;
    public string Tone { get; set; } = EnergyTones.Blue;
    public string Text { get; set; } = string.Empty;
}

/// <summary>Hesaplamanin dayandigi verinin durumu. Eksik veride kesin yorum yapilmaz.</summary>
public class EnergyDataQualityDto
{
    public bool HasMeals { get; set; }
    public bool HasSteps { get; set; }
    public bool StepsVerified { get; set; } = true;
    public DateTime? StepsLastSyncedAt { get; set; }
    public bool StepSyncStale { get; set; }
    public bool ProfileComplete { get; set; }
    /// <summary>BMR icin eksik profil alanlari: weight, height, birthDate.</summary>
    public List<string> MissingProfileFields { get; set; } = new();
    /// <summary>Cinsiyet bilinmiyorsa BMR erkek/kadin sabitlerinin ortalamasiyla hesaplanir.</summary>
    public bool GenderEstimated { get; set; }
    /// <summary>Suresi veya kalorisi belirlenemeyen antrenman sayisi (kaloriye katilmadi).</summary>
    public int WorkoutsWithoutCalories { get; set; }
    /// <summary>Kilo bu gune ait/oncesindeki tartimdan (true) ya da profilden (false) alindi.</summary>
    public bool WeightFromMeasurement { get; set; }
    public decimal? WeightKgUsed { get; set; }
    public bool IsPartialDay { get; set; }
    /// <summary>Gun tamamlanmis, besin kaydi var ve harcama hesaplanabildi: istatistiklere ve kesin yoruma girer.</summary>
    public bool IsComplete { get; set; }
}

/// <summary>GET /api/energy-balance?from&amp;to - aralik (son 7/30 gun, ozel aralik) grafikleri ve istatistikler.</summary>
public class EnergyRangeResponse
{
    public DateOnly From { get; set; }
    public DateOnly To { get; set; }
    public DateOnly Today { get; set; }
    public string TimeZone { get; set; } = string.Empty;
    public int? TargetCalories { get; set; }
    public string GoalDirection { get; set; } = "unknown";
    public List<EnergyRangeDayDto> Days { get; set; } = new();
    public EnergyRangeSummaryDto Summary { get; set; } = new();
    /// <summary>To'dan geriye 7'ser gunluk haftalar (en eski once).</summary>
    public List<EnergyWeekDto> Weeks { get; set; } = new();
}

public class EnergyRangeDayDto
{
    public DateOnly Date { get; set; }
    public bool IsToday { get; set; }
    public int IntakeCalories { get; set; }
    public int? ExpenditureCalories { get; set; }
    public int? Balance { get; set; }
    public string Tone { get; set; } = EnergyTones.Blue;
    public string Status { get; set; } = EnergyStatuses.InsufficientData;
    public bool HasMeals { get; set; }
    public bool IsComplete { get; set; }
    public int Steps { get; set; }
    public int WorkoutCalories { get; set; }
}

public class EnergyRangeSummaryDto
{
    /// <summary>Ortalamalara giren (tamamlanmis, besin kayitli, harcamasi hesaplanabilen) gun sayisi.</summary>
    public int CompleteDays { get; set; }
    public int TotalDays { get; set; }
    public int? AverageIntake { get; set; }
    public int? AverageExpenditure { get; set; }
    public int? AverageBalance { get; set; }
    public int? TotalBalance { get; set; }
    /// <summary>Tamamlanmis gunlerin toplam dengesinden tahmini kilo degisimi (kg, TotalBalance / KcalPerKg).</summary>
    public decimal? EstimatedWeightChangeKg { get; set; }
    public EnergyWeightTrendDto Weight { get; set; } = new();
    /// <summary>Kalori dengesi ile kilo degisiminin birlikte yorumu; veri yetersizse null.</summary>
    public string? Analysis { get; set; }
}

public class EnergyWeightTrendDto
{
    public DateOnly? StartDate { get; set; }
    public decimal? StartKg { get; set; }
    public DateOnly? EndDate { get; set; }
    public decimal? EndKg { get; set; }
    public decimal? ChangeKg { get; set; }
    public int MeasurementCount { get; set; }
}

public class EnergyWeekDto
{
    public DateOnly Start { get; set; }
    public DateOnly End { get; set; }
    public int CompleteDays { get; set; }
    public int? AverageBalance { get; set; }
    public int? AverageIntake { get; set; }
    public int? AverageExpenditure { get; set; }
}
