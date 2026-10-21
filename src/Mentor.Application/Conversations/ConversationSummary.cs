using Mentor.Application.Analyses;

namespace Mentor.Application.Conversations;

public record ConversationSummary(long Id, string? Url, string Title, string Status, string? Error,
    string CreatedAt, string? ImportedAt, int MessageCount, List<DayCoverage> Days,
    string IngestionKind = "share", string ContentKind = "transcript", string? Source = null, string? ExternalId = null);
