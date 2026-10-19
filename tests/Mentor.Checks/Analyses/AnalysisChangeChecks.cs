using Mentor.Application.Conversations;
using Mentor.Application.Dashboard;
using Mentor.Checks.Persistence;
using Mentor.Checks.TestDoubles;

namespace Mentor.Checks.Analyses;

internal static class AnalysisChangeChecks
{
    public static async Task RunAsync(Action<bool, string> check)
    {
        const string day = "2026-10-18";
        var clock = new TestClock(LocalTime.At(day + "T12:00:00"));
        var path = Path.Combine(Path.GetTempPath(), "mentor-changes-" + Guid.NewGuid(), "mentor.db");
        var repository = TestRepository.Create(path, "Instructions\n{{data}}", clock);
        var importer = new TestImporter();
        var refresher = new ConversationRefresher(repository, importer, clock);
        var service = new MentorService(repository, repository, repository, repository, importer, clock, refresher);
        const string url = "https://chatgpt.com/share/00000000-0000-0000-0000-000000000001";
        var first = service.AddConversation(url).Id;
        repository.ClaimImport();
        repository.ImportDone(first, importer.Data);
        check(service.GetState().ChangedDays.Contains(day), "first imported conversation enables analysis");
        CompleteAnalysis(repository, day);
        check(!service.GetState().ChangedDays.Contains(day), "completed analysis clears the change hint");
        var result = await service.RefreshTodayAsync(default);
        check(result is { CheckedCount: 1, UpdatedCount: 0 } && !service.GetState().ChangedDays.Contains(day),
            "bulk refresh of unchanged accessible source keeps the change hint cleared");
        importer.UnavailableUrls.Add(url);
        result = await service.RefreshTodayAsync(default);
        check(result is { UpdatedCount: 0 } && !service.GetState().ChangedDays.Contains(day),
            "bulk refresh of unavailable source keeps the change hint cleared");
        importer.UnavailableUrls.Clear();
        importer.Data = new(importer.Data.Title, [new("user", "Edited text with same message number", 1), new("assistant", "Response", 2)]);
        result = await service.RefreshTodayAsync(default);
        check(result is { UpdatedCount: 1 } && service.GetState().ChangedDays.Contains(day),
            "changed text with identical message count and numbers enables analysis");
        repository = TestRepository.Create(path, "{{data}}", clock);
        var restarted = new MentorService(repository, repository, repository, repository, importer, clock,
            new ConversationRefresher(repository, importer, clock));
        check(restarted.GetState().ChangedDays.Contains(day), "analysis change detection survives repository restart");
        CompleteAnalysis(repository, day);
        check(!service.GetState().ChangedDays.Contains(day), "analysis consumes refreshed changes");
        var secondUrl = url[..^1] + "2";
        var second = service.AddConversation(secondUrl).Id;
        check(!service.GetState().ChangedDays.Contains(day), "queued new conversation waits for successful import before enabling analysis");
        repository.ClaimImport();
        repository.ImportFailed(second, "Unavailable source");
        check(!service.GetState().ChangedDays.Contains(day), "failed new conversation import does not enable analysis");
        repository.Retry(second);
        repository.ClaimImport();
        repository.ImportDone(second, importer.Data);
        check(service.GetState().ChangedDays.Contains(day), "new conversation enables analysis after a previously completed analysis");
        CompleteAnalysis(repository, day);
        check(!service.GetState().ChangedDays.Contains(day), "analysis consumes the newly added conversation");
        check(!service.AddConversation(secondUrl).Added && !service.GetState().ChangedDays.Contains(day),
            "duplicate conversation submission does not enable analysis again");
        importer.Data = importer.Data with
        {
            Title = "Changed title"
        };
        importer.UnavailableUrls.Add(secondUrl);
        importer.Calls.Clear();
        result = await service.RefreshTodayAsync(default);
        check(result is { CheckedCount: 2, UpdatedCount: 1 } && importer.Calls.Count == 2 &&
            service.GetState().ChangedDays.Contains(day), "bulk refresh checks every source and allows partial success");
        CompleteAnalysis(repository, day);
        service.DeleteConversation(first);
        check(service.GetState().ChangedDays.Contains(day), "deletion enables analysis even when remaining messages were analyzed");
        CompleteAnalysis(repository, day);
        service.DeleteConversation(second);
        check(service.GetState().ChangedDays.Contains(day) && service.GetState().Conversations.Count == 0,
            "deleting final conversation enables an empty updated analysis");
        try
        {
            CompleteAnalysis(repository, day);
            throw new Exception("Empty analysis accepted");
        }
        catch (ArgumentException) { check(true, "deleting final conversation prevents analysis"); }
    }

    private static void CompleteAnalysis(Mentor.Infrastructure.Persistence.EfMentorRepository repository, string day)
    {
        var id = repository.EnqueueAnalysis("manual", day);
        repository.ClaimAnalysis();
        repository.AnalysisDone(id, "Test report");
    }
}
