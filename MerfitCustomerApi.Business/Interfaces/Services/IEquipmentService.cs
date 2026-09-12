using MerfitCustomerApi.Business.Dtos.Customer.Equipment;

namespace MerfitCustomerApi.Business.Interfaces.Services;

/// <summary>
/// Onboarding (EquipmentStep) ve antrenman ekipmani filtreleri icin kullanilan,
/// sisteme tanimli tum ekipmanlarin listesini donen servis sozlesmesi.
/// </summary>
public interface IEquipmentService
{
    Task<IReadOnlyList<CustomerEquipmentListItemDto>> GetAllAsync(CancellationToken cancellationToken = default);
}