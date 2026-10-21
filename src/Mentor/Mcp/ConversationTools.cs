using System.ComponentModel;
using Mentor.Application.Conversations;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace Mentor.Mcp;

[McpServerToolType]
public sealed class ConversationTools
{
    [McpServerTool(Name = "mentor_upsert_conversation", UseStructuredContent = true,
        Destructive = true, Idempotent = true, OpenWorld = false)]
    [Description("Store or replace a conversation in Mentor by (source, externalId). No Share link is needed. " +
        "contentKind is required: transcript means the complete transcript, summary means a summary, never claim a summary is complete. " +
        "Messages must be in ascending number order with explicit roles. A summary may use one assistant message. " +
        "Mentor assigns the local day on first receipt and retains it on updates, including updates on later days. " +
        "The optional URL is metadata only and is never fetched. Does not start an analysis.")]
    public static DirectConversationResult UpsertConversation(
        [Description("Conversation snapshot. Required: source, externalId, title, contentKind, messages. Optional: url.")]
        DirectConversation conversation, IConversationRepository repository)
    {
        try
        {
            ArgumentNullException.ThrowIfNull(conversation);
            return repository.UpsertDirect(conversation);
        }
        catch (ArgumentException error)
        {
            throw new McpException(error.Message);
        }
    }
}
