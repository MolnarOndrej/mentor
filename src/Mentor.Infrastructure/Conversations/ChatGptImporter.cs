using System.Text;
using System.Text.RegularExpressions;
using Mentor.Application.Conversations;
using Mentor.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace Mentor.Infrastructure.Conversations;

public sealed partial class ChatGptImporter(HttpClient http, IOptions<MentorInfrastructureOptions> options) : IConversationImporter
{
    public string NormalizeUrl(string url) => Normalize(url);

    public static string Normalize(string value)
    {
        if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri) || uri.Scheme != "https" ||
           !uri.Host.Equals("chatgpt.com", StringComparison.OrdinalIgnoreCase) || !uri.IsDefaultPort || uri.UserInfo.Length > 0 ||
           !(SharePath().IsMatch(uri.AbsolutePath) || CodexSharePath().IsMatch(uri.AbsolutePath)))
            throw new ArgumentException("Podporovány jsou veřejné odkazy https://chatgpt.com/share/<id> a https://chatgpt.com/s/cx_<id>. Tento zdroj nebo formát není podporován.");
        return "https://chatgpt.com" + uri.AbsolutePath.TrimEnd('/').ToLowerInvariant();
    }
    public async Task<ImportedConversation> ImportAsync(string url, CancellationToken ct)
    {
        try
        {
            return await DownloadAsync(url, ct);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new TimeoutException($"Načítání zdroje překročilo limit {options.Value.ImportTimeout.TotalSeconds:g} sekund. Obsah nebyl uložen.");
        }
        catch (HttpRequestException e)
        {
            throw new InvalidOperationException("Zdroj nelze načíst kvůli síťové chybě: " + e.Message, e);
        }
    }
    private async Task<ImportedConversation> DownloadAsync(string url, CancellationToken ct)
    {
        var normalized = Normalize(url);
        var codex = new Uri(normalized).AbsolutePath.StartsWith("/s/", StringComparison.Ordinal);
        var source = codex ? "https://chatgpt.com/backend-api/wham/shared_threads/" + normalized.Split('/')[^1] : normalized;
        using var response = await http.GetAsync(source, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Zdroj vrátil HTTP {(int)response.StatusCode} ({response.ReasonPhrase}). Odkaz může být smazaný, neveřejný nebo blokovaný.");
        if (response.Content.Headers.ContentType?.MediaType != (codex ? "application/json" : "text/html"))
            throw new InvalidOperationException(codex ? "Zdroj nevrátil JSON sdílené konverzace Codexu." : "Zdroj nevrátil HTML konverzace.");
        var limit = options.Value.MaxImportBytes;
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var buffer = new MemoryStream();
        var bytes = new byte[16384];
        int read;
        while ((read = await stream.ReadAsync(bytes, ct)) > 0)
        {
            if (buffer.Length + read > limit)
                throw new InvalidOperationException($"Stránka překročila limit {limit} bajtů; nebyla importována jen částečně.");
            buffer.Write(bytes, 0, read);
        }
        var content = Encoding.UTF8.GetString(buffer.ToArray());
        return codex ? CodexShareParser.Parse(content) : ChatGptShareParser.Parse(content);
    }
    [GeneratedRegex("^/share/[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}/?$")]
    private static partial Regex SharePath();

    [GeneratedRegex("^/s/cx_[0-9a-fA-F]{32}/?$")]
    private static partial Regex CodexSharePath();
}
