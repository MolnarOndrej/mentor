namespace Mentor.Infrastructure.Persistence.Configuration;

internal sealed class SettingsEntity
{
    public int Id
    {
        get; set;
    }
    public string Prompt { get; set; } = "";
    public string? LastDailyDate
    {
        get; set;
    }
}
