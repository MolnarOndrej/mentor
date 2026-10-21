namespace Mentor.Application.Conversations;

public record Conversation(long Id, string? Url, string Title, string Status, string? Error,
    string CreatedAt, string? ImportedAt, List<Message> Messages, string IngestionKind = "share",
    string ContentKind = "transcript", string? Source = null, string? ExternalId = null);
