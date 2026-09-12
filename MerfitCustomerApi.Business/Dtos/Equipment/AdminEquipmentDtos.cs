using System.ComponentModel.DataAnnotations;
using MerfitCustomerApi.Business.Common.Pagination;

namespace MerfitCustomerApi.Business.Dtos.Admin.Equipment;

public class AdminEquipmentDto
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

public class AdminEquipmentListRequest : PagedRequest
{
}

public class AdminUpsertEquipmentRequest
{
    [Required(ErrorMessage = "Isim alani zorunludur.")]
    [StringLength(150, MinimumLength = 2)]
    public string Name { get; set; } = string.Empty;

    [Required(ErrorMessage = "Slug alani zorunludur.")]
    [StringLength(150, MinimumLength = 2)]
    [RegularExpression("^[a-z0-9]+(-[a-z0-9]+)*$", ErrorMessage = "Slug yalnizca kucuk harf, rakam ve tire (-) icerebilir.")]
    public string Slug { get; set; } = string.Empty;
}
