using System.ComponentModel.DataAnnotations;

namespace MerfitCustomerApi.Business.Dtos.Customer.Activity;

/// <summary>POST /api/activity/steps/sync govdesi.</summary>
public class StepSyncRequest
{
    /// <summary>Gonderilen gunler (en fazla StepActivity:MaxDaysPerSync).</summary>
    [Required]
    public List<StepSyncDayRequest> Days { get; set; } = new();

    /// <summary>Verinin kaynagi: pedometer, healthkit, health_connect.</summary>
    [Required]
    [StringLength(30)]
    public string Source { get; set; } = "pedometer";

    /// <summary>ios / android.</summary>
    [StringLength(20)]
    public string? Platform { get; set; }
}

/// <summary>
/// Bir yerel gunun saatlik adim toplamlari. Sensorun kumulatif degeri DEGIL, o gun o saatte atilan adim
/// sayisidir. Ayni gun tekrar gonderilirse sunucu saat bazinda buyuk olani tutar (idempotent).
/// </summary>
public class StepSyncDayRequest
{
    /// <summary>Cihazin yerel saatine gore gun.</summary>
    [Required]
    public DateOnly? Date { get; set; }

    /// <summary>Yerel saat 00..23 icin adimlar (24 eleman).</summary>
    [Required]
    public int[] HourlySteps { get; set; } = Array.Empty<int>();

    /// <summary>Cihazin o anki UTC farki (dakika, orn. Turkiye icin 180).</summary>
    public int UtcOffsetMinutes { get; set; }
}

/// <summary>PUT /api/activity/steps/goal govdesi.</summary>
public class UpdateStepGoalRequest
{
    public int DailyGoal { get; set; }

    /// <summary>Istemcinin yerel "bugun"u; verilirse o gunun kaydindaki hedef de guncellenir (yuzde aninda degisir).</summary>
    public DateOnly? Date { get; set; }
}

/// <summary>Adim hedefi ve istemcinin yerel (henuz senkronize edilmemis) adimlar icin kullanacagi katsayilar.</summary>
public class StepSettingsDto
{
    public int DailyGoal { get; set; }

    public int DefaultDailyGoal { get; set; }

    public int MinDailyGoal { get; set; }

    public int MaxDailyGoal { get; set; }

    /// <summary>Profil boyundan tahmini adim uzunlugu (metre).</summary>
    public double StrideLengthMeters { get; set; }

    /// <summary>Profil kilosundan adim basina tahmini kalori (kcal).</summary>
    public double CaloriesPerStep { get; set; }
}

public class StepDayDto
{
    public DateOnly Date { get; set; }

    public int TotalSteps { get; set; }

    public int DistanceMeters { get; set; }

    public int CaloriesBurned { get; set; }

    public int DailyGoal { get; set; }

    public bool GoalCompleted { get; set; }

    /// <summary>Hedefin yuzde kaci (0-100+ , bir ondalik).</summary>
    public double CompletionPercent { get; set; }

    /// <summary>Gun icin kayit var mi (senkronize edilmis mi).</summary>
    public bool HasData { get; set; }

    /// <summary>Gun makullik kontrollerinden gecti mi (false ise basari/puan hesabina katilmaz).</summary>
    public bool IsVerified { get; set; }

    /// <summary>Yalnizca tek gun detayinda doldurulur (24 eleman).</summary>
    public int[]? HourlySteps { get; set; }
}

public class StepSyncDayResultDto : StepDayDto
{
    /// <summary>accepted (yeni/artan veri), unchanged (ayni veri tekrar geldi), rejected (tarih penceresi disi).</summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>rejected icin sebep: future_date, too_old.</summary>
    public string? Reason { get; set; }
}

public class StepSyncResponse
{
    public List<StepSyncDayResultDto> Days { get; set; } = new();

    public StepSettingsDto Settings { get; set; } = new();
}

public class StepRangeResponse
{
    public DateOnly From { get; set; }

    public DateOnly To { get; set; }

    public List<StepDayDto> Days { get; set; } = new();
}

/// <summary>Son 7 gun (EndDate dahil) ozeti ve onceki 7 gunle karsilastirma.</summary>
public class StepWeeklySummaryDto
{
    public DateOnly StartDate { get; set; }

    public DateOnly EndDate { get; set; }

    public List<StepDayDto> Days { get; set; } = new();

    public int TotalSteps { get; set; }

    /// <summary>7 gunluk ortalama (kayit olmayan gunler 0 sayilir).</summary>
    public int AverageSteps { get; set; }

    public int TotalDistanceMeters { get; set; }

    public int TotalCaloriesBurned { get; set; }

    /// <summary>Hedefin tamamlandigi gun sayisi.</summary>
    public int GoalCompletedDays { get; set; }

    /// <summary>GoalCompletedDays / 7 (yuzde, bir ondalik).</summary>
    public double GoalCompletionRate { get; set; }

    /// <summary>En cok adim atilan gun; hic adim yoksa null.</summary>
    public StepDayDto? MostActiveDay { get; set; }

    /// <summary>Onceki 7 gunun toplami.</summary>
    public int PreviousPeriodTotalSteps { get; set; }

    /// <summary>Onceki 7 gune gore degisim yuzdesi; onceki donem 0 ise null.</summary>
    public double? ChangePercent { get; set; }
}
