namespace Mentor.Application.Discussions;

public record DiscussionTurn(long Id, long AnalysisId, int Position, string Question, string? Answer,
    string Status, string? Error, string CreatedAt, string? CompletedAt);
