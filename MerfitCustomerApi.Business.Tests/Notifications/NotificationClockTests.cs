using MerfitCustomerApi.Business.Common;
using MerfitCustomerApi.Business.Services.Notifications.Engine;
using Xunit;

namespace MerfitCustomerApi.Business.Tests.Notifications;

public class NotificationClockTests
{
    [Theory]
    [InlineData("23:30", "22:00", "07:00", true)]
    [InlineData("06:59", "22:00", "07:00", true)]
    [InlineData("07:00", "22:00", "07:00", false)]
    [InlineData("12:00", "22:00", "07:00", false)]
    [InlineData("13:30", "13:00", "14:00", true)]
    [InlineData("14:00", "13:00", "14:00", false)]
    [InlineData("10:00", "10:00", "10:00", false)]
    public void IsWithinQuietHours_HandlesSameDayAndOvernightRanges(string now, string start, string end, bool expected)
    {
        Assert.Equal(expected, NotificationClock.IsWithinQuietHours(TimeOnly.Parse(now), TimeOnly.Parse(start), TimeOnly.Parse(end)));
    }

    [Fact]
    public void IsWithinQuietHours_NotConfigured_IsFalse()
    {
        Assert.False(NotificationClock.IsWithinQuietHours(new TimeOnly(23, 0), null, null));
    }

    [Fact]
    public void Resolve_UnknownOrMissingTimeZone_FallsBackToDefault()
    {
        var options = new NotificationOptions { DefaultTimeZone = "Europe/Istanbul" };

        Assert.Equal("Europe/Istanbul", NotificationClock.Resolve(null, options).Id);
        Assert.Equal("Europe/Istanbul", NotificationClock.Resolve("Mars/Olympus", options).Id);
        Assert.Equal("America/New_York", NotificationClock.Resolve("America/New_York", options).Id);
    }

    [Fact]
    public void QuietHoursEndUtc_Overnight_ReturnsNextMorningInUserZone()
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Istanbul");
        // 23:30 Istanbul = 20:30Z; sessiz saat 22:00-07:00 -> ertesi gun 07:00 Istanbul = 04:00Z.
        var end = NotificationClock.QuietHoursEndUtc(new DateTime(2026, 10, 14, 20, 30, 0, DateTimeKind.Utc), zone, new TimeOnly(22, 0), new TimeOnly(7, 0));

        Assert.Equal(new DateTime(2026, 10, 15, 4, 0, 0, DateTimeKind.Utc), end);
    }

    [Fact]
    public void ToUtc_RespectsDaylightSavingOfUserZone()
    {
        var newYork = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");

        Assert.Equal(new DateTime(2026, 7, 1, 13, 0, 0, DateTimeKind.Utc), NotificationClock.ToUtc(new DateOnly(2026, 7, 1), new TimeOnly(9, 0), newYork));
        Assert.Equal(new DateTime(2026, 12, 1, 14, 0, 0, DateTimeKind.Utc), NotificationClock.ToUtc(new DateOnly(2026, 12, 1), new TimeOnly(9, 0), newYork));
    }
}
