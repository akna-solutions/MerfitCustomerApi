using System.ComponentModel.DataAnnotations;
using MerfitCustomerApi.Business.Common.Pagination;
using MerfitCustomerApi.Domain.Entities.Enums;

namespace MerfitCustomerApi.Business.Dtos.Admin.SubscriptionProducts;

public class AdminSubscriptionProductDto
{
    public long Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? StoreProductIdIos { get; set; }
    public string? StoreProductIdAndroid { get; set; }
    public string BillingPeriod { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public string Currency { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

public class AdminSubscriptionProductListRequest : PagedRequest
{
    public bool? IsActive { get; set; }
    public BillingPeriod? BillingPeriod { get; set; }
}

public class AdminUpsertSubscriptionProductRequest
{
    [Required(ErrorMessage = "Kod alani zorunludur.")]
    [StringLength(100, MinimumLength = 2)]
    public string Code { get; set; } = string.Empty;

    [Required(ErrorMessage = "Isim alani zorunludur.")]
    [StringLength(200, MinimumLength = 2)]
    public string Name { get; set; } = string.Empty;

    [StringLength(200)]
    public string? StoreProductIdIos { get; set; }

    [StringLength(200)]
    public string? StoreProductIdAndroid { get; set; }

    [Required(ErrorMessage = "Faturalama periyodu zorunludur.")]
    public BillingPeriod BillingPeriod { get; set; }

    [Range(0, 100000, ErrorMessage = "Fiyat 0'dan buyuk olmalidir.")]
    public decimal Price { get; set; }

    [Required(ErrorMessage = "Para birimi zorunludur.")]
    [StringLength(3, MinimumLength = 3, ErrorMessage = "Para birimi 3 harfli ISO kodu olmalidir (orn. TRY, USD).")]
    public string Currency { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;
}

public class AdminUpdateSubscriptionProductStatusRequest
{
    [Required]
    public bool IsActive { get; set; }
}

public class AdminSubscriptionProductFeatureItemDto
{
    public long FeatureId { get; set; }
    public string FeatureCode { get; set; } = string.Empty;
    public string FeatureName { get; set; } = string.Empty;
}

/// <summary>PUT /api/admin/subscription-products/{id}/features govdesi; mevcut iliskinin YERINI ALIR (replace-all).</summary>
public class AdminSetSubscriptionProductFeaturesRequest
{
    [Required]
    public List<long> FeatureIds { get; set; } = new();
}
