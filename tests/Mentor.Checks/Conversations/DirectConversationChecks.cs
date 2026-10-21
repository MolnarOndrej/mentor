using Mentor.Application.Analyses;
using Mentor.Application.Conversations;
using Mentor.Application.Dashboard;
using Mentor.Application.Diagnostics;
using Mentor.Application.Processing;
using Mentor.Application.Prompts;
using Mentor.Checks.Persistence;
using Mentor.Checks.TestDoubles;

namespace Mentor.Checks.Conversations;

internal static class DirectConversationChecks
{
    public static async Task RunAsync(Action<bool, string> check)
    {
        var clock = new TestClock(LocalTime.At("2026-10-18T23:59:00"));
        var path = Path.Combine(Path.GetTempPath(), "mentor-direct-" + Guid.NewGuid(), "mentor.db");
        var repository = TestRepository.Create(path, "Instructions\n{{data}}", clock);
        var importer = new TestImporter();
        var refresher = new ConversationRefresher(repository, importer, clock);
        var service = new MentorService(repository, repository, repository, repository, importer, clock, refresher);
        var data = new DirectConversation("codex", "stable-id", "Direct title", "summary",
            [new("assistant", "Only a summary", 1)], "https://chatgpt.com/c/private");
        var first = repository.UpsertDirect(data);
        check(first is { Added: true, Day: "2026-10-18", MessageCount: 1, ContentKind: "summary" } &&
            repository.FindConversation(first.Id) is { Status: "ready", IngestionKind: "direct", Source: "codex", ExternalId: "stable-id" },
            "direct snapshot is immediately ready and assigned to local receipt day");
        check(repository.ClaimImport() == null && !repository.Retry(first.Id) &&
            !repository.RefreshToday(first.Id, new("Wrong", [new("user", "wrong", 1)])),
            "direct snapshots cannot enter the share import or refresh pipeline");
        await refresher.RefreshAsync(first.Id, default);
        check((await refresher.RefreshDayAsync(first.Day, default)).Count == 0 && importer.Calls.Count == 0,
            "individual and bulk refresh never download private direct URLs");

        var analyzer = new TestAnalyzer();
        using var telemetry = new MentorTelemetry();
        var processor = new WorkProcessor(repository, importer, analyzer, clock, telemetry, new TestLogger(), refresher);
        // Prevent the automatic due-time run; this check explicitly queues the day.
        repository.SaveRuntimeSettings(new(5, "23:59"));
        var analysis = repository.EnqueueAnalysis("manual", first.Day);
        await processor.ProcessNextAsync(repository.Settings(), default);
        check(repository.FindAnalysis(analysis)?.Status == "completed" && importer.Calls.Count == 0 &&
            analyzer.Input?.Conversations.Single().ContentKind == "summary" &&
            repository.FullPrompt(analysis)!.Contains("summary"),
            "daily processing analyzes summary metadata without fetching its URL");
        var replacement = data with
        {
            Title = "Complete title",
            Url = null,
            ContentKind = "transcript",
            Messages = [new("system", "Context", 1), new("user", "Question", 3),
                new("assistant", "Answer", 7), new("tool", "Visible tool result", 8)]
        };
        var second = repository.UpsertDirect(replacement);
        check(!second.Added && first.Id == second.Id && repository.Conversations().Count == 1 &&
            repository.FindConversation(first.Id) is { Url: null, Title: "Complete title", ContentKind: "transcript" } stored &&
            stored.Messages.SequenceEqual(replacement.Messages) && service.GetState().ChangedDays.Contains(first.Day),
            "repeat receipt replaces the entire snapshot and marks the completed analysis as changed");

        var tasks = Enumerable.Range(0, 20).Select(_ => Task.Run(() => repository.UpsertDirect(replacement)));
        var results = await Task.WhenAll(tasks);
        check(results.All(r => !r.Added && r.Id == first.Id) && repository.Conversations().Count == 1,
            "concurrent retries keep one stable source/id record");
        check(repository.UpsertDirect(replacement with
        {
            Source = "chatgpt"
        }).Added &&
            repository.UpsertDirect(replacement with
            {
                ExternalId = "other-id"
            }).Added,
            "source and external identifier jointly define identity");
        const string sharedUrl = "https://chatgpt.com/share/6ad4ab23-61e0-83eb-8533-14def95d3491";
        var directWithShareUrl = repository.UpsertDirect(replacement with
        {
            ExternalId = "shared-metadata",
            Url = sharedUrl
        });
        var share = repository.Add(sharedUrl);
        check(share.Added && share.Conversation.Id != directWithShareUrl.Id && !repository.Add(sharedUrl).Added,
            "metadata URL does not collide with separate Share import identity");

        var count = repository.Conversations().Count;
        foreach (var invalid in new[]
        {
            replacement with { Source = "Codex" }, replacement with { ExternalId = " " },
            replacement with { Source = null! }, replacement with { Title = null! },
            replacement with { ContentKind = null! }, replacement with { ContentKind = "partial" },
            replacement with { Url = "file:///private" }, replacement with { Url = "https://u:p@host/" },
            replacement with { Messages = [] }, replacement with { Messages = null! },
            replacement with { Messages = [null!] },
            replacement with { Messages = [new("unknown", "text", 1)] },
            replacement with { Messages = [new("user", "text", 0)] },
            replacement with { Messages = [new("user", "text", 2), new("assistant", "text", 1)] },
            replacement with { Messages = [new("user", "text", 1), new("assistant", "text", 1)] },
            replacement with { Messages = [new("user", " ", 1)] },
            replacement with { Messages = [new("user", new string('x', 240_001), 1)] }
        })
        {
            try
            {
                repository.UpsertDirect(invalid);
                throw new Exception("Invalid direct data accepted");
            }
            catch (ArgumentException) { }
        }
        check(repository.Conversations().Count == count &&
            repository.FindConversation(first.Id)!.Messages.SequenceEqual(replacement.Messages),
            "invalid identity, kind, URL, roles, ordering and oversized content leave stored data untouched");

        clock.Advance(TimeSpan.FromMinutes(2));
        repository = TestRepository.Create(path, "{{data}}", clock);
        var nextDay = repository.UpsertDirect(replacement);
        check(nextDay.Id == first.Id && !nextDay.Added && nextDay.Day == first.Day &&
            DailyAnalysisPolicy.SelectDay(repository.Conversations(), "2026-10-19").Count == 0 &&
            repository.FindConversation(first.Id)!.Messages.SequenceEqual(replacement.Messages),
            "restart and later receipt retain data, identity and first insertion day across midnight");
        check(repository.UpsertDirect(replacement with
        {
            ExternalId = "new-day"
        }).Day == "2026-10-19",
            "new identity uses Mentors current local day");
    }
}
