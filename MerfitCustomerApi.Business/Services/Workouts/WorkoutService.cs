using MerfitCustomerApi.Business.Common.Pagination;
using MerfitCustomerApi.Business.Dtos.Customer.Workouts;
using MerfitCustomerApi.Business.Interfaces.Services;
using MerfitCustomerApi.Domain.Entities;
using MerfitCustomerApi.Domain.Entities.Enums;
using MerfitCustomerApi.Domain.Exceptions;
using MerfitCustomerApi.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;
using EquipmentEntity = MerfitCustomerApi.Domain.Entities.Equipment;

namespace MerfitCustomerApi.Business.Services.Workouts;

/// <summary>
/// IWorkoutService'in varsayilan implementasyonu. Mevcut Workout/WorkoutCategory/MuscleGroup/
/// Equipment/WorkoutExercise entity'lerini oldugu gibi kullanir, yeni entity eklemez.
/// </summary>
public class WorkoutService : IWorkoutService
{
    private readonly IUnitOfWork _unitOfWork;

    public WorkoutService(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<PagedResult<CustomerWorkoutListItemDto>> GetWorkoutsAsync(
        long userId,
        CustomerWorkoutListRequest request,
        CancellationToken cancellationToken = default)
    {
        var query = _unitOfWork.Repository<Workout>()
            .GetQueryable()
            .Where(w => w.IsActive);

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            // Business projesi Npgsql paketine referans vermiyor (EF.Functions.ILike icin gerekli);
            // bu yuzden buyuk/kucuk harf duyarsiz arama icin taraflari ToLower() ile normallestiriyoruz.
            var term = request.Search.Trim().ToLower();
            query = query.Where(w => w.Title.ToLower().Contains(term));
        }

        var difficulties = ParseDifficulties(request.Difficulty);
        if (difficulties.Count > 0)
        {
            query = query.Where(w => difficulties.Contains(w.Difficulty));
        }

        if (!string.IsNullOrWhiteSpace(request.CategorySlug))
        {
            var category = await _unitOfWork.Repository<WorkoutCategory>()
                .FirstOrDefaultAsync(c => c.Slug == request.CategorySlug, cancellationToken);
            query = category is null ? query.Where(_ => false) : query.Where(w => w.CategoryId == category.Id);
        }

        var muscleGroupSlugs = SplitCsv(request.MuscleGroup);
        if (muscleGroupSlugs.Count > 0)
        {
            var muscleGroupIds = await _unitOfWork.Repository<MuscleGroup>()
                .GetQueryable()
                .Where(m => muscleGroupSlugs.Contains(m.Slug))
                .Select(m => m.Id)
                .ToListAsync(cancellationToken);
            query = query.Where(w => w.MuscleGroupId != null && muscleGroupIds.Contains(w.MuscleGroupId.Value));
        }

        var equipmentSlugs = SplitCsv(request.Equipment);
        if (equipmentSlugs.Count > 0)
        {
            var equipmentIds = await _unitOfWork.Repository<EquipmentEntity>()
                .GetQueryable()
                .Where(e => equipmentSlugs.Contains(e.Slug))
                .Select(e => e.Id)
                .ToListAsync(cancellationToken);

            var workoutIdsWithEquipment = await _unitOfWork.Repository<WorkoutEquipment>()
                .GetQueryable()
                .Where(we => equipmentIds.Contains(we.EquipmentId))
                .Select(we => we.WorkoutId)
                .Distinct()
                .ToListAsync(cancellationToken);

            query = query.Where(w => workoutIdsWithEquipment.Contains(w.Id));
        }

        var durationRanges = SplitCsv(request.Duration);
        if (durationRanges.Count > 0)
        {
            query = query.Where(w =>
                (durationRanges.Contains("under20") && w.DurationMin < 20) ||
                (durationRanges.Contains("20to40") && w.DurationMin >= 20 && w.DurationMin <= 40) ||
                (durationRanges.Contains("40plus") && w.DurationMin > 40));
        }

        if (request.Personalized == true)
        {
            var profile = await _unitOfWork.Repository<UserProfile>()
                .FirstOrDefaultAsync(p => p.UserId == userId, cancellationToken);

            if (profile?.ExperienceLevel is not null)
            {
                var targetDifficulty = MapExperienceToDifficulty(profile.ExperienceLevel.Value);
                query = query.Where(w => w.Difficulty == targetDifficulty);
            }

            var ownedEquipmentIds = await _unitOfWork.Repository<UserEquipment>()
                .GetQueryable()
                .Where(ue => ue.UserId == userId)
                .Select(ue => ue.EquipmentId)
                .ToListAsync(cancellationToken);

            if (ownedEquipmentIds.Count > 0)
            {
                var workoutIdsMatchingEquipment = await _unitOfWork.Repository<WorkoutEquipment>()
                    .GetQueryable()
                    .Where(we => ownedEquipmentIds.Contains(we.EquipmentId))
                    .Select(we => we.WorkoutId)
                    .Distinct()
                    .ToListAsync(cancellationToken);

                query = query.Where(w => workoutIdsMatchingEquipment.Contains(w.Id));
            }
        }

        query = query.OrderByDescending(w => w.IsFeatured).ThenBy(w => w.Title);

        var paged = await query.ToPagedResultAsync(request.Page, request.PageSize, cancellationToken);

        var workoutIds = paged.Items.Select(w => w.Id).ToList();

        var categoryNames = await _unitOfWork.Repository<WorkoutCategory>()
            .GetQueryable()
            .ToDictionaryAsync(c => c.Id, c => c.Name, cancellationToken);

        var muscleGroupNames = await _unitOfWork.Repository<MuscleGroup>()
            .GetQueryable()
            .ToDictionaryAsync(m => m.Id, m => m.Name, cancellationToken);

        var equipmentByWorkout = await _unitOfWork.Repository<WorkoutEquipment>()
            .GetQueryable()
            .Where(we => workoutIds.Contains(we.WorkoutId))
            .Join(
                _unitOfWork.Repository<EquipmentEntity>().GetQueryable(),
                we => we.EquipmentId,
                e => e.Id,
                (we, e) => new { we.WorkoutId, e.Name })
            .ToListAsync(cancellationToken);

        var items = paged.Items.Select(w => MapToListItem(w, categoryNames, muscleGroupNames, equipmentByWorkout
            .Where(x => x.WorkoutId == w.Id).Select(x => x.Name).ToList())).ToList();

        return PagedResult<CustomerWorkoutListItemDto>.Create(items, paged.Page, paged.PageSize, paged.TotalCount);
    }

