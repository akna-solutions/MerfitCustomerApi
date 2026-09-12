using System.ComponentModel.DataAnnotations;
using MerfitCustomerApi.Business.Common.Pagination;

namespace MerfitCustomerApi.Business.Dtos.Admin.Faq;

public class AdminFaqCategoryDto
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    public int FaqCount { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

public class AdminFaqCategoryListRequest : PagedRequest
{
}

public class AdminUpsertFaqCategoryRequest
{
    [Required(ErrorMessage = "Isim alani zorunludur.")]
    [StringLength(150, MinimumLength = 2)]
    public string Name { get; set; } = string.Empty;

    [Range(0, 10000)]
    public int SortOrder { get; set; }
}
