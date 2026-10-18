namespace Mentor.Application.Conversations;

public record Conversation(long Id, string Url, string Title, string Status, string? Error,
    string CreatedAt, string? ImportedAt, List<Message> Messages);
