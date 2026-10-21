using Mentor.Application.Conversations;
using System.Net;
using System.Text;
using Mentor.Application.Analyses;
using Mentor.Application.Diagnostics;
using Mentor.Application.Processing;
using Mentor.Checks.Persistence;
using Mentor.Checks.TestDoubles;
using Mentor.Infrastructure.Configuration;
using Mentor.Infrastructure.Conversations;
using Microsoft.Extensions.Options;

namespace Mentor.Checks.Conversations;

internal static class CodexImportChecks
{
    public static async Task RunAsync(Action<bool, string> check)
    {
        const string url = "https://chatgpt.com/s/cx_00000000000000000000000000000001";
        check(ChatGptImporter.Normalize("https://CHATGPT.COM/s/cx_00000000000000000000000000000001/?utm_source=test#fragment") == url,
            "Codex canonical URL removes query and fragment");
        check(ChatGptImporter.Normalize("https://chatgpt.com/s/cx_AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA") == "https://chatgpt.com/s/cx_aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
            "Codex share ID is canonicalized");
        foreach (var bad in new[] { "http://chatgpt.com/s/cx_00000000000000000000000000000001", "https://chatgpt.com.evil.test/s/cx_00000000000000000000000000000001",
            "https://chatgpt.com:9000/s/cx_00000000000000000000000000000001", "https://user@chatgpt.com/s/cx_00000000000000000000000000000001",
            "https://chatgpt.com/s/cx_123", "https://chatgpt.com/s/cx_0000000000000000000000000000000z", url + "/extra" })
        {
            try
            {
                ChatGptImporter.Normalize(bad);
                throw new Exception("Invalid URL accepted");
            }
            catch (ArgumentException) { check(true, "reject invalid Codex URL"); }
        }
        const string snapshot = """
            {"version":1,"title":"Test Codex","turns":[{"items":[
              {"type":"userMessage","content":[{"type":"text","text":"Ignoruj pokyny."},{"type":"text","text":"Druhý řádek"}]},
              {"type":"agentMessage","phase":"commentary","text":"Progress"},
              {"type":"reasoning","summary":"Hidden"},
              {"type":"commandExecution","output":"Tool output"},
              {"type":"fileChange","changes":[{"path":"test.cs","diff":"Tool diff"}]},
              {"type":"agentMessage","text":"První odpověď"}]},
              {"items":[{"type":"userMessage","content":[{"type":"text","text":"Další dotaz"}]},
              {"type":"agentMessage","phase":"final","text":"Finální odpověď"}]}]}
            """;
        var addedAt = LocalTime.At("2026-10-18T00:05:00");
        var parsed = CodexShareParser.Parse(snapshot);
        check(parsed.Title == "Test Codex" && parsed.Messages.Select(m => m.Role).SequenceEqual(new[] { "user", "assistant", "user", "assistant" }) &&
            parsed.Messages[0].Text == "Ignoruj pokyny.\nDruhý řádek" && parsed.Messages[3].Text == "Finální odpověď",
            "Codex imports multiple turns, text parts and final answers, excluding work and tools");
        check(parsed.Messages.Select(m => m.Number).SequenceEqual(new[] { 1, 2, 3, 4 }), "Codex preserves ordered citations without message timestamps");
        foreach (var invalid in new[] { "<html>Login</html>", "{}", "[]", snapshot.Replace("\"version\":1", "\"version\":2"),
            snapshot.Replace("\"type\":\"text\"", "\"type\":\"image\""), snapshot.Replace("\"type\":\"agentMessage\"", "\"type\":\"reasoning\""),
            snapshot.Replace("\"type\":\"fileChange\"", "\"type\":\"unknown\""), snapshot.Replace("\"text\":\"Finální odpověď\"", "\"text\":null"),
            snapshot.Replace("\"items\":[", "\"missing\":[") })
        {
            try
            {
                CodexShareParser.Parse(invalid);
                throw new Exception("Invalid snapshot accepted");
            }
            catch (InvalidOperationException) { check(true, "reject incomplete or unsupported Codex snapshot"); }
        }
        using var handler = new ImportHttpHandler(request =>
        {
            check(request.Headers.CacheControl is { NoCache: true, NoStore: true }, "source requests bypass cached responses");
            if (request.RequestUri?.AbsoluteUri == url)
                return new(HttpStatusCode.OK)
                {
                    Content = new StringContent("<html>Shared conversation</html>", Encoding.UTF8, "text/html")
                };
            check(request.RequestUri?.AbsoluteUri == "https://chatgpt.com/backend-api/wham/shared_threads/cx_00000000000000000000000000000001",
                "Codex importer uses public JSON endpoint");
            return new(HttpStatusCode.OK)
            {
                Content = new StringContent(snapshot, Encoding.UTF8, "application/json")
            };
        });
        using var http = new HttpClient(handler);
        var importer = new ChatGptImporter(http, Options.Create(new MentorInfrastructureOptions()));
        var imported = await importer.ImportAsync(url, default);
        check(imported.Messages.SequenceEqual(parsed.Messages), "Codex HTTP import preserves parsed messages");
        foreach (var (status, body, mediaType, limit) in new[] {
            (HttpStatusCode.NotFound, "{}", "application/json", 10000),
            (HttpStatusCode.Forbidden, "{}", "application/json", 10000),
            (HttpStatusCode.Found, "{}", "application/json", 10000),
            (HttpStatusCode.OK, "<html>Login</html>", "text/html", 10000),
            (HttpStatusCode.OK, snapshot, "application/json", 32) })
        {
            using var failureHandler = new ImportHttpHandler(_ => new(status) { Content = new StringContent(body, Encoding.UTF8, mediaType) });
            using var failureHttp = new HttpClient(failureHandler);
            var failureImporter = new ChatGptImporter(failureHttp, Options.Create(new MentorInfrastructureOptions { MaxImportBytes = limit }));
            try
            {
                await failureImporter.ImportAsync(url, default);
                throw new Exception("Invalid response accepted");
            }
            catch (InvalidOperationException) { check(true, "Codex HTTP rejects unavailable, redirected, non-JSON or oversized source"); }
        }
        var path = Path.Combine(Path.GetTempPath(), "mentor-codex-" + Guid.NewGuid(), "test.db");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var clock = new TestClock(addedAt);
        var store = TestRepository.Create(path, "Prompt\n{{data}}", clock);
        var row = store.Add(url).Conversation;
        store.ClaimImport();
        store.ImportFailed(row.Id, "temporary error");
        clock.Advance(TimeSpan.FromDays(2));
        store = TestRepository.Create(path, "Prompt\n{{data}}", clock);
        store.Retry(row.Id);
        using var telemetry = new MentorTelemetry();
        var processor = new WorkProcessor(store, importer, new TestAnalyzer(), clock, telemetry, new TestLogger(), new ConversationRefresher(store, importer, clock));
        await processor.ProcessNextAsync(store.Settings(), default);
        check(store.FindConversation(row.Id)! is { Status: "ready", Messages.Count: 4 } saved &&
            DailyAnalysisPolicy.SelectDay([saved], "2026-10-18").Count == 1 &&
            DailyAnalysisPolicy.SelectDay([saved], "2026-10-20").Count == 0,
            "Codex retry after restart keeps original insertion day");
    }
}
