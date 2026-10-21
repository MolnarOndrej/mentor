namespace Mentor.Application.Conversations;

public interface IConversationRepository
{
    List<Conversation> Conversations();
    Conversation? FindConversation(long id);
    (Conversation Conversation, bool Added) Add(string url);
    DirectConversationResult UpsertDirect(DirectConversation data);
    bool Retry(long id);
    bool RefreshToday(long id, ImportedConversation data);
    bool DeleteToday(long id);
}
