using MerfitCustomerApi.Domain.Entities.Enums;
using MerfitCustomerApi.Domain.Common;

namespace MerfitCustomerApi.Domain.Entities;

/// <summary>
/// Bir kullanicinin kayit sirasinda kuyruga alinan kisisellestirme (workout + nutrition plan
/// uretimi) isini temsil eder. FAZ 1'de bu kayit yalnizca "Pending" olarak olusturulur; gercek
/// plan uretimi (WorkoutPlanGenerator / NutritionPlanGenerator) sonraki fazda bu kayda baglanacaktir.
/// </summary>
public class PersonalizationJob : BaseEntity
{
    /// <summary>Isin ait oldugu kullanicinin kimligi.</summary>
    public long UserId { get; set; }

    /// <summary>Isin guncel durumu.</summary>
    public PersonalizationJobStatus Status { get; set; } = PersonalizationJobStatus.Pending;

    /// <summary>Isin kac kez denendigi (arka plan islemcisi tekrar denemeleri icin).</summary>
    public int AttemptCount { get; set; }

    /// <summary>Is basarisiz oldugunda (Status = Failed) son hata mesaji.</summary>
    public string? ErrorMessage { get; set; }

    /// <summary>Isin islenmeye baslandigi tarih.</summary>
    public DateTime? StartedAt { get; set; }

    /// <summary>Isin tamamlandigi (basarili ya da basarisiz) tarih.</summary>
    public DateTime? CompletedAt { get; set; }
}
