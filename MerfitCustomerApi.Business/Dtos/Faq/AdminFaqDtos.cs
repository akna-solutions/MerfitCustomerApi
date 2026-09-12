using System.ComponentModel.DataAnnotations;
using MerfitCustomerApi.Business.Common.Pagination;

namespace MerfitCustomerApi.Business.Dtos.Admin.Faq;

public class AdminFaqDto
{
    public long Id { get; set; }
    public long CategoryId { get; set; }
    public string CategoryName { get; set; } = string.Empty;
    public string Question { get; set; } = string.Empty;
    public string Answer { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

public class AdminFaqListRequest : PagedRequest
{
    public long? CategoryId { get; set; }
    public bool? IsActive { get; set; }
}

public class AdminUpsertFaqRequest
{
    [Required(ErrorMessage = "Kategori zorunludur.")]
    public long CategoryId { get; set; }

    [Required(ErrorMessage = "Soru alani zorunludur.")]
    [StringLength(500, MinimumLength = 2)]
    public string Question { get; set; } = string.Empty;

    [Required(ErrorMessage = "Cevap alani zorunludur.")]
    [StringLength(4000, MinimumLength = 2)]
    public string Answer { get; set; } = string.Empty;

    [Range(0, 10000)]
    public int SortOrder { get; set; }

    public bool IsActive { get; set; } = true;
}

public class AdminUpdateFaqStatusRequest
{
    [Required]
    public bool IsActive { get; set; }
}

/// <summary>PUT /api/admin/faqs/order govdesi; verilen Id'lerin SortOrder'ini toplu gunceller.</summary>
public class AdminReorderFaqsRequest
{
    [Required]
    public List<AdminFaqOrderItem> Items { get; set; } = new();
}

public class AdminFaqOrderItem
{
    [Required(ErrorMessage = "Id zorunludur.")]
    public long Id { get; set; }

    [Range(0, 10000)]
    public int SortOrder { get; set; }
}
