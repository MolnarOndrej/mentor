namespace Mentor.Infrastructure.Persistence.Analyses;

internal sealed class AnalysisEntity
{
    public long Id
    {
        get; set;
    }
    public string Status { get; set; } = "queued";
    public string Trigger { get; set; } = "";
    public string CreatedAt { get; set; } = "";
    public string? CompletedAt
    {
        get; set;
    }
    public string Day { get; set; } = "";
    public string Prompt { get; set; } = "";
    public string? FullPrompt
    {
        get; set;
    }
    public List<long> ConversationIds { get; set; } = [];
    public List<string> MessageKeys { get; set; } = [];
    public string? Result
    {
        get; set;
    }
    public string? Error
    {
        get; set;
    }

}
