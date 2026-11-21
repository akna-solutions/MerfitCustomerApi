using MerfitCustomerApi.Domain.Common;
using MerfitCustomerApi.Domain.Entities.Enums;

namespace MerfitCustomerApi.Domain.Entities;

/// <summary>Uygunsuz yorum raporu (store.ProductReviewReport); bir kullanici bir yorumu yalnizca bir kez raporlar.</summary>
public class ProductReviewReport : BaseEntity
{
    public long ReviewId { get; set; }

    public long ReporterUserId { get; set; }

    public ProductReviewReportReason Reason { get; set; }

    public string? Description { get; set; }

    public ProductReviewReportStatus Status { get; set; } = ProductReviewReportStatus.Open;

    public DateTime? ResolvedAt { get; set; }

    public long? ResolvedByUserId { get; set; }
}
