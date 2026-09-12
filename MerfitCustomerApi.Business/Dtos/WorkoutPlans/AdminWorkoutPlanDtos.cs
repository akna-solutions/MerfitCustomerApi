using System.ComponentModel.DataAnnotations;
using MerfitCustomerApi.Business.Common.Pagination;
using MerfitCustomerApi.Domain.Entities.Enums;

namespace MerfitCustomerApi.Business.Dtos.Admin.WorkoutPlans;

public class AdminWorkoutPlanListItemDto
{
    public long Id { get; set; }
    public long UserId { get; set; }
    public string UserEmail { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public FitnessGoal? Goal { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public bool IsActive { get; set; }
    public bool IsAiGenerated { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class AdminWorkoutPlanDetailDto : AdminWorkoutPlanListItemDto
{
    public string? Description { get; set; }
    public int DayCount { get; set; }
}

public class AdminWorkoutPlanListRequest : PagedRequest
{
    public long? UserId { get; set; }
    public bool? IsActive { get; set; }
    public bool? IsAiGenerated { get; set; }
    public FitnessGoal? Goal { get; set; }
}

public class AdminUpdateWorkoutPlanStatusRequest
{
    [Required]
    public bool IsActive { get; set; }
}

public class AdminWorkoutPlanDayItemDto
{
    public long WorkoutId { get; set; }
    public string WorkoutTitle { get; set; } = string.Empty;
    public int Order { get; set; }
}

/// <summary>PUT /api/admin/workout-plans/{id}/days govdesi; mevcut gun listesinin YERINI ALIR (replace-all).</summary>
public class AdminSetWorkoutPlanDaysRequest
{
    [Required]
    public List<AdminSetWorkoutPlanDayItem> Days { get; set; } = new();
}

public class AdminSetWorkoutPlanDayItem
{
    [Required(ErrorMessage = "WorkoutId zorunludur.")]
    public long WorkoutId { get; set; }

    [Range(0, 400)]
    public int Order { get; set; }
}
