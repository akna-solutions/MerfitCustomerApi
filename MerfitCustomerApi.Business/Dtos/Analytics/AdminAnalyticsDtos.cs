namespace MerfitCustomerApi.Business.Dtos.Admin.Analytics;

public class AdminAnalyticsUsersDto
{
    public long TotalUsers { get; set; }
    public long ActiveUsers { get; set; }
    public long NewUsersLast7Days { get; set; }
    public long NewUsersLast30Days { get; set; }

    /// <summary>Daily Active Users - son 24 saat icinde en az bir WorkoutSession baslatmis benzersiz kullanici sayisi.</summary>
    public long Dau { get; set; }

    /// <summary>Weekly Active Users - son 7 gunde en az bir WorkoutSession baslatmis benzersiz kullanici sayisi.</summary>
    public long Wau { get; set; }

    /// <summary>Monthly Active Users - son 30 gunde en az bir WorkoutSession baslatmis benzersiz kullanici sayisi.</summary>
    public long Mau { get; set; }
}

public class AdminAnalyticsWorkoutsDto
{
    public long TotalSessionsLast30Days { get; set; }
    public long CompletedSessionsLast30Days { get; set; }

    /// <summary>Tamamlanan / baslatilan oturum orani (yuzde).</summary>
    public double WorkoutCompletionRatePercent { get; set; }

    /// <summary>Son 30 gunde en az bir antrenman yapan kullanici basina ortalama tamamlanmis antrenman sayisi.</summary>
    public double AverageWorkoutsPerActiveUser { get; set; }

    public List<AdminAnalyticsTopItemDto> TopWorkouts { get; set; } = new();
}

public class AdminAnalyticsNutritionDto
{
    public long TotalMealsLast30Days { get; set; }
    public double AverageMealsPerUserLast30Days { get; set; }
    public double AverageCaloriesPerMeal { get; set; }
    public long UsersWithNutritionGoal { get; set; }
}

public class AdminAnalyticsSubscriptionsDto
{
    public long TotalSubscriptions { get; set; }
    public long ActiveSubscriptions { get; set; }

    /// <summary>Aktif abonelik / toplam kullanici (yuzde).</summary>
    public double PlusConversionRatePercent { get; set; }

    /// <summary>
    /// Son 30 gunde iptal edilen abonelik sayisinin, (aktif + son 30 gunde iptal edilen) toplamina
    /// orani (yuzde). Kohort bazli tam churn hesaplamasi icin donemsel aboneli sayisi anlik
    /// enstantane olarak tutulmadigindan bu basitlestirilmis bir yaklasimdir.
    /// </summary>
    public double ChurnRatePercent { get; set; }
}

public class AdminAnalyticsRevenueDto
{
    public decimal TotalRevenue { get; set; }
    public decimal RevenueLast7Days { get; set; }
    public decimal RevenueLast30Days { get; set; }
    public decimal AverageRevenuePerPayingUser { get; set; }
}

public class AdminAnalyticsRetentionDto
{
    /// <summary>
    /// Kayittan en az 7 gun sonra hala aktif olan (kayit tarihinden itibaren 7 gun icinde en az
    /// bir antrenman tamamlamis) kullanicilarin orani (yuzde). Basitlestirilmis "Day-7 retention" tanimidir.
    /// </summary>
    public double Retention7DayPercent { get; set; }

    /// <summary>Ayni mantik, 30 gunluk pencere ile (basitlestirilmis "Day-30 retention").</summary>
    public double Retention30DayPercent { get; set; }
}

public class AdminAnalyticsEngagementDto
{
    public long Dau { get; set; }
    public long Wau { get; set; }
    public long Mau { get; set; }

    /// <summary>DAU/MAU orani (yuzde) - "stickiness" olarak da bilinir.</summary>
    public double StickinessPercent { get; set; }

    public double AverageSessionsPerActiveUserLast30Days { get; set; }
}

public class AdminAnalyticsTopItemDto
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public long Count { get; set; }
}
