using System.ComponentModel.DataAnnotations;
using MerfitCustomerApi.Business.Common.Pagination;
using MerfitCustomerApi.Domain.Entities.Enums;

namespace MerfitCustomerApi.Business.Dtos.Admin.Workouts;

public class AdminWorkoutListItemDto
{
    public long Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public int DurationMin { get; set; }
    public string Difficulty { get; set; } = string.Empty;
    public long CategoryId { get; set; }
    public string CategoryName { get; set; } = string.Empty;
    public long? MuscleGroupId { get; set; }
    public string? MuscleGroupName { get; set; }
    public string? ImageUrl { get; set; }
    public bool IsFeatured { get; set; }
    public bool IsPremium { get; set; }
    public bool IsAiGenerated { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class AdminWorkoutDetailDto : AdminWorkoutListItemDto
{
    public string? Tagline { get; set; }
    public string? Description { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public int ExerciseCount { get; set; }
    public int EquipmentCount { get; set; }
}

public class AdminWorkoutListRequest : PagedRequest
{
    public long? CategoryId { get; set; }
    public long? MuscleGroupId { get; set; }
    public DifficultyLevel? Difficulty { get; set; }
    public bool? IsFeatured { get; set; }
    public bool? IsPremium { get; set; }
    public bool? IsAiGenerated { get; set; }
    public bool? IsActive { get; set; }
}

public class AdminUpsertWorkoutRequest
{
    [Required(ErrorMessage = "Baslik alani zorunludur.")]
    [StringLength(200, MinimumLength = 2)]
    public string Title { get; set; } = string.Empty;

    [Required(ErrorMessage = "Slug alani zorunludur.")]
    [StringLength(200, MinimumLength = 2)]
    [RegularExpression("^[a-z0-9]+(-[a-z0-9]+)*$", ErrorMessage = "Slug yalnizca kucuk harf, rakam ve tire (-) icerebilir.")]
    public string Slug { get; set; } = string.Empty;

    [StringLength(300)]
    public string? Tagline { get; set; }

    [StringLength(4000)]
    public string? Description { get; set; }

    [Range(1, 600, ErrorMessage = "Sure 1-600 dakika araliginda olmalidir.")]
    public int DurationMin { get; set; }

    [Required(ErrorMessage = "Zorluk seviyesi zorunludur.")]
    public DifficultyLevel Difficulty { get; set; }

    [Required(ErrorMessage = "Kategori zorunludur.")]
    public long CategoryId { get; set; }

    public long? MuscleGroupId { get; set; }

    [StringLength(1000)]
    [Url(ErrorMessage = "Gecerli bir gorsel URL'i giriniz.")]
    public string? ImageUrl { get; set; }

    public bool IsFeatured { get; set; }

    public bool IsPremium { get; set; }

    public bool IsActive { get; set; } = true;
}

public class AdminUpdateWorkoutStatusRequest
{
    [Required]
    public bool IsActive { get; set; }
}

public class AdminUpdateWorkoutFeaturedRequest
{
    [Required]
    public bool IsFeatured { get; set; }
}

public class AdminUpdateWorkoutPremiumRequest
{
    [Required]
    public bool IsPremium { get; set; }
}

public class AdminWorkoutExerciseItemDto
{
    public long ExerciseId { get; set; }
    public string ExerciseName { get; set; } = string.Empty;
    public int Order { get; set; }
    public int Sets { get; set; }
    public int? Reps { get; set; }
    public int? RestSeconds { get; set; }
    public int? DurationSeconds { get; set; }
    public string? Notes { get; set; }
    public bool IsOptional { get; set; }
}

/// <summary>
/// PUT /api/admin/workouts/{id}/exercises govdesi. Gonderilen liste, o antrenmanin
/// mevcut egzersiz listesinin YERINI ALIR (tam degistirme / replace-all semantigi).
/// </summary>
public class AdminSetWorkoutExercisesRequest
{
    [Required]
    [MinLength(0)]
    public List<AdminSetWorkoutExerciseItem> Exercises { get; set; } = new();
}

public class AdminSetWorkoutExerciseItem
{
    [Required(ErrorMessage = "ExerciseId zorunludur.")]
    public long ExerciseId { get; set; }

    [Range(0, 200)]
    public int Order { get; set; }

    [Range(1, 50, ErrorMessage = "Set sayisi 1-50 araliginda olmalidir.")]
    public int Sets { get; set; } = 1;

    [Range(1, 500)]
    public int? Reps { get; set; }

    [Range(0, 1800)]
    public int? RestSeconds { get; set; }

    [Range(1, 7200)]
    public int? DurationSeconds { get; set; }

    [StringLength(1000)]
    public string? Notes { get; set; }

    public bool IsOptional { get; set; }
}

public class AdminWorkoutEquipmentItemDto
{
    public long EquipmentId { get; set; }
    public string EquipmentName { get; set; } = string.Empty;
}

/// <summary>
/// PUT /api/admin/workouts/{id}/equipment govdesi. Gonderilen liste, o antrenmanin
/// mevcut ekipman listesinin YERINI ALIR (tam degistirme / replace-all semantigi).
/// </summary>
public class AdminSetWorkoutEquipmentRequest
{
    [Required]
    public List<long> EquipmentIds { get; set; } = new();
}
