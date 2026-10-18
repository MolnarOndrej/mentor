using Mentor.Application.Analyses;
using Mentor.Application.Conversations;

namespace Mentor.Checks.Persistence;

internal static class PersistenceChecks
{
    public static async Task RunAsync(Action<bool, string> check)
    {
        var directory = Path.Combine(Path.GetTempPath(), "mentor-ef-" + Guid.NewGuid());
        var repository = TestRepository.Create(Path.Combine(directory, "mentor.db"),
            "Original instructions\n{{data}}", TimeProvider.System);
        const string url = "https://chatgpt.com/share/00000000-0000-0000-0000-000000000001";
        var additions = await Task.WhenAll(Enumerable.Range(0, 12)
            .Select(_ => Task.Run(() => repository.Add(url))));
        check(additions.Count(x => x.Added) == 1 && additions.Select(x => x.Conversation.Id).Distinct().Count() == 1,
            "EF concurrent duplicate submissions produce one durable conversation");

        var claims = await Task.WhenAll(Enumerable.Range(0, 12)
            .Select(_ => Task.Run(repository.ClaimImport)));
        check(claims.Count(x => x != null) == 1 && repository.Conversations().Single().Status == "importing",
            "EF concurrent import claims return the queued item exactly once");

        var message = new Message("user", "Český text, JSON: {\"a\":1}\nDruhý řádek.",
            DateTimeOffset.Parse("2026-10-18T12:00:00+02:00"), 17);
        repository.ImportDone(additions[0].Conversation.Id, new("Persistence", [message]));
        check(repository.Conversations().Single().Messages.Single() == message,
            "EF JSON mapping round-trips Unicode, message number and timestamp offset");
        repository.Conversations().Single().Messages.Clear();
        check(repository.Conversations().Single().Messages.Count == 1,
            "EF read models cannot mutate persisted messages outside a write operation");

        const string day = "2026-10-18";
        var promptBefore = repository.Settings().Prompt;
        try
        {
            repository.EnqueueAnalysis("manual", day, "Changed instructions\n{{data}}", "stale data");
            throw new Exception("Stale data accepted");
        }
        catch (AnalysisConflictException) { }
        check(repository.Settings().Prompt == promptBefore && repository.Analyses().Count == 0,
            "EF rejected enqueue rolls back settings and the analysis together");

        var enqueues = await Task.WhenAll(Enumerable.Range(0, 12)
            .Select(_ => Task.Run(() => repository.EnqueueAnalysis("manual", day))));
        check(enqueues.Distinct().Count() == 1 && repository.Analyses().Count == 1,
            "EF concurrent analysis requests share one active daily batch");
        var analyses = await Task.WhenAll(Enumerable.Range(0, 12)
            .Select(_ => Task.Run(repository.ClaimAnalysis)));
        check(analyses.Count(x => x.HasValue) == 1 && repository.Analyses().Single().Status == "running" &&
            repository.FullPrompt(enqueues[0])!.Contains("Český text", StringComparison.Ordinal),
            "EF concurrent analysis claims persist one running batch and its prompt snapshot");
    }
}
