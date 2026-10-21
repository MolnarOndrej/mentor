using System.ComponentModel;

namespace Mentor.Application.Conversations;

public sealed record DirectConversation(
    [property: Description("Stable source namespace: 1–100 lowercase ASCII letters, digits, dots, underscores or hyphens (e.g. codex, chatgpt).")]
    string Source,
    [property: Description("Stable conversation ID within the source, case-sensitive, 1–500 characters. Reuse on every retry/update.")]
    string ExternalId,
    [property: Description("Conversation title, 1–500 characters without surrounding whitespace or control characters.")]
    string Title,
    [property: Description("Required: transcript for the complete transcript, summary for a summary or partial history.")]
    string ContentKind,
    [property: Description("1–10000 messages in strictly increasing positive number order; roles user, assistant, system, tool; nonblank text; at most 240000 text characters total.")]
    List<Message> Messages,
    [property: Description("Optional HTTP(S) source URL without credentials, at most 2048 characters. Metadata only, never downloaded. Omit/null to clear.")]
    string? Url = null)
{
    public void Validate()
    {
        Required(Source, 100, "source");
        Required(ExternalId, 500, "externalId");
        Required(Title, 500, "title");
        if (Source.Any(c => !(c is >= 'a' and <= 'z' or >= '0' and <= '9' or '.' or '_' or '-')))
            throw new ArgumentException("source musí obsahovat jen malá ASCII písmena, číslice, tečku, podtržítko nebo pomlčku.");
        if (ContentKind is not ("transcript" or "summary"))
            throw new ArgumentException("contentKind musí být transcript (úplný přepis) nebo summary (souhrn).");
        if (Url != null && (Url.Length > 2048 || !Uri.TryCreate(Url, UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("https" or "http") || !string.IsNullOrEmpty(uri.UserInfo)))
            throw new ArgumentException("url musí být absolutní HTTP(S) odkaz bez přihlašovacích údajů nebo null.");
        if (Messages == null || Messages.Count is < 1 or > 10_000)
            throw new ArgumentException("messages musí obsahovat 1 až 10 000 zpráv.");
        long characters = 0;
        var previous = 0;
        foreach (var message in Messages)
        {
            if (message == null || message.Role is not ("user" or "assistant" or "system" or "tool") ||
                string.IsNullOrWhiteSpace(message.Text) || message.Number <= previous)
                throw new ArgumentException("Zprávy vyžadují role user/assistant/system/tool, neprázdný text a rostoucí kladná number.");
            previous = message.Number;
            characters += message.Text.Length;
        }
        if (characters > 240_000)
            throw new ArgumentException("Obsah konverzace smí mít nejvýše 240 000 znaků; data se nezkracují.");
    }

    private static void Required(string value, int maximum, string name)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > maximum || value != value.Trim() || value.Any(char.IsControl))
            throw new ArgumentException($"{name} vyžaduje 1 až {maximum} znaků bez okrajových mezer a řídicích znaků.");
    }
}
