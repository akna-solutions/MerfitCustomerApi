using MerfitCustomerApi.Business.Dtos.Customer.Progress;

namespace MerfitCustomerApi.Business.Interfaces.Services;

/// <summary>MerfitNativeApp ProgressScreen'in gerektirdigi tum ilerleme verilerini derleyen servis sozlesmesi.</summary>
public interface IProgressService
{
    /// <summary>
    /// Verilen zaman araligina (kilo grafigi icin) gore kullanicinin ilerleme ozetini dondurur.
    /// </summary>
    /// <param name="range">"week" | "month" | "3months" | "year".</param>
    /// <exception cref="Domain.Exceptions.NotFoundException">Kullanici profili bulunamazsa firlatilir.</exception>
    Task<CustomerProgressResponse> GetProgressAsync(long userId, string range, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gunluk kilo kaydi: ayni gune ikinci giris oncekini gunceller. En yeni kayitsa aktif hedefin guncel
    /// kilosunu, en az 1 kg fark varsa profil kilosunu gunceller ve beslenme planini yeniden uretir.
    /// </summary>
    Task<CustomerWeightLogResponse> LogWeightAsync(long userId, LogWeightRequest request, CancellationToken cancellationToken = default);
}
