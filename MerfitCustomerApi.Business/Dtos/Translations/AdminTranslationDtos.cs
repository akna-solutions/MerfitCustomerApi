using System.ComponentModel.DataAnnotations;
using MerfitCustomerApi.Business.Common.Pagination;

namespace MerfitCustomerApi.Business.Dtos.Admin.Translations;

public class AdminTranslationDto
{
    public long Id { get; set; }
    public long LanguageId { get; set; }
    public string LanguageCode { get; set; } = string.Empty;
    public string ResourceKey { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

public class AdminTranslationListRequest : PagedRequest
{
    public long? LanguageId { get; set; }
    public string? ResourceKey { get; set; }
}

public class AdminUpsertTranslationRequest
{
    [Required(ErrorMessage = "LanguageId zorunludur.")]
    public long LanguageId { get; set; }

    [Required(ErrorMessage = "ResourceKey zorunludur.")]
    [StringLength(300, MinimumLength = 1)]
    public string ResourceKey { get; set; } = string.Empty;

    [Required(ErrorMessage = "Value zorunludur.")]
    [StringLength(4000)]
    public string Value { get; set; } = string.Empty;
}

/// <summary>
/// POST /api/admin/translations/import govdesi. TEK bir dil icin toplu ceviri
/// aktarimi yapar (orn. bir locale JSON dosyasindan). Var olan ResourceKey'ler guncellenir,
/// olmayanlar yeni olusturulur (upsert). Islem tek bir transaction icinde yapilir.
/// </summary>
public class AdminImportTranslationsRequest
{
    [Required(ErrorMessage = "LanguageId zorunludur.")]
    public long LanguageId { get; set; }

    [Required(ErrorMessage = "Items alani zorunludur.")]
    [MinLength(1, ErrorMessage = "En az bir ceviri girilmelidir.")]
    public List<AdminTranslationImportItem> Items { get; set; } = new();
}

public class AdminTranslationImportItem
{
    [Required(ErrorMessage = "ResourceKey zorunludur.")]
    [StringLength(300, MinimumLength = 1)]
    public string ResourceKey { get; set; } = string.Empty;

    [Required(ErrorMessage = "Value zorunludur.")]
    [StringLength(4000)]
    public string Value { get; set; } = string.Empty;
}

/// <summary>
/// PUT /api/admin/translations/bulk govdesi. Birden fazla dil/anahtar kombinasyonunu
/// tek istekte (upsert) gunceller; admin panelindeki ceviri grid'inin toplu kaydetme
/// senaryosu icin kullanilir. Islem tek bir transaction icinde yapilir.
/// </summary>
public class AdminBulkUpdateTranslationsRequest
{
    [Required(ErrorMessage = "Items alani zorunludur.")]
    [MinLength(1, ErrorMessage = "En az bir ceviri girilmelidir.")]
    public List<AdminUpsertTranslationRequest> Items { get; set; } = new();
}

public class AdminTranslationBulkResultDto
{
    public int CreatedCount { get; set; }
    public int UpdatedCount { get; set; }
}
