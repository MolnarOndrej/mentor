using System.Text.Json;

namespace Mentor.Infrastructure.Analyses;

internal static class CodexResponseParser
{
    public static string Parse(string stdout, string stderr)
    {
        string? result = null;
        var completed = false;
        var actionAttempted = false;
        foreach (var line in stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            var type = root.GetProperty("type").GetString();
            if (type is "error" or "turn.failed")
                throw new InvalidOperationException("Codex CLI oznámil chybu: " + Tail(line));
            if (type == "turn.completed")
                completed = true;
            if (root.TryGetProperty("item", out var item) && item.TryGetProperty("type", out var kind))
            {
                if (kind.GetString() is "command_execution" or "file_change" or "mcp_tool_call" or "web_search")
                    actionAttempted = true;
                if (type == "item.completed" && kind.GetString() == "agent_message")
                    result = item.GetProperty("text").GetString();
            }
        }
        if (actionAttempted)
            throw new InvalidOperationException("Codex se pokusil použít akční nástroj; výsledek nebyl přijat.");
        if (!completed || string.IsNullOrWhiteSpace(result))
            throw new InvalidOperationException("Codex CLI nevrátil dokončený rozbor. " + Tail(stderr));
        return result;
    }
    private static string Tail(string value) => value.Length <= 3000 ? value : value[^3000..];
}
