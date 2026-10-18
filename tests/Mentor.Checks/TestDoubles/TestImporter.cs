using Mentor.Application.Conversations;
using Mentor.Infrastructure.Conversations;

namespace Mentor.Checks.TestDoubles;

internal sealed class TestImporter : IConversationImporter
{
    public HashSet<string> UnavailableUrls { get; } = [];
    public List<string> Calls { get; } = [];
    public ImportedConversation Data
    {
        get; set;
    } = new("Test conversation",
        [new("user", "SENSITIVE_CONVERSATION", 1), new("assistant", "Response", 2)]);
    public string NormalizeUrl(string url) => ChatGptImporter.Normalize(url);
    public Task<ImportedConversation> ImportAsync(string url, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Calls.Add(url);
        return UnavailableUrls.Contains(url)
            ? Task.FromException<ImportedConversation>(new HttpRequestException("Unavailable"))
            : Task.FromResult(Data);
    }
}
