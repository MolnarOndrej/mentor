using Mentor.Application.Configuration;

namespace Mentor.Infrastructure.Persistence.Configuration;

internal sealed class SettingsEntity
{
    public SettingsEntity()
    {
        var defaults = new RuntimeSettings();
        PollIntervalSeconds = defaults.PollIntervalSeconds;
        DailyTime = defaults.DailyTime;
    }

    public int Id
    {
        get; set;
    }
    public string Prompt { get; set; } = "";
    public int PollIntervalSeconds
    {
        get; set;
    }
    public string DailyTime
    {
        get; set;
    }
    public string? LastDailyDate
    {
        get; set;
    }
}
