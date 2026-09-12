using System.ComponentModel.DataAnnotations;
using MerfitCustomerApi.Business.Common.Pagination;

namespace MerfitCustomerApi.Business.Dtos.Admin.Languages;

public class AdminLanguageDto
{
    public long Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsDefault { get; set; }
    public bool IsActive { get; set; }
    public int TranslationCount { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

public class AdminLanguageListRequest : PagedRequest
{
    public bool? IsActive { get; set; }
}

public class AdminUpsertLanguageRequest
{
    [Required(ErrorMessage = "Kod alani zorunludur.")]
    [StringLength(10, MinimumLength = 2, ErrorMessage = "Kod ISO 639-1/BCP-47 formatinda olmalidir (orn. tr, en, en-US).")]
    public string Code { get; set; } = string.Empty;

    [Required(ErrorMessage = "Isim alani zorunludur.")]
    [StringLength(100, MinimumLength = 2)]
    public string Name { get; set; } = string.Empty;

    /// <summary>true ise bu dil varsayilan yapilir ve diger tum dillerin IsDefault degeri otomatik olarak false'a cekilir.</summary>
    public bool IsDefault { get; set; }

    public bool IsActive { get; set; } = true;
}
