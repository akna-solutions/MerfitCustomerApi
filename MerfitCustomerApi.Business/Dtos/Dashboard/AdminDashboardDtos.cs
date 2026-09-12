namespace MerfitCustomerApi.Business.Dtos.Admin.Dashboard;

/// <summary>GET /api/admin/dashboard/summary yaniti.</summary>
public class AdminDashboardSummaryDto
{
    public long TotalUsers { get; set; }
    public long ActiveUsers { get; set; }
    public long NewUsersToday { get; set; }
    public long NewUsersThisMonth { get; set; }
    public long PlusSubscribers { get; set; }
    public long ActiveSubscriptions { get; set; }
    public decimal RevenueThisMonth { get; set; }
    public decimal RevenueTotal { get; set; }
    public long WorkoutsCompleted { get; set; }
    public long OpenSupportTickets { get; set; }
    public long AiRequestCount { get; set; }
    public long ActiveWorkouts { get; set; }
    public long ActiveExercises { get; set; }
}

/// <summary>Tarih bazli tek bir seri noktasi (kullanici artisi, gelir, aktivite grafikleri icin ortak).</summary>
public class AdminDashboardTimeSeriesPointDto
{
    public DateOnly Date { get; set; }
    public long Count { get; set; }
    public decimal Amount { get; set; }
}

/// <summary>GET /api/admin/dashboard/user-growth yaniti.</summary>
public class AdminUserGrowthDto
{
    public DateTime From { get; set; }
    public DateTime To { get; set; }
    public List<AdminDashboardTimeSeriesPointDto> Points { get; set; } = new();
}

/// <summary>GET /api/admin/dashboard/revenue yaniti.</summary>
public class AdminRevenueDto
{
    public DateTime From { get; set; }
    public DateTime To { get; set; }
    public decimal TotalRevenue { get; set; }
    public List<AdminDashboardTimeSeriesPointDto> Points { get; set; } = new();
}

/// <summary>GET /api/admin/dashboard/subscriptions yaniti.</summary>
public class AdminDashboardSubscriptionsDto
{
    public long ActiveCount { get; set; }
    public long ExpiredCount { get; set; }
    public long CancelledCount { get; set; }
    public long GracePeriodCount { get; set; }
    public List<AdminSubscriptionProductBreakdownDto> ByProduct { get; set; } = new();
}

public class AdminSubscriptionProductBreakdownDto
{
    public long SubscriptionProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public long ActiveCount { get; set; }
}

/// <summary>GET /api/admin/dashboard/activity yaniti.</summary>
public class AdminDashboardActivityDto
{
    public DateTime From { get; set; }
    public DateTime To { get; set; }
    public List<AdminDashboardTimeSeriesPointDto> WorkoutsCompleted { get; set; } = new();
    public List<AdminDashboardTimeSeriesPointDto> NewUsers { get; set; } = new();
}

/// <summary>Dashboard zaman serisi uc noktalari icin ortak tarih araligi parametresi.</summary>
public class AdminDashboardDateRangeRequest
{
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
}
