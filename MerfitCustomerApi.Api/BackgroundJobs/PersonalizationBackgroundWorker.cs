using MerfitCustomerApi.Business.Common;
using MerfitCustomerApi.Business.Interfaces.Services;
using Microsoft.Extensions.Options;

namespace MerfitCustomerApi.Api.BackgroundJobs;

/// <summary>
/// Kayit sirasinda olusturulan PersonalizationJob(Pending) kayitlarini arka planda isleyen
/// ASP.NET Core BackgroundService (hosted service). Kendisi hicbir is mantigi icermez; tek
/// sorumlulugu belirli araliklarla IPersonalizationJobProcessor.ProcessNextAsync'i cagirmaktir.
///
/// DbContext yasam suresi: bu sinif Singleton olarak calisir (BackgroundService varsayilani),
/// ancak IPersonalizationJobProcessor (ve onun kullandigi Scoped IUnitOfWork/AppDbContext) her
/// job denemesi icin IServiceScopeFactory ile AYRI bir DI scope'unda olusturulur ve iş bitince
/// (using bloğu ile) hemen dispose edilir. Boylece tek bir uzun omurlu DbContext instance'i
/// singleton worker boyunca paylasilmaz (memory leak / thread-safety sorunlarindan kacinilir).
///
/// Neden Controller icinde veya RegisterAsync icinde degil: bu is potansiyel olarak uzun surebilir
/// (antrenman + beslenme programi uretimi + coklu SaveChanges) ve register isteginin yanit
/// suresini etkilememesi gerekir (bkz. Faz 2 gereksinimi "Register'ın response süresini uzatma").
/// Fire-and-forget Task.Run yerine ASP.NET Core'un yonetilen BackgroundService/IHostedService
/// mekanizmasi kullanilir; bu sayede uygulama kapanirken StopAsync ile duzgun sekilde durdurulur.
/// </summary>
public class PersonalizationBackgroundWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly PersonalizationJobProcessingOptions _options;
    private readonly ILogger<PersonalizationBackgroundWorker> _logger;

    public PersonalizationBackgroundWorker(
        IServiceScopeFactory scopeFactory,
        IOptions<PersonalizationJobProcessingOptions> options,
        ILogger<PersonalizationBackgroundWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("PersonalizationBackgroundWorker started.");

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
                processedAny = await ProcessOneJobInNewScopeAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Beklenmeyen (jobun kendi hata yakalamasinin disinda kalan, orn. DB baglanti
                // sorunu) bir istisna worker'i tamamen durdurmamali; loglayip devam et.
                _logger.LogError(ex, "PersonalizationBackgroundWorker loop encountered an unexpected error.");
                processedAny = false;
            }

            var delay = processedAny ? _options.BackToBackDelay : _options.PollingInterval;

            try
            {
                await Task.Delay(delay, stoppingToken);
            }
            catch (TaskCanceledException)
            {
                break;
            }
        }

        _logger.LogInformation("PersonalizationBackgroundWorker stopped.");
    }

    /// <summary>
    /// Tek bir job denemesi icin yeni bir DI scope acar, IPersonalizationJobProcessor'u bu
    /// scope'tan cozer, tek bir job islemeyi dener ve scope'u (dolayisiyla scoped DbContext'i) dispose eder.
    /// </summary>
    private async Task<bool> ProcessOneJobInNewScopeAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var processor = scope.ServiceProvider.GetRequiredService<IPersonalizationJobProcessor>();
        return await processor.ProcessNextAsync(cancellationToken);
    }
}
