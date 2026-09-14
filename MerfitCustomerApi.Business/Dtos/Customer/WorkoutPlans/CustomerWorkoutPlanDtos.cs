namespace MerfitCustomerApi.Business.Dtos.Customer.WorkoutPlans;

/// <summary>
/// GET /api/my-plan yaniti. Kullanicinin aktif kisisel antrenman programinin tam gorunumu.
/// MerfitNativeApp "Kişisel Programım" ekraninin ihtiyac duydugu tum veriyi tek cagrida saglar.
/// </summary>
public class CustomerWorkoutPlanDto
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;

    /// <summary>"LoseWeight" | "BuildMuscle" | "GetStronger" | "ImproveFitness" | "MaintainWeight" | "ImproveEndurance"</summary>
    public string? Goal { get; set; }

    public DateTime StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public bool IsActive { get; set; }

    /// <summary>
    /// Kurallardan (AI kullanilmadan) uretilmis, kullaniciya gosterilebilir kisa aciklama.
    /// Orn: "Başlangıç seviyene, haftada 3 gün antrenman tercihine ve mevcut ekipmanlarına göre hazırlandı."
    /// </summary>
    public string? Summary { get; set; }

    public List<CustomerWorkoutPlanDayDto> Days { get; set; } = new();
}

public class CustomerWorkoutPlanDayDto
{
    /// <summary>"Monday" | "Tuesday" | ... (System.DayOfWeek.ToString()).</summary>
    public string DayOfWeek { get; set; } = string.Empty;

    public CustomerWorkoutPlanWorkoutDto Workout { get; set; } = new();
    public List<CustomerWorkoutPlanExerciseDto> Exercises { get; set; } = new();
}

public class CustomerWorkoutPlanWorkoutDto
{
    public long Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public int DurationMin { get; set; }
    public string? ImageUrl { get; set; }
}

public class CustomerWorkoutPlanExerciseDto
{
    public long ExerciseId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int Order { get; set; }
    public int Sets { get; set; }
    public int? Reps { get; set; }
    public int? RestSeconds { get; set; }
    public int? DurationSeconds { get; set; }
}

/// <summary>
/// GET /api/my-plan/today yaniti. Bugunun (System.DayOfWeek) aktif plandaki karsiligini dondurur.
/// Bugune ait plan yoksa (orn. kullanicinin o gun antrenmani yok, veya henuz aktif plan yok)
/// HasWorkoutToday=false doner ve Day null olur - 404 firlatilmaz (bkz. Faz 2 gereksinimi).
/// </summary>
public class CustomerTodayWorkoutPlanResponse
{
    public bool HasWorkoutToday { get; set; }

    /// <summary>Bugun icin herhangi bir aktif plan bulunamadiysa true (henuz Completed olmamis olabilir).</summary>
    public bool HasActivePlan { get; set; }

    public CustomerWorkoutPlanDayDto? Day { get; set; }

    /// <summary>WorkoutSession baslatirken kullanilacak WorkoutPlanDay kimligi (kisisellestirilmis hedeflerin session'a tasinmasi icin).</summary>
    public long? WorkoutPlanDayId { get; set; }
}
