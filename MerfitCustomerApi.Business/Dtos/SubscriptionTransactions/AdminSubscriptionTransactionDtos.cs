using MerfitCustomerApi.Business.Common.Pagination;
using MerfitCustomerApi.Domain.Entities.Enums;

namespace MerfitCustomerApi.Business.Dtos.Admin.SubscriptionTransactions;

public class AdminSubscriptionTransactionListItemDto
{
    public long Id { get; set; }
    public long SubscriptionId { get; set; }
    public long UserId { get; set; }
    public string UserEmail { get; set; } = string.Empty;
    public string TransactionId { get; set; } = string.Empty;
    public string ProductId { get; set; } = string.Empty;
    public string Provider { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public DateTime PurchasedAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
}

/// <summary>
/// Detay yaniti "RawReceipt" alanini oldugu gibi DONMEZ; sadece kaydin olup olmadigini belirtir
/// (madde 39/18 - hassas verilerin yalnizca gerekli olduginda ve guvenli bicimde gosterilmesi).
/// </summary>
public class AdminSubscriptionTransactionDetailDto : AdminSubscriptionTransactionListItemDto
{
    public string? OriginalTransactionId { get; set; }
    public bool HasRawReceipt { get; set; }
}

public class AdminSubscriptionTransactionListRequest : PagedRequest
{
    public SubscriptionProvider? Provider { get; set; }
    public string? ProductId { get; set; }
    public long? UserId { get; set; }
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
}
