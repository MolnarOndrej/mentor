using System.Globalization;

namespace Mentor.Checks.TestDoubles;

internal static class LocalTime
{
    public static DateTimeOffset At(string value)
    {
        var local = DateTime.ParseExact(value, "yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture);
        return new DateTimeOffset(local, TimeZoneInfo.Local.GetUtcOffset(local));
    }
}
