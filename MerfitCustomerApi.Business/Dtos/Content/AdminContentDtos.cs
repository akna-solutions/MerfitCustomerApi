using System.ComponentModel.DataAnnotations;
using MerfitCustomerApi.Business.Common.Pagination;

namespace MerfitCustomerApi.Business.Dtos.Admin.Content;

public class AdminContentDto
{
    public long Id { get; set; }
    public string Key { get; set; } = string.Empty;

    /// <summary>Serbest metin, ancak onerilen degerler: Banner, Announcement, Campaign, FeatureCard, Promotional.</summary>
    public string Type { get; set; } = string.Empty;

    public string? Title { get; set; }
    public string? Description { get; set; }
    public string? ImageUrl { get; set; }
    public string? LinkUrl { get; set; }
    public bool IsActive { get; set; }
    public DateTime? StartAt { get; set; }
    public DateTime? EndAt { get; set; }

    /// <summary>StartAt/EndAt'e gore icerigin su an fiilen yayinda olup olmadigini belirtir (IsActive VE tarih araligi icinde).</summary>
    public bool IsCurrentlyLive { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

public class AdminContentListRequest : PagedRequest
{
    public string? Type { get; set; }
    public bool? IsActive { get; set; }
}

public class AdminUpsertContentRequest
{
    [Required(ErrorMessage = "Key alani zorunludur.")]
    [StringLength(150, MinimumLength = 2)]
    [RegularExpression("^[a-z0-9]+(-[a-z0-9]+)*$", ErrorMessage = "Key yalnizca kucuk harf, rakam ve tire (-) icerebilir.")]
    public string Key { get; set; } = string.Empty;

    [Required(ErrorMessage = "Tur (Type) alani zorunludur.")]
    [StringLength(50)]
    public string Type { get; set; } = string.Empty;

    [StringLength(300)]
    public string? Title { get; set; }

    [StringLength(2000)]
    public string? Description { get; set; }

    [StringLength(1000)]
    [Url(ErrorMessage = "Gecerli bir gorsel URL'i giriniz.")]
    public string? ImageUrl { get; set; }

    [StringLength(1000)]
    public string? LinkUrl { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime? StartAt { get; set; }

    public DateTime? EndAt { get; set; }
}

public class AdminUpdateContentStatusRequest
{
    [Required]
    public bool IsActive { get; set; }
}
