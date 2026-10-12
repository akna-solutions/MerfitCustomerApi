using MerfitCustomerApi.Domain.Entities;
using MerfitCustomerApi.Domain.Entities.Enums;
using MerfitCustomerApi.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace MerfitCustomerApi.Business.Services.Personalization;

/// <summary>Profil/kilo degisikliklerinden sonra kisisel planlarin yeniden uretilmesini kuyruga alir.</summary>
public static class PlanRegenerationQueue
{
    /// <summary>
    /// Kayittaki ile ayni sekilde bir PersonalizationJob kuyruga alir; arka plan islemcisi yeni
    /// plan(lar)i uretip eski aktif planlari pasiflestirir. Bekleyen (Pending) bir is zaten varsa yenisi
    /// eklenmez, o is guncel profili okuyacaktir (gerekirse kapsami tam yenilemeye genisletilir). Islenmekte
    /// (Processing) olan is eski profili okumus olabilecegi icin yeni is eklenir; islemci ayni kullanicinin
    /// islerini sirayla isler.
    /// </summary>
    public static async Task EnqueueAsync(IUnitOfWork unitOfWork, long userId, bool nutritionOnly, CancellationToken cancellationToken)
    {
        var jobs = unitOfWork.Repository<PersonalizationJob>();
        var pendingJob = await jobs.GetQueryable(asNoTracking: false)
            .Where(j => j.UserId == userId && j.Status == PersonalizationJobStatus.Pending)
            .OrderBy(j => j.Id)
            .FirstOrDefaultAsync(cancellationToken);
        if (pendingJob is not null)
        {
            if (!nutritionOnly && pendingJob.NutritionOnly)
            {
                pendingJob.NutritionOnly = false;
                pendingJob.UpdatedAt = DateTime.UtcNow;
            }

            return;
        }

        await jobs.AddAsync(new PersonalizationJob
        {
            UserId = userId,
            Status = PersonalizationJobStatus.Pending,
            NutritionOnly = nutritionOnly,
            CreatedAt = DateTime.UtcNow,
        }, cancellationToken);
    }
}
