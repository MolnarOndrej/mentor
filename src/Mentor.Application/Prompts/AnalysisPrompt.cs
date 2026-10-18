using System.Text.Json;
using Mentor.Application.Analyses;

namespace Mentor.Application.Prompts;

public static class AnalysisPrompt
{
    public static int MaximumInputCharacters => 240_000;
    public static string Data(AnalysisInput input) => JsonSerializer.Serialize(
        new
        {
            input.Day,
            TimeZone = TimeZoneInfo.Local.Id,
            Conversations = input.Conversations.Select(c => new { c.Id, c.Title, c.Url, c.Messages })
        },
        new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }).ReplaceLineEndings("\n");

    public static string Build(AnalysisInput input)
    {
        if (input.FullPrompt != null)
            return input.FullPrompt;
        Validate(input.Prompt);
        return input.Prompt.Replace("{{data}}", Data(input), StringComparison.Ordinal);
    }

    public static string Template(string prompt, string? data)
    {
        // The editor shows the resolved prompt. Replace its unchanged data block with
        // a visible slot so a later day's conversations never reuse today's snapshot.
        if (!string.IsNullOrEmpty(data))
        {
            var index = prompt.IndexOf(data, StringComparison.Ordinal);
            if (index >= 0 && prompt.IndexOf(data, index + data.Length, StringComparison.Ordinal) < 0)
                prompt = prompt[..index] + "{{data}}" + prompt[(index + data.Length)..];
        }
        Validate(prompt);
        return prompt;
    }

    public static void Validate(string prompt)
    {
        if (string.IsNullOrWhiteSpace(prompt) || prompt.Length > 20_000)
            throw new ArgumentException("Zadání musí mít 1 až 20 000 znaků bez dat konverzací.");
        var index = prompt.IndexOf("{{data}}", StringComparison.Ordinal);
        if (index < 0 || prompt.IndexOf("{{data}}", index + 8, StringComparison.Ordinal) >= 0)
            throw new ArgumentException("V promptu ponechte blok dat konverzací, nebo jej nahraďte jedním {{data}}. Veškeré instrukce můžete upravit.");
    }
}
