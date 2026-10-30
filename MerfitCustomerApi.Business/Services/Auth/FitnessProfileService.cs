using MerfitCustomerApi.Business.Common;
using MerfitCustomerApi.Business.Dtos.Auth;
using MerfitCustomerApi.Business.Interfaces.Services;
using MerfitCustomerApi.Business.Services.Personalization;
using MerfitCustomerApi.Domain.Entities;
using MerfitCustomerApi.Domain.Exceptions;
using MerfitCustomerApi.Domain.Interfaces;
using Microsoft.Extensions.Options;

namespace MerfitCustomerApi.Business.Services.Auth;

/// <summary>IFitnessProfileService'in varsayilan implementasyonu (bkz. <see cref="FitnessOnboarding"/>).</summary>
public class FitnessProfileService : IFitnessProfileService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly INutritionCalculator _nutritionCalculator;
    private readonly BodyCompositionOptions _bodyCompositionOptions;

    public FitnessProfileService(
        IUnitOfWork unitOfWork,
        INutritionCalculator nutritionCalculator,
        IOptions<BodyCompositionOptions>? bodyCompositionOptions = null)
    {
        _unitOfWork = unitOfWork;
        _nutritionCalculator = nutritionCalculator;
        _bodyCompositionOptions = bodyCompositionOptions?.Value ?? new BodyCompositionOptions();
    }

    public async Task CompleteAsync(long userId, FitnessProfileRequest request, CancellationToken cancellationToken = default)
    {
        var profileRepo = _unitOfWork.Repository<UserProfile>();
        var profile = await profileRepo.FirstOrDefaultAsync(p => p.UserId == userId, cancellationToken)
            ?? throw new NotFoundException(nameof(UserProfile), userId);

        // Tamamlanmis profil bu uctan ezilmez; sonraki degisiklikler PUT /api/profile ile yapilir.
        if (FitnessOnboarding.IsCompleted(profile))
        {
            throw new ConflictException("Fitness profilin zaten tamamlanmış.");
        }

        var fitness = FitnessOnboarding.Prepare(request, _bodyCompositionOptions, requireWeight: true);

        fitness.ApplyTo(profile);
        profile.UpdatedAt = DateTime.UtcNow;
        profileRepo.Update(profile);

        await fitness.PersistAsync(_unitOfWork, _nutritionCalculator, profile, cancellationToken);
        await PlanRegenerationQueue.EnqueueAsync(_unitOfWork, userId, nutritionOnly: false, cancellationToken);

        // Tum degisiklikler tek SaveChanges ile (tek transaction'da) kaydedilir.
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
