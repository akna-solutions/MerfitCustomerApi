namespace MerfitCustomerApi.Business.Dtos.Customer.Dashboard;

/// <summary>
/// MerfitNativeApp DashboardScreen'in ihtiyac duydugu tum verileri tek cagrida saglayan response.
/// Sekil (shape), mobil taraftaki DashboardData tipi ile birebir eslesecek sekilde tasarlandi.
/// </summary>
public class CustomerDashboardResponse
{
    public string UserName { get; set; } = string.Empty;

    /// <summary>Kullanicinin daha once en az bir antrenman tamamlayip tamamlamadigi (empty-state icin).</summary>
    public bool HasCompletedFirstWorkout { get; set; }

    public CustomerTodayProgressDto TodayProgress { get; set; } = new();

    /// <summary>Bugun icin onerilen/planlanan antrenman; hicbiri yoksa null (mobil taraf EmptyState gosterir).</summary>
    public CustomerDashboardWorkoutDto? TodayWorkout { get; set; }

    public List<CustomerQuickStatDto> QuickStats { get; set; } = new();

    public CustomerGoalProgressDto? GoalProgress { get; set; }

    public List<CustomerDashboardWorkoutDto> Recommended { get; set; } = new();
}

public class CustomerTodayProgressDto
{
    /// <summary>Bu hafta tamamlanan antrenman sayisi.</summary>
    public int WorkoutsCompleted { get; set; }

    /// <summary>Kullanicinin profilindeki haftalik antrenman hedefi (varsayilan 3).</summary>
    public int WorkoutsTarget { get; set; }

    /// <summary>Bugun tamamlanan oturumlardan yakilan toplam tahmini kalori.</summary>
    public int Calories { get; set; }
}

/// <summary>
/// Dashboard'daki "bugunun antrenmani" ve "onerilenler" kartlari icin ortak, hafif (summary) DTO.
/// </summary>
public class CustomerDashboardWorkoutDto
{
    public long Id { get; set; }
    public string Title { get; set; } = string.Empty;

    /// <summary>Ekranda gosterime hazir, birim etiketli sure (orn. "45 dk").</summary>
    public string Duration { get; set; } = string.Empty;

    /// <summary>Ekranda gosterime hazir kisa aciklama (orn. "6 egzersiz").</summary>
    public string Meta { get; set; } = string.Empty;

    /// <summary>"Baslangic" | "Orta" | "Ileri" (mobil taraftaki Difficulty union'i ile birebir).</summary>
    public string Difficulty { get; set; } = string.Empty;

    public string? ImageUrl { get; set; }
}

public class CustomerQuickStatDto
{
    /// <summary>"weight" | "streak" | "calories" | "workouts" (mobil taraftaki QuickStat.icon union'i ile birebir).</summary>
    public string Id { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;

    /// <summary>"scale" | "flame" | "trophy" | "barbell".</summary>
    public string Icon { get; set; } = string.Empty;
}

public class CustomerGoalProgressDto
{
    public decimal CurrentWeightKg { get; set; }
    public decimal GoalWeightKg { get; set; }
    public decimal StartWeightKg { get; set; }
}
