using System.ComponentModel.DataAnnotations;
using MerfitCustomerApi.Business.Common.Pagination;
using MerfitCustomerApi.Domain.Entities.Enums;

namespace MerfitCustomerApi.Business.Dtos.Admin.Exercises;

public class AdminExerciseListItemDto
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string Difficulty { get; set; } = string.Empty;
    public long PrimaryMuscleGroupId { get; set; }
    public string PrimaryMuscleGroupName { get; set; } = string.Empty;
    public string? ImageUrl { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class AdminExerciseDetailDto : AdminExerciseListItemDto
{
    public string? Description { get; set; }
    public string? Instructions { get; set; }
    public string? EquipmentType { get; set; }
    public string? VideoUrl { get; set; }
    public decimal? CaloriesPerMinute { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

public class AdminExerciseListRequest : PagedRequest
{
    public DifficultyLevel? Difficulty { get; set; }
    public long? MuscleGroupId { get; set; }
    public bool? IsActive { get; set; }
}

public class AdminUpsertExerciseRequest
{
    [Required(ErrorMessage = "Isim alani zorunludur.")]
    [StringLength(200, MinimumLength = 2)]
    public string Name { get; set; } = string.Empty;

    [Required(ErrorMessage = "Slug alani zorunludur.")]
    [StringLength(200, MinimumLength = 2)]
    [RegularExpression("^[a-z0-9]+(-[a-z0-9]+)*$", ErrorMessage = "Slug yalnizca kucuk harf, rakam ve tire (-) icerebilir.")]
    public string Slug { get; set; } = string.Empty;

    [StringLength(4000)]
    public string? Description { get; set; }

    [StringLength(8000)]
    public string? Instructions { get; set; }

    [Required(ErrorMessage = "Zorluk seviyesi zorunludur.")]
    public DifficultyLevel Difficulty { get; set; }

    [Required(ErrorMessage = "Ana kas grubu zorunludur.")]
    public long PrimaryMuscleGroupId { get; set; }

    [StringLength(200)]
    public string? EquipmentType { get; set; }

    [StringLength(1000)]
    [Url(ErrorMessage = "Gecerli bir video URL'i giriniz.")]
    public string? VideoUrl { get; set; }

    [StringLength(1000)]
    [Url(ErrorMessage = "Gecerli bir gorsel URL'i giriniz.")]
    public string? ImageUrl { get; set; }

    [Range(0, 100, ErrorMessage = "Dakika basina kalori 0-100 araliginda olmalidir.")]
    public decimal? CaloriesPerMinute { get; set; }

    public bool IsActive { get; set; } = true;
}

public class AdminUpdateExerciseStatusRequest
{
    [Required]
    public bool IsActive { get; set; }
}
