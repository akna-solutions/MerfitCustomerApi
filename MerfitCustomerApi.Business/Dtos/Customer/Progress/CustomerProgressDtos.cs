namespace MerfitCustomerApi.Business.Dtos.Customer.Progress;

/// <summary>
/// MerfitNativeApp ProgressScreen'in ihtiyac duydugu tum verileri tek cagrida saglayan response.
/// Sekil, mobil taraftaki ProgressData tipi ile birebir eslesecek sekilde tasarlandi.
/// </summary>
public class CustomerProgressResponse
{
    public bool HasCompletedFirstWorkout { get; set; }

    /// <summary>"lose_weight" | "build_muscle" | "get_stronger" | "improve_fitness" | "maintain_weight" | "improve_endurance".</summary>
    public string GoalType { get; set; } = string.Empty;
    public string GoalLabel { get; set; } = string.Empty;

    /// <summary>build_muscle/get_stronger/improve_endurance icin (UserGoal.ProgressPercent).</summary>
    public decimal? GoalPercent { get; set; }

    /// <summary>improve_fitness icin - bu haftaki tamamlanan antrenman sayisi.</summary>
    public int? WorkoutsCompleted { get; set; }

    /// <summary>improve_fitness icin - haftalik antrenman hedefi (profilden).</summary>
    public int? WorkoutsGoal { get; set; }

    public decimal CurrentWeight { get; set; }
    public decimal StartingWeight { get; set; }
    public decimal TargetWeight { get; set; }

    /// <summary>Son 30 gundeki kilo degisimi (negatifse kilo verilmis demektir).</summary>
    public decimal MonthlyChange { get; set; }

    /// <summary>Bu ay tamamlanan antrenman sayisi.</summary>
    public int Workouts { get; set; }

    /// <summary>Bu ay yakilan tahmini toplam kalori.</summary>
    public int Calories { get; set; }

    public int Streak { get; set; }

    /// <summary>Bu ay toplam antrenman suresi (dakika).</summary>
    public int TrainingMinutes { get; set; }

    /// <summary>Pazartesi..Pazar, uzunluk 7 - o gun tamamlanan bir antrenman var mi.</summary>
    public List<bool> WeeklyWorkouts { get; set; } = new();

    public List<CustomerWeightPointDto> WeightHistory { get; set; } = new();
    public List<CustomerBodyMetricDto> BodyMetrics { get; set; } = new();
    public List<CustomerRecentActivityDto> RecentActivity { get; set; } = new();

    /// <summary>MB FIT skoru; hic MerfitScore kaydi yoksa null (mobil taraf ScoreCard'i gizler).</summary>
    public CustomerScoreSummaryDto? Score { get; set; }
}

public class CustomerWeightPointDto
{
    /// <summary>Gosterime hazir kisa etiket (orn. "Haz 15").</summary>
    public string Date { get; set; } = string.Empty;
    public decimal Weight { get; set; }
}

public class CustomerBodyMetricDto
{
    public string Id { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}

public class CustomerRecentActivityDto
{
    public long Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public int DurationMin { get; set; }

    /// <summary>"Bugün" | "Dün" | "12 Ağu".</summary>
    public string DateLabel { get; set; } = string.Empty;
}

public class CustomerScoreSummaryDto
{
    public int Points { get; set; }
    public int WeeklyChange { get; set; }

    /// <summary>Aktif liderlik tablosu donemindeki sirasi; henuz hesaplanmamissa null.</summary>
    public int? Rank { get; set; }
}
