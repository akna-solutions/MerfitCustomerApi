using MerfitCustomerApi.Domain.Entities;
using MerfitCustomerApi.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace MerfitCustomerApi.Business.Services.WorkoutSessions;

/// <summary>
/// Oturum basliklarini toplu cozer. Katalog antrenmanindan baslatilan oturumda baslik Workout.Title'dir;
/// antrenman motorunun plan gununden baslatilan oturumda (WorkoutId bos) gunun basligi (WorkoutPlanDay.Title) kullanilir.
/// </summary>
public sealed class WorkoutSessionTitles
{
    private readonly IReadOnlyDictionary<long, string> _workoutTitles;
    private readonly IReadOnlyDictionary<long, string> _dayTitles;

    private WorkoutSessionTitles(IReadOnlyDictionary<long, string> workoutTitles, IReadOnlyDictionary<long, string> dayTitles)
    {
        _workoutTitles = workoutTitles;
        _dayTitles = dayTitles;
    }

    public static async Task<WorkoutSessionTitles> LoadAsync(
        IUnitOfWork unitOfWork,
        IEnumerable<(long? WorkoutId, long? WorkoutPlanDayId)> sessions,
        CancellationToken cancellationToken)
    {
        var list = sessions.ToList();
        var workoutIds = list.Where(s => s.WorkoutId.HasValue).Select(s => s.WorkoutId!.Value).Distinct().ToList();
        var dayIds = list.Where(s => !s.WorkoutId.HasValue && s.WorkoutPlanDayId.HasValue)
            .Select(s => s.WorkoutPlanDayId!.Value).Distinct().ToList();

        var workoutTitles = workoutIds.Count == 0
            ? new Dictionary<long, string>()
            : await unitOfWork.Repository<Workout>().GetQueryable()
                .Where(w => workoutIds.Contains(w.Id))
                .ToDictionaryAsync(w => w.Id, w => w.Title, cancellationToken);
        var dayTitles = dayIds.Count == 0
            ? new Dictionary<long, string>()
            : await unitOfWork.Repository<WorkoutPlanDay>().GetQueryable()
                .Where(d => dayIds.Contains(d.Id))
                .ToDictionaryAsync(d => d.Id, d => d.Title ?? string.Empty, cancellationToken);

        return new WorkoutSessionTitles(workoutTitles, dayTitles);
    }

    public string Resolve(long? workoutId, long? workoutPlanDayId)
    {
        if (workoutId is long wid && _workoutTitles.TryGetValue(wid, out var title))
        {
            return title;
        }

        return workoutPlanDayId is long did && _dayTitles.TryGetValue(did, out var dayTitle) ? dayTitle : string.Empty;
    }
}
