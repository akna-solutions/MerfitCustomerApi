using System.ComponentModel.DataAnnotations;
using MerfitCustomerApi.Business.Common.Pagination;

namespace MerfitCustomerApi.Business.Dtos.Admin.WorkoutCategories;

public class AdminWorkoutCategoryDto
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? ImageUrl { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

public class AdminWorkoutCategoryListRequest : PagedRequest
{
    public bool? IsActive { get; set; }
}

public class AdminUpsertWorkoutCategoryRequest
{
    [Required(ErrorMessage = "Isim alani zorunludur.")]
    [StringLength(150, MinimumLength = 2)]
    public string Name { get; set; } = string.Empty;

    [Required(ErrorMessage = "Slug alani zorunludur.")]
    [StringLength(150, MinimumLength = 2)]
    [RegularExpression("^[a-z0-9]+(-[a-z0-9]+)*$", ErrorMessage = "Slug yalnizca kucuk harf, rakam ve tire (-) icerebilir.")]
    public string Slug { get; set; } = string.Empty;

    [StringLength(2000)]
    public string? Description { get; set; }

    [StringLength(1000)]
    [Url(ErrorMessage = "Gecerli bir URL giriniz.")]
    public string? ImageUrl { get; set; }

    public bool IsActive { get; set; } = true;
}
