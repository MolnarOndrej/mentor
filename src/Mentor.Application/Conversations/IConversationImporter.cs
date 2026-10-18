namespace Mentor.Application.Conversations;

public interface IConversationImporter
{
    string NormalizeUrl(string url);
    Task<ImportedConversation> ImportAsync(string url, DateTimeOffset addedAt, CancellationToken cancellationToken);
}
