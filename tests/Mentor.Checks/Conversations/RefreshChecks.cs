using Mentor.Application.Conversations;
using Mentor.Application.Dashboard;
using Mentor.Application.Diagnostics;
using Mentor.Application.Processing;
using Mentor.Checks.Persistence;
using Mentor.Checks.TestDoubles;

namespace Mentor.Checks.Conversations;

internal static class RefreshChecks
{
    public static async Task RunAsync(Action<bool, string> check)
    {
        var clock = new TestClock(LocalTime.At("2026-10-18T12:00:00"));
        var path = Path.Combine(Path.GetTempPath(), "mentor-refresh-" + Guid.NewGuid(), "mentor.db");
        var repository = TestRepository.Create(path, "Instructions\n{{data}}", clock);
        var importer = new TestImporter();
        var refresher = new ConversationRefresher(repository, importer, clock);
        var service = new MentorService(repository, repository, repository, repository, importer, clock, refresher);
        var analyzer = new TestAnalyzer();
        using var telemetry = new MentorTelemetry();
        var processor = new WorkProcessor(repository, repository, importer, analyzer, clock, telemetry, new TestLogger(), refresher);
        const string url = "https://chatgpt.com/share/6ad4ab23-61e0-83eb-8533-14def95d3491";
        var first = service.AddConversation(url).Id;
        var secondUrl = url[..^1] + "2";
        var second = service.AddConversation(secondUrl).Id;
        await processor.ProcessNextAsync(default);
        await processor.ProcessNextAsync(default);
        var before = repository.FindConversation(first)!;
        var reviewed = service.RenderPrompt("2026-10-18", null);
        var analysis = service.AnalyzeDay("2026-10-18", reviewed.Prompt, reviewed.Data);
        importer.Data = new("Updated title", [new("user", "New content", 1), new("assistant", "New answer", 2), new("user", "More", 3)]);
        importer.UnavailableUrls.Add(secondUrl);
        importer.Calls.Clear();
        await processor.ProcessNextAsync(default);
        check(importer.Calls.Order().SequenceEqual(new[] { url, secondUrl }.Order()), "today's analysis refreshes all saved links");
        check(analyzer.Input!.Conversations.Single(c => c.Id == first).Messages.Count == 3 &&
            analyzer.Input.Conversations.Single(c => c.Id == second).Messages.Count == 2,
            "analysis uses new content and falls back to saved content for unavailable links");
        check(service.GetFullPrompt(analysis)!.Contains("New content") &&
            service.GetAnalysis(analysis)!.MessageKeys.Count == 5,
            "actual refreshed analysis prompt and coverage are persisted");
        check(repository.FindConversation(first) is { Url: url } refreshed && refreshed.CreatedAt == before.CreatedAt,
            "refresh retains source URL and insertion day");
        var saved = repository.FindConversation(second)!;
        var result = await service.RefreshConversationAsync(second, default);
        check(!result.Updated && result.Message.Length > 0 &&
            System.Text.Json.JsonSerializer.Serialize(repository.FindConversation(second)) == System.Text.Json.JsonSerializer.Serialize(saved),
            "manual unavailable refresh returns notice without modifying saved data");
        importer.UnavailableUrls.Clear();
        result = await service.RefreshConversationAsync(second, default);
        check(result.Updated && repository.FindConversation(second)!.Messages.Count == 3,
            "manual refresh updates only the selected conversation");
        var unchanged = System.Text.Json.JsonSerializer.Serialize(repository.FindConversation(second));
        result = await service.RefreshConversationAsync(second, default);
        check(!result.Updated && result.Message.Contains("beze změn") &&
            unchanged == System.Text.Json.JsonSerializer.Serialize(repository.FindConversation(second)),
            "unchanged source does not report an update or modify persisted data");
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        try
        {
            await service.RefreshConversationAsync(first, cancelled.Token);
            throw new Exception("Cancellation swallowed");
        }
        catch (OperationCanceledException) { check(true, "refresh propagates cancellation"); }
        service.DeleteConversation(first);
        check(repository.FindConversation(first) == null && !service.GetState().Conversations.Any(c => c.Id == first),
            "delete removes today's conversation from database and dashboard");
        repository = TestRepository.Create(path, "{{data}}", clock);
        check(repository.FindConversation(first) == null && repository.FindConversation(second)!.Url == secondUrl,
            "deletion and stored source URLs survive restart");
        clock.Advance(TimeSpan.FromDays(1));
        importer.Calls.Clear();
        check(!(await service.RefreshConversationAsync(second, default)).Updated && importer.Calls.Count == 0,
            "archived conversation cannot be refreshed");
        check(!repository.DeleteToday(second) && !repository.RefreshToday(second, importer.Data),
            "persistence rejects modification of archived conversations");
        service.AnalyzeDay("2026-10-18", null, null);
        await processor.ProcessNextAsync(default);
        check(importer.Calls.Count == 0, "archived analysis never reloads source links");
    }
}
