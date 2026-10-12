using MerfitCustomerApi.Business.Common;

namespace MerfitCustomerApi.Business.Services.Notifications.Engine;

/// <summary>Kullanici saat dilimi, yerel gun sinirlari ve sessiz saat hesaplari.</summary>
public static class NotificationClock
{
    /// <summary>Gecerli bir IANA (veya sistem) saat dilimi kimligi mi.</summary>
    public static bool IsValidTimeZone(string? timeZoneId) =>
        !string.IsNullOrWhiteSpace(timeZoneId) && TimeZoneInfo.TryFindSystemTimeZoneById(timeZoneId, out _);

    /// <summary>Kullanicinin saat dilimi; bos/gecersizse DefaultTimeZone, o da gecersizse UTC+3 (MB FIT puan gunu ile ayni).</summary>
    public static TimeZoneInfo Resolve(string? timeZoneId, NotificationOptions options)
    {
        if (!string.IsNullOrWhiteSpace(timeZoneId) && TimeZoneInfo.TryFindSystemTimeZoneById(timeZoneId, out var zone))
        {
            return zone;
        }

        if (TimeZoneInfo.TryFindSystemTimeZoneById(options.DefaultTimeZone, out var fallback))
        {
            return fallback;
        }

        return TimeZoneInfo.CreateCustomTimeZone("MBFit+03", TimeSpan.FromHours(3), "MBFit+03", "MBFit+03");
    }

    public static DateTime ToLocal(DateTime utc, TimeZoneInfo zone) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), zone);

    /// <summary>Yerel tarih + saatin UTC karsiligi (DST bosluklarinda bir sonraki gecerli ana kaydirilir).</summary>
    public static DateTime ToUtc(DateOnly date, TimeOnly time, TimeZoneInfo zone)
    {
        var local = DateTime.SpecifyKind(date.ToDateTime(time), DateTimeKind.Unspecified);
        while (zone.IsInvalidTime(local))
        {
            local = local.AddMinutes(30);
        }
        return TimeZoneInfo.ConvertTimeToUtc(local, zone);
    }

    public static (DateTime StartUtc, DateTime EndUtc) LocalDayBoundsUtc(DateOnly date, TimeZoneInfo zone) =>
        (ToUtc(date, TimeOnly.MinValue, zone), ToUtc(date.AddDays(1), TimeOnly.MinValue, zone));

    /// <summary>Yerel saat sessiz saat araliginda mi; baslangic &gt; bitis ise aralik gece yarisini asar. Baslangic == bitis: sessiz saat yok.</summary>
    public static bool IsWithinQuietHours(TimeOnly localTime, TimeOnly? start, TimeOnly? end)
    {
        if (start is null || end is null || start == end)
        {
            return false;
        }

        return start < end
            ? localTime >= start && localTime < end
            : localTime >= start || localTime < end;
    }

    /// <summary>Sessiz saatin bittigi UTC an (yalnizca sessiz saat icindeyken anlamlidir).</summary>
    public static DateTime QuietHoursEndUtc(DateTime nowUtc, TimeZoneInfo zone, TimeOnly start, TimeOnly end)
    {
        var local = ToLocal(nowUtc, zone);
        var today = DateOnly.FromDateTime(local);
        var nowTime = TimeOnly.FromDateTime(local);
        var endDate = start < end || nowTime < end ? today : today.AddDays(1);
        return ToUtc(endDate, end, zone);
    }

    public static bool IsDayEnabled(int daysOfWeekMask, DayOfWeek day) => (daysOfWeekMask & (1 << (int)day)) != 0;
}
