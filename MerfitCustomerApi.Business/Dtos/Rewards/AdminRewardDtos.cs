using System.ComponentModel.DataAnnotations;
using MerfitCustomerApi.Business.Common.Pagination;

namespace MerfitCustomerApi.Business.Dtos.Admin.Rewards;

public class AdminRewardDto
{
    public long Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? ImageUrl { get; set; }
    public string RewardType { get; set; } = string.Empty;
    public string? Value { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

public class AdminRewardListRequest : PagedRequest
{
    public bool? IsActive { get; set; }
    public string? RewardType { get; set; }
}

public class AdminUpsertRewardRequest
{
    [Required(ErrorMessage = "Baslik alani zorunludur.")]
    [StringLength(200, MinimumLength = 2)]
    public string Title { get; set; } = string.Empty;

    [StringLength(2000)]
    public string? Description { get; set; }

    [StringLength(1000)]
    [Url(ErrorMessage = "Gecerli bir gorsel URL'i giriniz.")]
    public string? ImageUrl { get; set; }

    [Required(ErrorMessage = "Odul turu zorunludur.")]
    [StringLength(50)]
    public string RewardType { get; set; } = string.Empty;

    [StringLength(500)]
    public string? Value { get; set; }

    public bool IsActive { get; set; } = true;
}

public class AdminUpdateRewardStatusRequest
{
    [Required]
    public bool IsActive { get; set; }
}
