namespace MerfitCustomerApi.Domain.Interfaces;

/// <summary>
/// Gonderim kuyrugunun veritabani seviyesinde atomik sahiplenilmesi. Birden fazla API instance'i ayni
/// anda calissa bile her gonderim tek bir calisana verilir (PostgreSQL FOR UPDATE SKIP LOCKED);
/// process-ici kilit kullanilmaz.
/// </summary>
public interface INotificationQueueStore
{
    /// <summary>
    /// Zamani gelmis Pending gonderimleri ve kilit suresi dolmus Processing gonderimleri (cokmus calisan)
    /// lockToken ile Processing durumuna alir ve sahiplenilen kimlikleri dondurur.
    /// </summary>
    Task<IReadOnlyList<long>> ClaimDueDispatchesAsync(DateTime nowUtc, int batchSize, TimeSpan lockDuration, Guid lockToken, CancellationToken cancellationToken = default);
}