    public async Task<CustomerWorkoutDetailDto> GetWorkoutDetailAsync(long workoutId, CancellationToken cancellationToken = default)
    {
        var workout = await _unitOfWork.Repository<Workout>().GetByIdAsync(workoutId, cancellationToken);
        if (workout is null || !workout.IsActive)
        {
            throw new NotFoundException(nameof(Workout), workoutId);
        }

        var categoryNames = await _unitOfWork.Repository<WorkoutCategory>()
            .GetQueryable()
            .ToDictionaryAsync(c => c.Id, c => c.Name, cancellationToken);

        var muscleGroupNames = await _unitOfWork.Repository<MuscleGroup>()
            .GetQueryable()
            .ToDictionaryAsync(m => m.Id, m => m.Name, cancellationToken);

        var equipmentNames = await _unitOfWork.Repository<WorkoutEquipment>()
            .GetQueryable()
            .Where(we => we.WorkoutId == workoutId)
            .Join(
                _unitOfWork.Repository<EquipmentEntity>().GetQueryable(),
                we => we.EquipmentId,
                e => e.Id,
                (we, e) => e.Name)
            .ToListAsync(cancellationToken);

        var exercises = await _unitOfWork.Repository<WorkoutExercise>()
            .GetQueryable()
            .Where(we => we.WorkoutId == workoutId)
            .OrderBy(we => we.Order)
            .Join(
                _unitOfWork.Repository<Exercise>().GetQueryable(),
                we => we.ExerciseId,
                e => e.Id,
                (we, e) => new CustomerWorkoutExerciseDto
                {
                    ExerciseId = e.Id,
                    Name = e.Name,
                    Order = we.Order,
                    Sets = we.Sets,
                    Reps = we.Reps,
                    RestSeconds = we.RestSeconds,
                    DurationSeconds = we.DurationSeconds,
                    VideoUrl = e.VideoUrl,
                    ImageUrl = e.ImageUrl,
                })
            .ToListAsync(cancellationToken);

        var listItem = MapToListItem(workout, categoryNames, muscleGroupNames, equipmentNames);

        return new CustomerWorkoutDetailDto
        {
            Id = listItem.Id,
            Title = listItem.Title,
            Tagline = listItem.Tagline,
            DurationMin = listItem.DurationMin,
            Difficulty = listItem.Difficulty,
            Category = listItem.Category,
            MuscleGroup = listItem.MuscleGroup,
            Equipment = listItem.Equipment,
            ImageUrl = listItem.ImageUrl,
            Featured = listItem.Featured,
            Description = workout.Description,
            Exercises = exercises,
        };
    }

    private static CustomerWorkoutListItemDto MapToListItem(
        Workout workout,
        IReadOnlyDictionary<long, string> categoryNames,
        IReadOnlyDictionary<long, string> muscleGroupNames,
        List<string> equipmentNames)
    {
        return new CustomerWorkoutListItemDto
        {
            Id = workout.Id,
            Title = workout.Title,
            Tagline = workout.Tagline,
            DurationMin = workout.DurationMin,
            Difficulty = MapDifficulty(workout.Difficulty),
            Category = categoryNames.GetValueOrDefault(workout.CategoryId, string.Empty),
            MuscleGroup = workout.MuscleGroupId.HasValue
                ? muscleGroupNames.GetValueOrDefault(workout.MuscleGroupId.Value)
                : null,
            Equipment = equipmentNames,
            ImageUrl = workout.ImageUrl,
            Featured = workout.IsFeatured,
        };
    }

    /// <summary>Domain.DifficultyLevel enum'ini mobil taraftaki Turkce union degerlerine cevirir.</summary>
    internal static string MapDifficulty(DifficultyLevel difficulty) => difficulty switch
    {
        DifficultyLevel.Beginner => "Başlangıç",
        DifficultyLevel.Intermediate => "Orta",
        DifficultyLevel.Advanced => "İleri",
        _ => difficulty.ToString(),
    };

    private static DifficultyLevel MapExperienceToDifficulty(ExperienceLevel level) => level switch
    {
        ExperienceLevel.Beginner => DifficultyLevel.Beginner,
        ExperienceLevel.Intermediate => DifficultyLevel.Intermediate,
        ExperienceLevel.Advanced => DifficultyLevel.Advanced,
        _ => DifficultyLevel.Beginner,
    };

    private static List<DifficultyLevel> ParseDifficulties(string? csv)
    {
        var result = new List<DifficultyLevel>();
        foreach (var raw in SplitCsv(csv))
        {
            if (Enum.TryParse<DifficultyLevel>(raw, ignoreCase: true, out var parsed))
            {
                result.Add(parsed);
            }
        }

        return result;
    }

    private static List<string> SplitCsv(string? csv)
    {
        if (string.IsNullOrWhiteSpace(csv))
        {
            return new List<string>();
        }

        return csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
    }
}
