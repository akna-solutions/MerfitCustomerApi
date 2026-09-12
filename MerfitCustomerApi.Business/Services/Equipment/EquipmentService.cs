using MerfitCustomerApi.Business.Dtos.Customer.Equipment;
using MerfitCustomerApi.Business.Interfaces.Services;
using MerfitCustomerApi.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;
// Namespace'imiz "...Services.Equipment" oldugundan entity ile isim cakismasini
// (CS0118 vb.) onlemek icin acikca bir alias kullaniyoruz.
using EquipmentEntity = MerfitCustomerApi.Domain.Entities.Equipment;

namespace MerfitCustomerApi.Business.Services.Equipment;

/// <summary>IEquipmentService'in varsayilan implementasyonu. Domain.Entities.Equipment entity'sini oldugu gibi kullanir.</summary>
public class EquipmentService : IEquipmentService
{
    private readonly IUnitOfWork _unitOfWork;

    public EquipmentService(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<IReadOnlyList<CustomerEquipmentListItemDto>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var equipment = await _unitOfWork.Repository<EquipmentEntity>()
            .GetQueryable()
            .OrderBy(e => e.Name)
            .Select(e => new CustomerEquipmentListItemDto
            {
                Id = e.Id,
                Name = e.Name,
                Slug = e.Slug,
            })
            .ToListAsync(cancellationToken);

        return equipment;
    }
}