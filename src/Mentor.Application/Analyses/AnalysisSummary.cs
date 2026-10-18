namespace Mentor.Application.Analyses;

public record AnalysisSummary(long Id, string Status, string Trigger, string CreatedAt, string? CompletedAt,
    string Day, List<long> ConversationIds, int MessageCount, string? Error);
