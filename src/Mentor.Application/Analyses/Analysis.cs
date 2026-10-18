namespace Mentor.Application.Analyses;

public record Analysis(long Id, string Status, string Trigger, string CreatedAt, string? CompletedAt,
    string Day, string Prompt, List<long> ConversationIds, List<string> MessageKeys, string? Result, string? Error);
