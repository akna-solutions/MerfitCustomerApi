namespace MerfitCustomerApi.Business.Dtos.Customer.Subscriptions;

/// <summary>
/// GET /api/subscription yaniti: kullanicinin guncel Plus uyeligi ve satin alinabilir planlar.
/// </summary>
public class MySubscriptionResponse
{
    /// <summary>Aktif (suresi dolmamis) bir Plus aboneligi varsa true.</summary>
    public bool IsPlus { get; set; }

    /// <summary>Kullanicinin en guncel abonelik kaydi; hic aboneligi yoksa null.</summary>
    public SubscriptionInfoDto? Current { get; set; }

    /// <summary>Aktif abonelik urunleri (aylik/yillik) ve icerdikleri ozellikler.</summary>
    public List<SubscriptionProductDto> Products { get; set; } = new();
}

public class SubscriptionInfoDto
{
    /// <summary>"Active" | "Expired" | "Cancelled" | "Refunded" | "Paused" | "GracePeriod".</summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>"Apple" | "Google".</summary>
    public string Provider { get; set; } = string.Empty;

    public DateTime StartedAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public bool AutoRenew { get; set; }
    public DateTime? CancelledAt { get; set; }
    public SubscriptionProductDto? Product { get; set; }
}

public class SubscriptionProductDto
{
    public long Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;

    /// <summary>"Monthly" | "Yearly".</summary>
    public string BillingPeriod { get; set; } = string.Empty;

    public decimal Price { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string? StoreProductIdIos { get; set; }
    public string? StoreProductIdAndroid { get; set; }
    public List<string> Features { get; set; } = new();
}
