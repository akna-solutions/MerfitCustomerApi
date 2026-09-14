namespace MerfitCustomerApi.Business.Interfaces.Services;

/// <summary>
/// Kuyruktaki (Pending) tek bir PersonalizationJob'u atomik olarak "claim" edip (Processing'e
/// gecirip) isleyen servis sozlesmesi. Arka plan calisani (PersonalizationBackgroundWorker) bu
/// servisi periyodik olarak, her seferinde YENI bir DI scope icinde cagirir (bkz. Infrastructure/
/// BackgroundJobs XML docs) - implementasyon bu yuzden scoped bir IUnitOfWork'e guvenebilir.
/// </summary>
public interface IPersonalizationJobProcessor
{
    /// <summary>
    /// Kuyrukta islenmeyi bekleyen (Pending, deneme siniri asilmamis) bir sonraki job'u bulur,
    /// concurrency-guvenli sekilde claim eder (bkz. implementasyon - PostgreSQL xmin optimistic
    /// concurrency), antrenman + beslenme programini uretip kaydeder ve job'u Completed/Failed/
    /// (retry icin) Pending olarak isaretler.
    /// </summary>
    /// <returns>
    /// Islenecek bir job bulunup denendiyse true (basarili/basarisiz farketmez); kuyrukta
    /// islenecek uygun bir job yoksa false doner.
    /// </returns>
    Task<bool> ProcessNextAsync(CancellationToken cancellationToken = default);
}
