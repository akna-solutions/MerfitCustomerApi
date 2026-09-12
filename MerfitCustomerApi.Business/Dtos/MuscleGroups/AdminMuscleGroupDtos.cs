using System.ComponentModel.DataAnnotations;
using MerfitCustomerApi.Business.Common.Pagination;

namespace MerfitCustomerApi.Business.Dtos.Admin.MuscleGroups;

public class AdminMuscleGroupDto
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

public class AdminMuscleGroupListRequest : PagedRequest
{
}

public class AdminUpsertMuscleGroupRequest
{
    [Required(ErrorMessage = "Isim alani zorunludur.")]
    [StringLength(150, MinimumLength = 2)]
    public string Name { get; set; } = string.Empty;

    [Required(ErrorMessage = "Slug alani zorunludur.")]
    [StringLength(150, MinimumLength = 2)]
    [RegularExpression("^[a-z0-9]+(-[a-z0-9]+)*$", ErrorMessage = "Slug yalnizca kucuk harf, rakam ve tire (-) icerebilir.")]
    public string Slug { get; set; } = string.Empty;
}
