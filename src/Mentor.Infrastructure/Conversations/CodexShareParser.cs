using System.Text.Json;
using Mentor.Application.Conversations;

namespace Mentor.Infrastructure.Conversations;

public static class CodexShareParser
{
    public static ImportedConversation Parse(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("version", out var version) || !version.TryGetInt32(out var number) || number != 1 ||
                !root.TryGetProperty("turns", out var turns) || turns.ValueKind != JsonValueKind.Array)
                throw InvalidSnapshot();

            var messages = new List<Message>();
            foreach (var turn in turns.EnumerateArray())
            {
                if (turn.ValueKind != JsonValueKind.Object || !turn.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
                    throw InvalidSnapshot();
                foreach (var item in items.EnumerateArray())
                {
                    var type = RequiredText(item, "type");
                    string role, text;
                    switch (type)
                    {
                        case "userMessage":
                            role = "user";
                            if (!item.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array)
                                throw InvalidSnapshot();
                            var parts = new List<string>();
                            foreach (var part in content.EnumerateArray())
                            {
                                if (RequiredText(part, "type") != "text")
                                    throw new InvalidOperationException("Konverzace Codexu obsahuje netextovou zprávu (např. přílohu), kterou zatím neumím spolehlivě importovat.");
                                parts.Add(RequiredText(part, "text"));
                            }
                            text = string.Join("\n", parts);
                            break;
                        case "agentMessage":
                            if (item.TryGetProperty("phase", out var phase) && phase.ValueKind != JsonValueKind.Null)
                            {
                                if (phase.ValueKind != JsonValueKind.String)
                                    throw InvalidSnapshot();
                                if (phase.GetString() == "commentary")
                                    continue;
                                if (phase.GetString() != "final")
                                    throw InvalidSnapshot();
                            }
                            role = "assistant";
                            text = RequiredText(item, "text");
                            break;
                        case "reasoning":
                        case "commandExecution":
                        case "fileChange":
                        case "mcpToolCall":
                        case "dynamicToolCall":
                        case "collabAgentToolCall":
                        case "webSearch":
                        case "imageGeneration":
                        case "plan":
                            continue;
                        default:
                            throw InvalidSnapshot();
                    }
                    if (!string.IsNullOrWhiteSpace(text))
                        messages.Add(new(role, text, messages.Count + 1));
                }
            }
            if (!messages.Any(m => m.Role == "user") || !messages.Any(m => m.Role == "assistant"))
                throw new InvalidOperationException("Nepodařilo se získat uživatelské zprávy i finální odpovědi Codexu. Import nelze považovat za úplný.");
            var title = root.TryGetProperty("title", out var titleElement) && titleElement.ValueKind == JsonValueKind.String
                ? titleElement.GetString() : null;
            return new(string.IsNullOrWhiteSpace(title) ? "Sdílená konverzace Codexu" : title, messages);
        }
        catch (JsonException error)
        {
            throw new InvalidOperationException("Zdroj neobsahuje platná JSON data sdílené konverzace Codexu.", error);
        }
    }

    private static string RequiredText(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String)
            throw InvalidSnapshot();
        return value.GetString()!;
    }

    private static InvalidOperationException InvalidSnapshot() =>
        new("Sdílená konverzace Codexu má neznámý nebo neúplný formát. Import byl zastaven.");
}
