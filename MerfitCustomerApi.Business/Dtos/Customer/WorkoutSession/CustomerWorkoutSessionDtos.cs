using System.ComponentModel.DataAnnotations;

namespace MerfitCustomerApi.Business.Dtos.Customer.WorkoutSessions;

/// <summary>POST /api/workout-sessions govdesi; bir antrenmani baslatmak icin sadece hangi Workout'un secildigi gerekir.</summary>
public class StartWorkoutSessionRequest
{
    [Required(ErrorMessage = "WorkoutId alani zorunludur.")]
    public long WorkoutId { get; set; }
}

/// <summary>
/// Aktif oturumun tam gorunumu. MerfitNativeApp workout/types.ts::WorkoutSession + Exercise[]
/// verisinin API karsiligi; ActiveWorkout ekrani bu DTO'yu direkt render edebilir.
/// </summary>
public class CustomerWorkoutSessionDto
{
    public long Id { get; set; }
    public long WorkoutId { get; set; }
    public string Title { get; set; } = string.Empty;

    /// <summary>"Started" | "Paused" | "Completed" | "Cancelled" | "Abandoned".</summary>
    public string Status { get; set; } = string.Empty;
    public DateTime StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public int? DurationSeconds { get; set; }
    public decimal? CaloriesBurned { get; set; }

    public List<CustomerWorkoutSessionExerciseDto> Exercises { get; set; } = new();
}

public class CustomerWorkoutSessionExerciseDto
{
    /// <summary>WorkoutSessionExercise.Id - set loglarken bu kimlik kullanilir.</summary>
    public long SessionExerciseId { get; set; }
    public long ExerciseId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int Order { get; set; }

    /// <summary>Hedef set sayisi (antrenman planindan).</summary>
    public int TargetSets { get; set; }
    public int? TargetReps { get; set; }
    public int? RestSeconds { get; set; }
    public int? DurationSeconds { get; set; }
    public string? VideoUrl { get; set; }
    public string? ImageUrl { get; set; }

    /// <summary>Bu oturumda simdiye kadar loglanan setler.</summary>
    public List<CustomerWorkoutSetLogDto> CompletedSets { get; set; } = new();

    /// <summary>Kullanicinin bu egzersizdeki onceki en iyi performansi ("YENI REKOR" rozeti icin); hic yoksa null.</summary>
    public CustomerPreviousBestDto? PreviousBest { get; set; }
}

public class CustomerWorkoutSetLogDto
{
    public int SetNumber { get; set; }
    public decimal? WeightKg { get; set; }
    public int? Reps { get; set; }
    public int? DurationSeconds { get; set; }
    public decimal? Rpe { get; set; }
}

public class CustomerPreviousBestDto
{
    public decimal WeightKg { get; set; }
    public int Reps { get; set; }
}

/// <summary>POST /api/workout-sessions/{id}/exercises/{sessionExerciseId}/sets govdesi.</summary>
public class LogWorkoutSetRequest
{
    [Range(1, 50, ErrorMessage = "SetNumber 1-50 araliginda olmalidir.")]
    public int SetNumber { get; set; }

    [Range(0, 1000, ErrorMessage = "WeightKg gecerli bir deger olmalidir.")]
    public decimal? WeightKg { get; set; }

    [Range(0, 1000, ErrorMessage = "Reps gecerli bir deger olmalidir.")]
    public int? Reps { get; set; }

    [Range(0, 36000, ErrorMessage = "DurationSeconds gecerli bir deger olmalidir.")]
    public int? DurationSeconds { get; set; }

    [Range(0, 10, ErrorMessage = "Rpe 0-10 araliginda olmalidir.")]
    public decimal? Rpe { get; set; }
}

/// <summary>PUT /api/workout-sessions/{id}/complete govdesi.</summary>
public class CompleteWorkoutSessionRequest
{
    [Range(1, 36000, ErrorMessage = "DurationSeconds gecerli bir deger olmalidir.")]
    public int DurationSeconds { get; set; }

    [Range(0, 10000, ErrorMessage = "CaloriesBurned gecerli bir deger olmalidir.")]
    public decimal? CaloriesBurned { get; set; }

    [StringLength(1000)]
    public string? Notes { get; set; }
}

/// <summary>
/// Tamamlanan oturumun ozeti. MerfitNativeApp WorkoutSummary ekraninin ihtiyac duydugu
/// "yeni rekor" bilgisini de icerir.
/// </summary>
public class CustomerWorkoutSessionSummaryDto
{
    public long SessionId { get; set; }
    public int DurationSeconds { get; set; }
    public decimal? CaloriesBurned { get; set; }
    public List<CustomerNewPersonalRecordDto> NewPersonalRecords { get; set; } = new();
}

public class CustomerNewPersonalRecordDto
{
    public long ExerciseId { get; set; }
    public string ExerciseName { get; set; } = string.Empty;
    public decimal WeightKg { get; set; }
    public int Reps { get; set; }
}
