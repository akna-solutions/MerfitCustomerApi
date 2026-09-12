using MerfitCustomerApi.Business.Common.Pagination;

namespace MerfitCustomerApi.Business.Dtos.Customer.Workouts;

/// <summary>
/// MerfitNativeApp workouts/types.ts::Workout tipi ile birebir eslesen liste ogesi.
/// </summary>
public class CustomerWorkoutListItemDto
{
    public long Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Tagline { get; set; }

    /// <summary>Dakika cinsinden ham deger (mobil taraf kendi filtre mantiginda sayi olarak kullaniyor).</summary>
    public int DurationMin { get; set; }

    /// <summary>"Baslangic" | "Orta" | "Ileri".</summary>
    public string Difficulty { get; set; } = string.Empty;

    public string Category { get; set; } = string.Empty;
    public string? MuscleGroup { get; set; }
    public List<string> Equipment { get; set; } = new();
    public string? ImageUrl { get; set; }
    public bool Featured { get; set; }
}

/// <summary>Antrenman detay ekrani (workout/ActiveWorkout oncesi onizleme) icin egzersiz listesini de icerir.</summary>
public class CustomerWorkoutDetailDto : CustomerWorkoutListItemDto
{
    public string? Description { get; set; }
    public List<CustomerWorkoutExerciseDto> Exercises { get; set; } = new();
}

public class CustomerWorkoutExerciseDto
{
    public long ExerciseId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int Order { get; set; }
    public int Sets { get; set; }
    public int? Reps { get; set; }
    public int? RestSeconds { get; set; }
    public int? DurationSeconds { get; set; }
    public string? VideoUrl { get; set; }
    public string? ImageUrl { get; set; }
}

/// <summary>
/// GET /api/workouts icin sorgu parametreleri. Mobil taraftaki WorkoutFilters ile eslesir:
/// difficulty/equipment/muscleGroup coklu secim (virgulle ayrilmis), duration ise araliktir.
/// </summary>
public class CustomerWorkoutListRequest : PagedRequest
{
    /// <summary>Virgulle ayrilmis zorluk degerleri (orn. "Beginner,Intermediate"); bos ise tumu.</summary>
    public string? Difficulty { get; set; }

    /// <summary>"under20" | "20to40" | "40plus"; birden fazlaysa virgulle ayrilir.</summary>
    public string? Duration { get; set; }

    /// <summary>Virgulle ayrilmis ekipman slug'lari.</summary>
    public string? Equipment { get; set; }

    /// <summary>Virgulle ayrilmis kas grubu slug'lari.</summary>
    public string? MuscleGroup { get; set; }

    public string? CategorySlug { get; set; }

    /// <summary>true ise sadece kullanicinin profiline (deneyim seviyesi + sahip oldugu ekipman) uygun antrenmanlar doner.</summary>
    public bool? Personalized { get; set; }
}
