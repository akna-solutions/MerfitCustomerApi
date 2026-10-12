namespace MerfitCustomerApi.Business.Interfaces.Services;

/// <summary>Zamani gelen bildirim kurallari icin gonderim kayitlari olusturur.</summary>
public interface INotificationRulePlanner
{
    /// <summary>Olusturulan yeni gonderim sayisi.</summary>
    Task<int> PlanDueRulesAsync(DateTime nowUtc, CancellationToken cancellationToken = default);
}

/// <summary>Gonderim kuyrugunu isleyen tek gonderici.</summary>
public interface INotificationDispatchProcessor
{
    /// <summary>Sahiplenilip islenen gonderim sayisi.</summary>
    Task<int> ProcessDueAsync(DateTime nowUtc, CancellationToken cancellationToken = default);

    /// <summary>Saglayici makbuzlarini kontrol eder; gecersiz token'li cihazlari pasiflestirir.</summary>
    Task<int> CheckReceiptsAsync(DateTime nowUtc, CancellationToken cancellationToken = default);

    /// <summary>Tum alicilari sonuclanmis kampanyalari Completed yapar.</summary>
    Task<int> CompleteFinishedCampaignsAsync(DateTime nowUtc, CancellationToken cancellationToken = default);
}

/// <summary>Kullanici ilgili aksiyonu tamamladiginda bekleyen hatirlatmalari iptal eder.</summary>
public interface INotificationReminderCanceller
{
    Task CancelWorkoutRemindersAsync(long userId, DateTime completedAtUtc, CancellationToken cancellationToken = default);

    Task CancelMealRemindersAsync(long userId, DateOnly mealDate, Domain.Entities.Enums.MealType mealType, CancellationToken cancellationToken = default);
}
