using Mentor.Application.Conversations;

namespace Mentor.Infrastructure.Persistence.Conversations;

internal sealed class ConversationEntity
{
    public long Id
    {
        get; set;
    }
    public string Url { get; set; } = "";
    public string Title { get; set; } = "";
    public string Status { get; set; } = "queued";
    public string? Error
    {
        get; set;
    }
    public string CreatedAt { get; set; } = "";
    public string? ImportedAt
    {
        get; set;
    }
    public List<Message> Messages { get; set; } = [];

    public Conversation ToModel() => new(Id, Url, Title, Status, Error, CreatedAt, ImportedAt, Messages);
}
