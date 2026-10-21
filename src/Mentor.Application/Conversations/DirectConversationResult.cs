namespace Mentor.Application.Conversations;

public sealed record DirectConversationResult(long Id, bool Added, string Day, string ContentKind, int MessageCount);
