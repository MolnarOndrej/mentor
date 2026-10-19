using Mentor.Application.Discussions;

namespace Mentor.Infrastructure.Persistence.Discussions;

internal sealed class DiscussionTurnEntity
{
    public long Id
    {
        get; set;
    }
    public long AnalysisId
    {
        get; set;
    }
    public int Position
    {
        get; set;
    }
    public string Question { get; set; } = "";
    public string? Answer
    {
        get; set;
    }
    public string Status { get; set; } = "queued";
    public string? Error
    {
        get; set;
    }
    public string CreatedAt { get; set; } = "";
    public string? CompletedAt
    {
        get; set;
    }

    public DiscussionTurn ToModel() => new(Id, AnalysisId, Position, Question, Answer, Status, Error, CreatedAt, CompletedAt);
}
