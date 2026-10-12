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

    /// <summary>
    /// Kilo hedeflerinde (lose_weight/build_muscle/get_stronger/maintain_weight) baslangic kilosundan
    /// hedef kiloya kat edilen yol, 0-100. Antrenman hedeflerinde null.
    /// </summary>
    public decimal? GoalPercent { get; set; }

    /// <summary>"lose" | "gain" | "maintain": hedef kiloya ulasmak icin kilonun gitmesi gereken yon.</summary>
    public string WeightDirection { get; set; } = "maintain";

    /// <summary>improve_fitness/improve_endurance icin - bu haftaki tamamlanan antrenman sayisi.</summary>
    public int? WorkoutsCompleted { get; set; }

    /// <summary>improve_fitness/improve_endurance icin - haftalik antrenman hedefi (profilden).</summary>
    public int? WorkoutsGoal { get; set; }

    public decimal CurrentWeight { get; set; }
    public decimal StartingWeight { get; set; }
    /// <summary>Hedef kilo; kullanici girmediyse hedefe gore otomatik hesaplanir (bkz. TargetWeightCalculator).</summary>
    public decimal TargetWeight { get; set; }

    /// <summary>Bugun girilen kilo; bugun giris yapilmadiysa null.</summary>
    public decimal? TodayWeight { get; set; }

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

    /// <summary>Tum zamanlar MB FIT puani; puani olmayan kullanicida 0 puan ve null sira ile doner.</summary>
    public CustomerScoreSummaryDto? Score { get; set; }
}

public class CustomerWeightPointDto
{
    /// <summary>Gosterime hazir kisa etiket (orn. "Haz 15").</summary>
    public string Date { get; set; } = string.Empty;

    /// <summary>"yyyy-MM-dd".</summary>
    public string IsoDate { get; set; } = string.Empty;

    public decimal Weight { get; set; }
}

/// <summary>POST /api/progress/weight govdesi: gunluk kilo girisi (ayni gune ikinci giris oncekini gunceller).</summary>
public class LogWeightRequest
{
    [System.ComponentModel.DataAnnotations.Range(20, 400, ErrorMessage = "Kilo 20 ile 400 kg arasinda olmalidir.")]
    public decimal WeightKg { get; set; }

    /// <summary>"yyyy-MM-dd"; verilmezse bugun kabul edilir.</summary>
    public DateOnly? Date { get; set; }
}

public class CustomerWeightLogResponse
{
    public string Date { get; set; } = string.Empty;
    public decimal WeightKg { get; set; }
    public decimal? TargetWeight { get; set; }
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

    /// <summary>Tum zamanlar siralamasindaki yeri; hic puani yoksa null.</summary>
    public int? Rank { get; set; }
}
