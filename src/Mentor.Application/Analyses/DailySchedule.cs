using System.Globalization;

namespace Mentor.Application.Analyses;

public static class DailySchedule
{
    public static readonly TimeZoneInfo Prague = TimeZoneInfo.FindSystemTimeZoneById("Europe/Prague");

    public static string Day(DateTimeOffset instant) =>
        TimeZoneInfo.ConvertTime(instant, Prague).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    public static string? DueDate(DateTimeOffset utc, string? lastDate)
    {
        var local = TimeZoneInfo.ConvertTime(utc, Prague);
        var date = Day(utc);
        return local.Hour >= 22 && lastDate != date ? date : null;
    }
}
