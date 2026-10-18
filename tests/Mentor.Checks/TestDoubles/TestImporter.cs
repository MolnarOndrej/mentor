using Mentor.Application.Conversations;
using Mentor.Infrastructure.Conversations;

namespace Mentor.Checks.TestDoubles;

internal sealed class TestImporter(TimeProvider clock) : IConversationImporter
{
    public string NormalizeUrl(string url) => ChatGptImporter.Normalize(url);
    public Task<ImportedConversation> ImportAsync(string url, DateTimeOffset addedAt, CancellationToken cancellationToken) =>
        Task.FromResult(new ImportedConversation("Test conversation",
        [new("user", "SENSITIVE_CONVERSATION", clock.GetUtcNow(), 1), new("assistant", "Response", clock.GetUtcNow(), 2)]));
}
