using Mentor.Application.Analyses;
using Mentor.Checks.Persistence;
using Mentor.Checks.TestDoubles;
using Microsoft.Data.Sqlite;

namespace Mentor.Checks.Analyses;

internal static class AnalysisReplacementChecks
{
    public static void Run(Action<bool, string> check)
    {
        const string day = "2026-10-18";
        var clock = new TestClock(LocalTime.At(day + "T12:00:00"));
        var path = Path.Combine(Path.GetTempPath(), "mentor-replacement-" + Guid.NewGuid(), "mentor.db");
        var repository = TestRepository.Create(path, "Instructions\n{{data}}", clock);
        try
        {
            repository.EnqueueAnalysis("manual", day);
            throw new Exception("Empty day accepted");
        }
        catch (ArgumentException) { check(repository.Analyses().Count == 0, "empty day rejected without creating a report"); }
        var conversation = repository.Add("https://chatgpt.com/share/00000000-0000-0000-0000-000000000001").Conversation;
        repository.ClaimImport();
        repository.ImportDone(conversation.Id, new("Test", [new("user", "Message", 1)]));
        var original = repository.EnqueueAnalysis("manual", day);
        repository.ClaimAnalysis();
        repository.AnalysisDone(original, "Original report");
        var replacement = repository.EnqueueAnalysis("manual", day);
        check(repository.FindAnalysis(original)?.Result == "Original report", "unchanged data allows rerun while original remains readable in queue");
        repository.ClaimAnalysis();
        check(repository.FindAnalysis(original)?.Result == "Original report", "original remains readable during generation");
        try
        {
            repository.AnalysisDone(replacement, " \n");
            throw new Exception("Empty result accepted");
        }
        catch (InvalidOperationException) { check(repository.FindAnalysis(original)?.Result == "Original report", "invalid result cannot replace original"); }
        repository.AnalysisFailed(replacement, "Invalid result");
        check(repository.FindAnalysis(replacement)?.Error == "Invalid result" && repository.FindAnalysis(original)?.Result == "Original report",
            "last attempt error is separate from successful report");
        var interrupted = repository.EnqueueAnalysis("manual", day);
        repository.ClaimAnalysis();
        repository = TestRepository.Create(path, "{{data}}", clock);
        check(repository.FindAnalysis(original)?.Result == "Original report" && repository.FindAnalysis(interrupted)?.Status == "failed" &&
            repository.FindAnalysis(replacement) == null, "restart preserves original and only the latest failed attempt");
        replacement = repository.EnqueueAnalysis("manual", day);
        repository.ClaimAnalysis();
        using var database = new SqliteConnection($"Data Source={path}");
        database.Open();
        using var command = database.CreateCommand();
        command.CommandText = "CREATE TRIGGER reject_replacement BEFORE DELETE ON analyses BEGIN SELECT RAISE(ABORT, 'Test rollback'); END";
        command.ExecuteNonQuery();
        try
        {
            repository.AnalysisDone(replacement, "New report");
            throw new Exception("Database failure ignored");
        }
        catch (Microsoft.EntityFrameworkCore.DbUpdateException)
        {
            check(repository.FindAnalysis(original)?.Result == "Original report" && repository.FindAnalysis(replacement) is { Status: "running", Result: null },
                "failed replacement transaction rolls back both result and deletion");
        }
        command.CommandText = "DROP TRIGGER reject_replacement";
        command.ExecuteNonQuery();
        repository.AnalysisDone(replacement, "New report");
        repository = TestRepository.Create(path, "{{data}}", clock);
        check(repository.Analyses() is [{ Id: var id, Result: "New report", Status: "completed" }] && id == replacement &&
            repository.FindAnalysis(original) == null && repository.FullPrompt(original) == null,
            "successful replacement persists only the new report with no replaced history");
    }
}
