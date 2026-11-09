using MerfitCustomerApi.Business.Common;
using MerfitCustomerApi.Business.Interfaces.Services;
using Microsoft.Extensions.Options;

namespace MerfitCustomerApi.Api.BackgroundJobs;

/// <summary>
/// Admin panelinden (MerfitApi) acilan Antrenman Laboratuvari simulasyonlarini isleyen arka plan calisani.
/// Yalnizca IWorkoutPlanSimulationProcessor'u periyodik olarak cagirir; her deneme kendi DI scope'unda calisir
/// (bkz. PersonalizationBackgroundWorker). Kullaniciya yonelik hicbir is (plan, bildirim) tetiklemez.
/// </summary>
public class WorkoutPlanSimulationBackgroundWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly WorkoutPlanLabOptions _options;
    private readonly ILogger<WorkoutPlanSimulationBackgroundWorker> _logger;

    public WorkoutPlanSimulationBackgroundWorker(
        IServiceScopeFactory scopeFactory,
        IOptions<WorkoutPlanLabOptions> options,
        ILogger<WorkoutPlanSimulationBackgroundWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("WorkoutPlanSimulationBackgroundWorker disabled (WorkoutPlanLab:Enabled = false).");
            return;
        }

        try
        {
            await Task.Delay(_options.StartupDelay, stoppingToken);
        }
        catch (TaskCanceledException)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            bool processedAny;
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var processor = scope.ServiceProvider.GetRequiredService<IWorkoutPlanSimulationProcessor>();
                processedAny = await processor.ProcessNextAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "WorkoutPlanSimulationBackgroundWorker loop encountered an unexpected error.");
                processedAny = false;
            }

            try
            {
                await Task.Delay(processedAny ? TimeSpan.Zero : _options.PollingInterval, stoppingToken);
            }
            catch (TaskCanceledException)
            {
                break;
            }
        }
    }
}
