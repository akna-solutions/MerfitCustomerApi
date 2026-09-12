using System.ComponentModel.DataAnnotations;
using MerfitCustomerApi.Business.Common.Pagination;

namespace MerfitCustomerApi.Business.Dtos.Admin.Achievements;

public class AdminAchievementDto
{
    public long Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Icon { get; set; }
    public int Points { get; set; }
    public string ConditionType { get; set; } = string.Empty;
    public string ConditionValue { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

public class AdminAchievementListRequest : PagedRequest
{
    public bool? IsActive { get; set; }
}

public class AdminUpsertAchievementRequest
{
    [Required(ErrorMessage = "Kod alani zorunludur.")]
    [StringLength(100, MinimumLength = 2)]
    [RegularExpression("^[A-Z0-9_]+$", ErrorMessage = "Kod yalnizca buyuk harf, rakam ve alt tire (_) icerebilir.")]
    public string Code { get; set; } = string.Empty;

    [Required(ErrorMessage = "Baslik alani zorunludur.")]
    [StringLength(200, MinimumLength = 2)]
    public string Title { get; set; } = string.Empty;

    [StringLength(1000)]
    public string? Description { get; set; }

    [StringLength(500)]
    public string? Icon { get; set; }

    [Range(0, 100000, ErrorMessage = "Puan 0 veya daha buyuk olmalidir.")]
    public int Points { get; set; }

    [Required(ErrorMessage = "Kosul turu zorunludur.")]
    [StringLength(100)]
    public string ConditionType { get; set; } = string.Empty;

    [Required(ErrorMessage = "Kosul degeri zorunludur.")]
    [StringLength(200)]
    public string ConditionValue { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;
}

public class AdminUpdateAchievementStatusRequest
{
    [Required]
    public bool IsActive { get; set; }
}

public class AdminAchievementUserListItemDto
{
    public long UserId { get; set; }
    public string UserEmail { get; set; } = string.Empty;
    public DateTime EarnedAt { get; set; }
}
