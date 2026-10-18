using System.Globalization;

namespace Mentor.Application.Analyses;

public static class DailySchedule
{
    public static readonly TimeZoneInfo Prague = TimeZoneInfo.FindSystemTimeZoneById("Europe/Prague");

    public static string Day(DateTimeOffset instant) =>
        TimeZoneInfo.ConvertTime(instant, Prague).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    public static string? DueDate(DateTimeOffset utc, string? lastDate, string dailyTime)
    {
        var local = TimeZoneInfo.ConvertTime(utc, Prague);
        var date = Day(utc);
        var scheduled = TimeOnly.ParseExact(dailyTime, "HH:mm", CultureInfo.InvariantCulture);
        return TimeOnly.FromDateTime(local.DateTime) >= scheduled && lastDate != date ? date : null;
    }
}
