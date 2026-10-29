using MerfitCustomerApi.Business.Dtos.Customer.Progress;
using MerfitCustomerApi.Business.Interfaces.Services;
using MerfitCustomerApi.Domain.Entities;
using MerfitCustomerApi.Domain.Entities.Enums;
using MerfitCustomerApi.Domain.Exceptions;
using MerfitCustomerApi.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace MerfitCustomerApi.Business.Services.Progress;

/// <summary>
/// Yaklasik iki gunde bir gosterilen kilo guncelleme hatirlatmasi. Ayri bir zamanlayici tablosu tutulmaz:
/// son kilo kaydi (BodyMeasurement) ve kullanicinin erteleme ani (UserPreference.WeightReminderSnoozedUntil)
/// birlikte degerlendirilir. Kilo kaydi mevcut POST /api/progress/weight ile yapilir (ayni gune ikinci giris
/// kaydi gunceller, mukerrer kayit olusmaz).
/// </summary>
public class WeightReminderService : IWeightReminderService
{
    /// <summary>Iki kilo kaydi arasinda beklenen gun sayisi.</summary>
    public const int IntervalDays = 2;

    /// <summary>"Sonra hatirlat" secildiginde tekrar sorulmadan once beklenen sure.</summary>
    public static readonly TimeSpan LaterDelay = TimeSpan.FromDays(1);

    private readonly IUnitOfWork _unitOfWork;
    private readonly TimeProvider _timeProvider;

    public WeightReminderService(IUnitOfWork unitOfWork, TimeProvider? timeProvider = null)
    {
        _unitOfWork = unitOfWork;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<CustomerWeightReminderResponse> GetAsync(long userId, CancellationToken cancellationToken = default)
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;

        var profile = await _unitOfWork.Repository<UserProfile>()
            .FirstOrDefaultAsync(p => p.UserId == userId, cancellationToken)
            ?? throw new NotFoundException(nameof(UserProfile), userId);
        var preference = await _unitOfWork.Repository<UserPreference>()
            .FirstOrDefaultAsync(p => p.UserId == userId, cancellationToken);

        var last = await _unitOfWork.Repository<BodyMeasurement>()
            .GetQueryable()
            .Where(m => m.UserId == userId && m.WeightKg != null)
            .OrderByDescending(m => m.RecordedAt)
            .Select(m => new { m.RecordedAt, m.WeightKg })
            .FirstOrDefaultAsync(cancellationToken);

        // Kilo kayitlari gun bazinda (RecordedAt = gunun UTC baslangici) tutulur; POST /api/progress/weight ile ayni gun tanimi.
        var today = DateOnly.FromDateTime(now);
        DateOnly? lastDate = last is null ? null : DateOnly.FromDateTime(last.RecordedAt);

        bool due;
        if (lastDate is not null)
        {
            due = today.DayNumber - lastDate.Value.DayNumber >= IntervalDays;
        }
        else
        {
            // Hic kilo kaydi yoksa (kayitta girilen kilo profilde durur) hesap acildiktan iki gun sonra sorulur.
            var user = await _unitOfWork.Repository<ApplicationUser>().GetByIdAsync(userId, cancellationToken);
            var since = user?.CreatedAt ?? profile.CreatedAt;
            due = today.DayNumber - DateOnly.FromDateTime(since).DayNumber >= IntervalDays;
        }

        var snoozedUntil = preference?.WeightReminderSnoozedUntil;
        var snoozed = snoozedUntil.HasValue && snoozedUntil.Value > now;

        return new CustomerWeightReminderResponse
        {
            ShouldPrompt = due && !snoozed,
            IntervalDays = IntervalDays,
            LastLoggedDate = lastDate?.ToString("yyyy-MM-dd"),
            LastWeightKg = last?.WeightKg ?? profile.WeightKg,
            SnoozedUntil = snoozed ? snoozedUntil : null,
            UnitSystem = preference?.UnitSystem == UnitSystem.Imperial ? "imperial" : "metric",
        };
    }

    public async Task<CustomerWeightReminderResponse> SnoozeAsync(long userId, SnoozeWeightReminderRequest request, CancellationToken cancellationToken = default)
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var until = request.Action == "dismiss" ? now.AddDays(IntervalDays) : now.Add(LaterDelay);

        var repository = _unitOfWork.Repository<UserPreference>();
        var preference = await repository.GetQueryable(asNoTracking: false)
            .FirstOrDefaultAsync(p => p.UserId == userId, cancellationToken);
        if (preference is null)
        {
            // Eski hesaplarda tercih satiri olmayabilir; varsayilanlarla olusturulur.
            preference = new UserPreference
            {
                UserId = userId,
                Language = "tr",
                Theme = Theme.System,
                UnitSystem = UnitSystem.Metric,
                CreatedAt = now,
            };
            await repository.AddAsync(preference, cancellationToken);
        }
        else
        {
            preference.UpdatedAt = now;
        }

        preference.WeightReminderSnoozedUntil = until;
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return await GetAsync(userId, cancellationToken);
    }
}
