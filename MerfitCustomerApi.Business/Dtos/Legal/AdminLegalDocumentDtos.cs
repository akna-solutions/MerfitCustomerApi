using System.ComponentModel.DataAnnotations;
using MerfitCustomerApi.Business.Common.Pagination;
using MerfitCustomerApi.Domain.Entities.Enums;

namespace MerfitCustomerApi.Business.Dtos.Admin.Legal;

public class AdminLegalDocumentListItemDto
{
    public long Id { get; set; }
    public string Type { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string Language { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public DateTime PublishedAt { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class AdminLegalDocumentDetailDto : AdminLegalDocumentListItemDto
{
    public string Content { get; set; } = string.Empty;
    public DateTime? UpdatedAt { get; set; }
}

public class AdminLegalDocumentListRequest : PagedRequest
{
    public LegalDocumentType? Type { get; set; }
    public string? Language { get; set; }
    public bool? IsActive { get; set; }
}

public class AdminUpsertLegalDocumentRequest
{
    [Required(ErrorMessage = "Belge turu zorunludur.")]
    public LegalDocumentType Type { get; set; }

    [Required(ErrorMessage = "Surum alani zorunludur.")]
    [StringLength(50, MinimumLength = 1)]
    public string Version { get; set; } = string.Empty;

    [Required(ErrorMessage = "Dil kodu zorunludur.")]
    [StringLength(10, MinimumLength = 2)]
    public string Language { get; set; } = string.Empty;

    [Required(ErrorMessage = "Baslik zorunludur.")]
    [StringLength(300, MinimumLength = 2)]
    public string Title { get; set; } = string.Empty;

    [Required(ErrorMessage = "Icerik zorunludur.")]
    public string Content { get; set; } = string.Empty;

    public DateTime? PublishedAt { get; set; }

    public bool IsActive { get; set; }
}
