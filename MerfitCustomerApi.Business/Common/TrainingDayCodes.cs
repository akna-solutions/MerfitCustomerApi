namespace MerfitCustomerApi.Business.Common;

/// <summary>
/// Antrenman gunlerinin API'de kullanilan kisa kodlari ("Mon".."Sun") ile DayOfWeek arasindaki
/// donusumler. Gunler her zaman Pazartesi'den baslayarak siralanir.
/// </summary>
public static class TrainingDayCodes
{
    public static readonly string[] All = { "Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun" };

    /// <summary>Pazartesi = 0 ... Pazar = 6.</summary>
    public static int MondayFirstIndex(DayOfWeek day) => ((int)day + 6) % 7;

    public static string ToCode(DayOfWeek day) => All[MondayFirstIndex(day)];

    public static bool TryParseCode(string? code, out DayOfWeek day)
    {
        var index = Array.FindIndex(All, c => string.Equals(c, code?.Trim(), StringComparison.OrdinalIgnoreCase));
        day = index < 0 ? default : (DayOfWeek)((index + 1) % 7);
        return index >= 0;
    }

    /// <summary>"Mon,Wed,Fri" gibi saklanan degeri tekrarsiz ve sirali gunlere cevirir; gecersiz kodlar atlanir.</summary>
    public static List<DayOfWeek> ParseStored(string? stored)
    {
        if (string.IsNullOrWhiteSpace(stored))
        {
            return new List<DayOfWeek>();
        }

        return stored.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(code => TryParseCode(code, out var day) ? (DayOfWeek?)day : null)
            .Where(day => day.HasValue)
            .Select(day => day!.Value)
            .Distinct()
            .OrderBy(MondayFirstIndex)
            .ToList();
    }

    public static string Format(IEnumerable<DayOfWeek> days) =>
        string.Join(",", days.Distinct().OrderBy(MondayFirstIndex).Select(ToCode));
}
