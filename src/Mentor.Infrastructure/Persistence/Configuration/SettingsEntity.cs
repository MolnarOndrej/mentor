using Mentor.Application.Configuration;

namespace Mentor.Infrastructure.Persistence.Configuration;

internal sealed class SettingsEntity
{
    public int Id
    {
        get; set;
    }
    public string Prompt { get; set; } = "";
    public int PollIntervalSeconds { get; set; } = new RuntimeSettings().PollIntervalSeconds;
    public string DailyTime { get; set; } = new RuntimeSettings().DailyTime;
    public string? LastDailyDate
    {
        get; set;
    }
}
