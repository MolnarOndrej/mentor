using System.Text.Json;
using System.Text.RegularExpressions;
using Mentor.Application.Conversations;

namespace Mentor.Infrastructure.Conversations;

public static partial class ChatGptShareParser
{
    public static ImportedConversation Parse(string html)
    {
        foreach (Match match in StreamChunk().Matches(html))
        {
            JsonDocument table;
            try
            {
                table = JsonDocument.Parse(JsonSerializer.Deserialize<string>(match.Groups[1].Value)!);
            }
            catch (JsonException) { continue; }
            using (table)
            {
                var a = table.RootElement;
                if (a.ValueKind != JsonValueKind.Array)
                    continue;
                JsonElement Resolve(JsonElement v) => v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i) && i >= 0 && i < a.GetArrayLength() ? a[i] : default;
                JsonElement Property(JsonElement o, string name)
                {
                    if (o.ValueKind != JsonValueKind.Object)
                        return default;
                    foreach (var p in o.EnumerateObject())
                        if (p.Name.StartsWith('_') && int.TryParse(p.Name.AsSpan(1), out var i) && i >= 0 && i < a.GetArrayLength() &&
                           a[i].ValueKind == JsonValueKind.String && a[i].GetString() == name)
                            return Resolve(p.Value);
                    return default;
                }
                string? Text(JsonElement v) => v.ValueKind == JsonValueKind.String ? v.GetString() : null;
                foreach (var obj in a.EnumerateArray())
                {
                    var nodes = Property(obj, "linear_conversation");
                    if (nodes.ValueKind != JsonValueKind.Array)
                        continue;
                    var messages = new List<Message>();
                    foreach (var node in nodes.EnumerateArray())
                    {
                        var message = Property(Resolve(node), "message");
                        var role = Text(Property(Property(message, "author"), "role"));
                        if (role is not ("user" or "assistant"))
                            continue;
                        var channel = Text(Property(message, "channel"));
                        if (role == "assistant" && channel is not (null or "final"))
                            continue;
                        var content = Property(message, "content");
                        var type = Text(Property(content, "content_type"));
                        if (role == "assistant" && type is "thoughts" or "reasoning_recap")
                            continue;
                        if (type != "text")
                            throw new InvalidOperationException("Konverzace obsahuje netextovou zprávu (např. přílohu), kterou zatím neumím spolehlivě importovat.");
                        var parts = Property(content, "parts");
                        if (parts.ValueKind != JsonValueKind.Array)
                            throw new InvalidOperationException("Text zprávy nemá očekávanou strukturu. Import byl zastaven.");
                        var texts = parts.EnumerateArray().Select(p => Text(Resolve(p))).ToList();
                        if (texts.Any(t => t == null))
                            throw new InvalidOperationException("Některé části zpráv nejsou textové; import byl zastaven.");
                        var text = string.Join("\n", texts);
                        if (!string.IsNullOrWhiteSpace(text))
                            messages.Add(new(role, text, messages.Count + 1));
                    }
                    if (!messages.Any(x => x.Role == "user") || !messages.Any(x => x.Role == "assistant"))
                        throw new InvalidOperationException("Nepodařilo se získat uživatelské zprávy i odpovědi AI. Import nelze považovat za úplný.");
                    return new(Text(Property(obj, "title")) ?? "Sdílená konverzace", messages);
                }
            }
        }
        throw new InvalidOperationException("Ve stránce nebyla nalezena čitelná sdílená konverzace. Může vyžadovat přihlášení, být smazaná či blokovaná nebo se změnil formát ChatGPT.");
    }
    [GeneratedRegex("streamController\\.enqueue\\((\"(?:\\\\.|[^\"\\\\])*\")\\)", RegexOptions.Singleline)]
    private static partial Regex StreamChunk();
}
