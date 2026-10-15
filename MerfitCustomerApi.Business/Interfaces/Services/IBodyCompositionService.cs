using MerfitCustomerApi.Business.Dtos.Customer.BodyComposition;

namespace MerfitCustomerApi.Business.Interfaces.Services;

/// <summary>Vucut olcumu gecmisi ve vucut yag orani degerlendirmesi.</summary>
public interface IBodyCompositionService
{
    /// <summary>Olcum gecmisi (yeniden eskiye) ve guncel yag orani degerlendirmesi.</summary>
    /// <exception cref="Domain.Exceptions.NotFoundException">Kullanici profili bulunamazsa.</exception>
    Task<BodyMeasurementHistoryResponse> GetHistoryAsync(long userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Yeni bir olcum kaydi ekler (eski kayitlara dokunmaz). Cevre olcumleri yeterliyse tahmini yag orani
    /// kayitla birlikte saklanir. En yeni kilo ise profil/hedef kilosu guncellenir; vucut kompozisyonu
    /// degistiyse beslenme plani yeniden uretilmek uzere kuyruga alinir.
    /// </summary>
    Task<BodyMeasurementHistoryResponse> AddMeasurementAsync(long userId, CreateBodyMeasurementRequest request, CancellationToken cancellationToken = default);
}
