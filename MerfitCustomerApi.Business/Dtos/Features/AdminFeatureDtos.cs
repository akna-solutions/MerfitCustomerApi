using System.ComponentModel.DataAnnotations;
using MerfitCustomerApi.Business.Common.Pagination;

namespace MerfitCustomerApi.Business.Dtos.Admin.Features;

public class AdminFeatureDto
{
    public long Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

public class AdminFeatureListRequest : PagedRequest
{
}

public class AdminUpsertFeatureRequest
{
    [Required(ErrorMessage = "Kod alani zorunludur.")]
    [StringLength(100, MinimumLength = 2)]
    [RegularExpression("^[A-Z0-9_]+$", ErrorMessage = "Kod yalnizca buyuk harf, rakam ve alt tire (_) icerebilir.")]
    public string Code { get; set; } = string.Empty;

    [Required(ErrorMessage = "Isim alani zorunludur.")]
    [StringLength(200, MinimumLength = 2)]
    public string Name { get; set; } = string.Empty;

    [StringLength(1000)]
    public string? Description { get; set; }
}
