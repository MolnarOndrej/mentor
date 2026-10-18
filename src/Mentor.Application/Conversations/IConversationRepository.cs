namespace Mentor.Application.Conversations;

public interface IConversationRepository
{
    List<Conversation> Conversations();
    Conversation? FindConversation(long id);
    (Conversation Conversation, bool Added) Add(string url);
    bool Retry(long id);
}
