using MerfitCustomerApi.Business.Interfaces.Services;
using MerfitCustomerApi.Domain.Entities;
using MerfitCustomerApi.Domain.Entities.Enums;
using MerfitCustomerApi.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace MerfitCustomerApi.Business.Services.Notifications.Engine;

/// <summary>
/// Antrenman tamamlandiginda veya ogun kaydedildiginde o gune ait bekleyen (henuz sahiplenilmemis)
/// hatirlatmalari iptal eder. Sahiplenilmis olanlar zaten gonderimden once kosulu yeniden kontrol eder;
/// bu sinif yalnizca gereksiz isi ve gecikmeli bayat gonderimi onler. SaveChanges cagirmaz: cagiran
/// servisin kaydi ile ayni islemde yazilir.
/// </summary>
public class NotificationReminderCanceller : INotificationReminderCanceller
{
    private readonly IUnitOfWork _unitOfWork;

    public NotificationReminderCanceller(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task CancelWorkoutRemindersAsync(long userId, DateTime completedAtUtc, CancellationToken cancellationToken = default)
    {
        // Saat dilimi farklarina karsi tamamlanma aninin bir gun oncesi/sonrasi kapsanir; sonraki gunun
        // hatirlatmasi henuz planlanmamis olacagi icin etkilenmez.
        var from = DateOnly.FromDateTime(completedAtUtc).AddDays(-1);
        var to = DateOnly.FromDateTime(completedAtUtc).AddDays(1);
        await CancelAsync(d => d.UserId == userId
            && d.Source == NotificationDispatchSource.Rule
            && d.Type == NotificationType.WorkoutReminder
            && d.Status == NotificationDispatchStatus.Pending
            && d.LocalDate >= from && d.LocalDate <= to, cancellationToken);
    }

    public async Task CancelMealRemindersAsync(long userId, DateOnly mealDate, MealType mealType, CancellationToken cancellationToken = default)
    {
        var ruleIds = await _unitOfWork.Repository<NotificationRule>().GetQueryable()
            .Where(r => r.Kind == NotificationRuleKind.NutritionReminder && r.MealType == mealType)
            .Select(r => r.Id)
            .ToListAsync(cancellationToken);
        if (ruleIds.Count == 0)
        {
            return;
        }

        await CancelAsync(d => d.UserId == userId
            && d.Source == NotificationDispatchSource.Rule
            && d.NotificationRuleId.HasValue && ruleIds.Contains(d.NotificationRuleId.Value)
            && d.Status == NotificationDispatchStatus.Pending
            && d.LocalDate == mealDate, cancellationToken);
    }

    private async Task CancelAsync(System.Linq.Expressions.Expression<Func<NotificationDispatch, bool>> predicate, CancellationToken cancellationToken)
    {
        var pending = await _unitOfWork.Repository<NotificationDispatch>().GetQueryable(asNoTracking: false)
            .Where(predicate)
            .ToListAsync(cancellationToken);

        var now = DateTime.UtcNow;
        foreach (var dispatch in pending)
        {
            dispatch.Status = NotificationDispatchStatus.Cancelled;
            dispatch.StatusReason = NotificationDispatchProcessor.ReasonConditionMet;
            dispatch.ProcessedAtUtc = now;
            dispatch.UpdatedAt = now;
        }
    }
}
