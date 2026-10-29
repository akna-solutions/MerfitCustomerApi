using System.ComponentModel.DataAnnotations;

namespace MerfitCustomerApi.Business.Dtos.Customer.Achievements;

/// <summary>GET /api/achievements - kullanicinin tum aktif basarilari ve henuz kutlanmamis yeni kazanimlari.</summary>
public class CustomerAchievementsResponse
{
    public int EarnedCount { get; set; }
    public int TotalCount { get; set; }

    /// <summary>Tum aktif basarilar (kazanilanlar once, sonra kazanilmayanlar).</summary>
    public List<CustomerAchievementItemDto> Achievements { get; set; } = new();

    /// <summary>Kazanilmis ama kullaniciya henuz kutlamasi gosterilmemis basarilar (en eskisi once).</summary>
    public List<CustomerAchievementItemDto> NewlyEarned { get; set; } = new();
}

public class CustomerAchievementItemDto
{
    /// <summary>Achievement.Id; POST /api/achievements/seen bu degeri bekler.</summary>
    public long Id { get; set; }

    /// <summary>Sistemsel kod (orn. FIRST_WORKOUT).</summary>
    public string Code { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    /// <summary>Admin panelinde girilen simge (emoji veya "streak" | "workouts" | "pr" | "weekly").</summary>
    public string Icon { get; set; } = string.Empty;
    public int Points { get; set; }
    public bool Earned { get; set; }
    public DateTime? EarnedAt { get; set; }
}

/// <summary>POST /api/achievements/seen - kutlamasi gosterilen basarilar.</summary>
public class MarkAchievementsSeenRequest
{
    [Required]
    [MinLength(1)]
    [MaxLength(100)]
    public List<long> AchievementIds { get; set; } = new();
}
