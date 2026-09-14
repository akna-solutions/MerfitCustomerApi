using MerfitCustomerApi.Business.Dtos.Customer.Personalization;
using MerfitCustomerApi.Business.Interfaces.Services;
using MerfitCustomerApi.Domain.Entities;
using MerfitCustomerApi.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace MerfitCustomerApi.Business.Services.Personalization;

/// <summary>IPersonalizationStatusService'in varsayilan implementasyonu.</summary>
public class PersonalizationStatusService : IPersonalizationStatusService
{
    private readonly IUnitOfWork _unitOfWork;

    public PersonalizationStatusService(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<PersonalizationStatusDto> GetStatusAsync(long userId, CancellationToken cancellationToken = default)
    {
        // Bir kullanicinin birden fazla PersonalizationJob'u olabilir (orn. ileride "planimi
        // yenile" ozelligiyle); en gunceli (en son olusturulan) esas alinir.
        var job = await _unitOfWork.Repository<PersonalizationJob>()
            .GetQueryable()
            .Where(j => j.UserId == userId)
            .OrderByDescending(j => j.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (job is null)
        {
            return new PersonalizationStatusDto { HasJob = false };
        }

        return new PersonalizationStatusDto
        {
            HasJob = true,
            Status = job.Status.ToString(),
            AttemptCount = job.AttemptCount,
            StartedAt = job.StartedAt,
            CompletedAt = job.CompletedAt,
            ErrorMessage = job.ErrorMessage,
        };
    }
}
