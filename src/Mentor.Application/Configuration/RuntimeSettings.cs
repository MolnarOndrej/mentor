using System.Globalization;

namespace Mentor.Application.Configuration;

public record RuntimeSettings(int PollIntervalSeconds = 5, string DailyTime = "22:00")
{
    public void Validate()
    {
        if (PollIntervalSeconds is < 1 or > 3600)
            throw new ArgumentException("Interval pollování musí být celé číslo od 1 do 3600 sekund.");
        if (!TimeOnly.TryParseExact(DailyTime, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
            throw new ArgumentException("Čas automatického rozboru musí být ve formátu HH:mm.");
    }
}
