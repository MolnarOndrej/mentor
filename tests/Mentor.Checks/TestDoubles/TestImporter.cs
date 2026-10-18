using Mentor.Application.Conversations;
using Mentor.Infrastructure.Conversations;

namespace Mentor.Checks.TestDoubles;

internal sealed class TestImporter : IConversationImporter
{
    public string NormalizeUrl(string url) => ChatGptImporter.Normalize(url);
    public Task<ImportedConversation> ImportAsync(string url, CancellationToken cancellationToken) =>
        Task.FromResult(new ImportedConversation("Test conversation",
        [new("user", "SENSITIVE_CONVERSATION", 1), new("assistant", "Response", 2)]));
}
