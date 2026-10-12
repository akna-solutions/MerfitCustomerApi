using MerfitCustomerApi.Business.Common;
using MerfitCustomerApi.Business.Interfaces.Services;
using Microsoft.Extensions.Options;

namespace MerfitCustomerApi.Api.BackgroundJobs;

/// <summary>
/// Bildirim motorunun zamanlayicisi. Is mantigi icermez; belirli araliklarla:
///  - INotificationRulePlanner.PlanDueRulesAsync (kural planlamasi),
///  - INotificationDispatchProcessor.ProcessDueAsync (gonderim kuyrugu),
///  - CompleteFinishedCampaignsAsync ve CheckReceiptsAsync
/// cagirir. Her adim kendi DI scope'unda calisir (PersonalizationBackgroundWorker ile ayni desen).
/// Tum durum veritabanindadir: uygulama yeniden baslasa da kaldigi yerden devam eder; birden fazla
/// instance ayni anda calisabilir (sahiplenme ve DedupeKey veritabaninda).
/// </summary>
public class NotificationBackgroundWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly NotificationOptions _options;
    private readonly ILogger<NotificationBackgroundWorker> _logger;

    public NotificationBackgroundWorker(IServiceScopeFactory scopeFactory, IOptions<NotificationOptions> options, ILogger<NotificationBackgroundWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.WorkerEnabled)
        {
            _logger.LogInformation("NotificationBackgroundWorker disabled (Notifications:WorkerEnabled=false).");
            return;
        }

        _logger.LogInformation("NotificationBackgroundWorker started.");
        var nextPlanning = DateTime.MinValue;
        var nextReceipts = DateTime.MinValue;

        try
        {
            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        }
        catch (TaskCanceledException)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            var now = DateTime.UtcNow;
            var processedAny = false;

            if (now >= nextPlanning)
            {
                await RunStepAsync("plan rules", sp => sp.GetRequiredService<INotificationRulePlanner>().PlanDueRulesAsync(DateTime.UtcNow, stoppingToken));
                await RunStepAsync("complete campaigns", sp => sp.GetRequiredService<INotificationDispatchProcessor>().CompleteFinishedCampaignsAsync(DateTime.UtcNow, stoppingToken));
                nextPlanning = now + _options.RulePlanningInterval;
            }

            processedAny = await RunStepAsync("dispatch", sp => sp.GetRequiredService<INotificationDispatchProcessor>().ProcessDueAsync(DateTime.UtcNow, stoppingToken)) > 0;

            if (now >= nextReceipts)
            {
                await RunStepAsync("receipts", sp => sp.GetRequiredService<INotificationDispatchProcessor>().CheckReceiptsAsync(DateTime.UtcNow, stoppingToken));
                nextReceipts = now + _options.ReceiptCheckInterval;
            }

            try
            {
                // Dolu bir kuyrukta beklemeden devam edilir; buyuk kitleler partiler halinde islenir.
                await Task.Delay(processedAny ? TimeSpan.FromMilliseconds(200) : _options.DispatchInterval, stoppingToken);
            }
            catch (TaskCanceledException)
            {
                break;
            }
        }

        _logger.LogInformation("NotificationBackgroundWorker stopped.");
    }

    private async Task<int> RunStepAsync(string name, Func<IServiceProvider, Task<int>> step)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            return await step(scope.ServiceProvider);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "NotificationBackgroundWorker step '{Step}' failed.", name);
            return 0;
        }
    }
}
