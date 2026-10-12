using MerfitCustomerApi.Business.Interfaces.Services;
using MerfitCustomerApi.Domain.Interfaces;

namespace MerfitCustomerApi.Business.Services.Notifications.Engine;

/// <summary>Hatirlatma iptalini ana islemi bozmadan calistirir (MerfitScoreService.TryRefreshUserAsync ile ayni yaklasim).</summary>
public static class NotificationReminderCancellation
{
    public static async Task TryRunAsync(IUnitOfWork unitOfWork, INotificationReminderCanceller? canceller, Func<INotificationReminderCanceller, Task> action, CancellationToken cancellationToken)
    {
        if (canceller is null)
        {
            return;
        }

        try
        {
            await action(canceller);
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Bildirim iptali basarisiz olsa da gonderici kosulu yeniden kontrol ettigi icin hatirlatma gitmez.
            unitOfWork.ClearTracking();
        }
    }
}
