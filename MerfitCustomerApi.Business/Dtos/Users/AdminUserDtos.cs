using System.ComponentModel.DataAnnotations;
using MerfitCustomerApi.Business.Common.Pagination;
using MerfitCustomerApi.Domain.Entities.Enums;

namespace MerfitCustomerApi.Business.Dtos.Admin.Users;

/// <summary>GET /api/admin/users liste satiri. Hassas alanlar (PasswordHash vb.) kesinlikle icermez.</summary>
public class AdminUserListItemDto
{
    public long Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string Role { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public bool EmailConfirmed { get; set; }
    public bool IsDeleted { get; set; }
    public string? SubscriptionStatus { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? LastLoginAt { get; set; }
}

/// <summary>GET /api/admin/users sorgu parametreleri (madde 6 - desteklenen filtreler).</summary>
public class AdminUserListRequest : PagedRequest
{
    public string? Email { get; set; }
    public string? Username { get; set; }
    public bool? IsActive { get; set; }
    public bool? EmailConfirmed { get; set; }

    /// <summary>"active" | "expired" | "cancelled" | "none" (aboneligi hic olmayanlar).</summary>
    public string? SubscriptionStatus { get; set; }

    public DateTime? CreatedFrom { get; set; }
    public DateTime? CreatedTo { get; set; }
    public DateTime? LastLoginFrom { get; set; }
    public DateTime? LastLoginTo { get; set; }

    /// <summary>true ise soft-delete edilmis kullanicilar da listeye dahil edilir (varsayilan: false).</summary>
    public bool IncludeDeleted { get; set; }
}

/// <summary>
/// GET /api/admin/users/{userId} yaniti. Admin panelinde kullanicinin genel durumunu tek bakista
/// gorebilmek icin en cok kullanilan bilgileri bir araya getiren aggregate bir DTO'dur; detayli
/// alt kaynaklar (workoutlar, mealler vb.) ayri uc noktalardan (bkz. madde 6) alinir.
/// </summary>
public class AdminUserDetailDto
{
    public long Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public string? PhoneNumber { get; set; }
    public string Role { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public bool EmailConfirmed { get; set; }
    public bool PhoneNumberConfirmed { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? LastLoginAt { get; set; }

    public AdminUserProfileDto? Profile { get; set; }

    public string? CurrentSubscriptionStatus { get; set; }
    public string? CurrentSubscriptionProductName { get; set; }
    public DateTime? CurrentSubscriptionExpiresAt { get; set; }

    public decimal? LatestMerfitScore { get; set; }
    public int CurrentStreak { get; set; }
    public int LongestStreak { get; set; }
    public long TotalWorkoutSessions { get; set; }
    public long TotalAchievements { get; set; }
}

/// <summary>GET /api/admin/users/{userId}/profile yaniti.</summary>
public class AdminUserProfileDto
{
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Username { get; set; }
    public DateTime? DateOfBirth { get; set; }
    public Gender? Gender { get; set; }
    public decimal? HeightCm { get; set; }
    public decimal? WeightKg { get; set; }
    public decimal? TargetWeightKg { get; set; }
    public string? ProfileImageUrl { get; set; }
    public FitnessGoal? Goal { get; set; }
    public ExperienceLevel? ExperienceLevel { get; set; }
    public ActivityLevel? ActivityLevel { get; set; }
    public TrainingLocation? TrainingLocation { get; set; }
    public int? TrainingDaysPerWeek { get; set; }
    public UnitSystem UnitSystem { get; set; }
}

/// <summary>PATCH /api/admin/users/{userId}/status istegi.</summary>
public class AdminUpdateUserStatusRequest
{
    [Required(ErrorMessage = "isActive alani zorunludur.")]
    public bool IsActive { get; set; }

    /// <summary>Islemin sebebi (audit log'a yazilir, opsiyonel).</summary>
    [StringLength(500)]
    public string? Reason { get; set; }
}

public class AdminUserSubscriptionListItemDto
{
    public long Id { get; set; }
    public long SubscriptionProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string Provider { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime StartedAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public bool AutoRenew { get; set; }
    public DateTime? CancelledAt { get; set; }
}

public class AdminUserWorkoutSessionListItemDto
{
    public long Id { get; set; }
    public long WorkoutId { get; set; }
    public string WorkoutTitle { get; set; } = string.Empty;
    public DateTime StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public int? DurationSeconds { get; set; }
    public decimal? CaloriesBurned { get; set; }
}

public class AdminUserWorkoutPlanListItemDto
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public FitnessGoal? Goal { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public bool IsActive { get; set; }
    public bool IsAiGenerated { get; set; }
}

public class AdminUserMealListItemDto
{
    public long Id { get; set; }
    public DateTime Date { get; set; }
    public string? Notes { get; set; }
    public int ItemCount { get; set; }
    public decimal TotalCalories { get; set; }
}

public class AdminUserNutritionGoalDto
{
    public decimal DailyCalories { get; set; }
    public decimal ProteinTarget { get; set; }
    public decimal CarbsTarget { get; set; }
    public decimal FatTarget { get; set; }
    public decimal WaterTargetMl { get; set; }
}

public class AdminUserWaterLogListItemDto
{
    public long Id { get; set; }
    public DateTime Date { get; set; }
    public decimal AmountMl { get; set; }
}

public class AdminUserMeasurementListItemDto
{
    public long Id { get; set; }
    public DateTime RecordedAt { get; set; }
    public decimal? WeightKg { get; set; }
    public decimal? BodyFatPercentage { get; set; }
    public decimal? BMI { get; set; }
    public decimal? ChestCm { get; set; }
    public decimal? WaistCm { get; set; }
    public decimal? HipCm { get; set; }
    public decimal? ArmCm { get; set; }
    public decimal? ThighCm { get; set; }
}

public class AdminUserScoreDto
{
    public long? Id { get; set; }
    public decimal Score { get; set; }
    public string? Period { get; set; }
    public DateTime? CalculatedAt { get; set; }
}

public class AdminUserScoreHistoryListItemDto
{
    public long Id { get; set; }
    public decimal Score { get; set; }
    public DateTime RecordedAt { get; set; }
}

public class AdminUserScoreBreakdownItemDto
{
    public string Category { get; set; } = string.Empty;
    public decimal Points { get; set; }
}

public class AdminUserAchievementListItemDto
{
    public long AchievementId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public int Points { get; set; }
    public DateTime EarnedAt { get; set; }
}

public class AdminUserDeviceListItemDto
{
    public long Id { get; set; }
    public string DeviceToken { get; set; } = string.Empty;
    public Platform Platform { get; set; }
    public string? DeviceName { get; set; }
    public string? AppVersion { get; set; }
    public string? OsVersion { get; set; }
    public bool IsActive { get; set; }
    public DateTime? LastSeenAt { get; set; }
}

public class AdminUserConsentListItemDto
{
    public long Id { get; set; }
    public long DocumentId { get; set; }
    public string DocumentTitle { get; set; } = string.Empty;
    public bool Accepted { get; set; }
    public DateTime AcceptedAt { get; set; }
    public string Version { get; set; } = string.Empty;
    public string? IpAddress { get; set; }
}
