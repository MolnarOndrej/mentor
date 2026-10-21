using System.Collections.Concurrent;
using Mentor.Application.Analyses;
using Mentor.Application.Conversations;
using Mentor.Application.Diagnostics;
using Mentor.Application.Processing;
using Mentor.Application.Prompts;
using Mentor.Checks.Persistence;
using Mentor.Checks.TestDoubles;
using Mentor.Infrastructure.Analyses;
using Mentor.Infrastructure.Configuration;
using Mentor.Infrastructure.Processes;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace Mentor.Checks.Discussions;

internal static class DiscussionChecks
{
    public static async Task RunAsync(Action<bool, string> check)
    {
        const string day = "2026-10-18";
        var clock = new TestClock(LocalTime.At(day + "T12:00:00"));
        var directory = Path.Combine(Path.GetTempPath(), "mentor-discussion-" + Guid.NewGuid());
        var path = Path.Combine(directory, "mentor.db");
        var repository = TestRepository.Create(path, "Original instructions\n{{data}}", clock);
        var conversation = repository.Add("https://chatgpt.com/share/00000000-0000-0000-0000-000000000001").Conversation;
        repository.ClaimImport();
        repository.ImportDone(conversation.Id, new("Source", [new("user", "Original source", 1)]));
        var report = repository.EnqueueAnalysis("manual", day);
        repository.ClaimAnalysis();
        repository.AnalysisDone(report, "Original result");
        check(repository.Discussion(report).Count == 0 && repository.ClaimAnswer() == null, "opening report or empty discussion creates no answer job");

        var first = repository.EnqueueAnswer(report, "First question");
        check(repository.Discussion(report) is [{ Status: "queued", Position: 1, Answer: null }], "question and pending status durably stored in order");
        Reject(() => repository.EnqueueAnalysis("manual", day), check, "queued answer blocks analysis");
        Reject(() => repository.EnqueueAnswer(report, "Concurrent"), check, "queued answer blocks another question");
        var originalPrompt = repository.FullPrompt(report)!;
        repository.SavePrompt("Changed instructions\n{{data}}");
        repository.RefreshToday(conversation.Id, new("Changed source", [new("user", "Changed source", 1)]));
        var claim = repository.ClaimAnswer()!.Value;
        using (var json = System.Text.Json.JsonDocument.Parse(claim.Prompt[claim.Prompt.IndexOf('{')..]))
        {
            check(json.RootElement.GetProperty("OriginalInput").GetString() == originalPrompt &&
                json.RootElement.GetProperty("AnalysisResult").GetString() == "Original result" &&
                json.RootElement.GetProperty("Question").GetString() == "First question", "answer uses exact original prompt snapshot and result despite source edits");
        }
        Reject(() => repository.EnqueueAnalysis("manual", day), check, "running answer blocks analysis");
        repository.AnswerDone(first, "First answer");
        repository = TestRepository.Create(path, "{{data}}", clock);
        check(repository.Discussion(report) is [{ Question: "First question", Answer: "First answer", Status: "completed" }], "discussion survives normal restart");
        var second = repository.EnqueueAnswer(report, "Second question");
        check(repository.ClaimAnswer()!.Value.Prompt.Contains("First answer", StringComparison.Ordinal), "next answer includes previous discussion");
        repository.AnswerFailed(second, "CLI failure");
        check(repository.Discussion(report) is [{ Answer: "First answer" }, { Status: "failed", Error: "CLI failure", Position: 2 }], "failure preserves question and prior discussion");
        repository.RetryAnswer(second);
        repository.ClaimAnswer();
        repository = TestRepository.Create(path, "{{data}}", clock);
        check(repository.Discussion(report)[1] is { Status: "failed", CompletedAt: not null, Answer: null } &&
            repository.Discussion(report)[1].Error!.Contains("přerušen", StringComparison.Ordinal), "unclean interruption becomes retryable failure on restart");
        repository.RetryAnswer(second);
        using var stopping = new CancellationTokenSource();
        using var telemetry = new MentorTelemetry();
        var importer = new TestImporter();
        var processorClock = new TestClock(LocalTime.At("2026-10-19T12:00:00"));
        var processor = new WorkProcessor(repository, importer, new TestAnalyzer(), processorClock,
            telemetry, new TestLogger(), new ConversationRefresher(repository, importer, processorClock), new CancellingResponder(stopping));
        try
        {
            await processor.ProcessNextAsync(repository.Settings(), stopping.Token);
            throw new Exception("Cancellation ignored");
        }
        catch (OperationCanceledException) when (stopping.IsCancellationRequested) { }
        check(repository.Discussion(report)[1] is { Status: "failed", Answer: null }, "graceful shutdown rejects late answer and preserves history");

        repository.RetryAnswer(second);
        var capture = Path.Combine(directory, "answer-stdin.txt");
        var responder = new CodexCliAnalyzer(Options.Create(new MentorInfrastructureOptions
        {
            CodexExecutable = Environment.ProcessPath!,
            DataDirectory = directory
        }), telemetry, new CliProcessRunner());
        Environment.SetEnvironmentVariable("MENTOR_CAPTURE_FILE", capture);
        try
        {
            processor = new WorkProcessor(repository, importer, new TestAnalyzer(), processorClock,
                telemetry, new TestLogger(), new ConversationRefresher(repository, importer, processorClock), responder);
            await processor.ProcessNextAsync(repository.Settings(), default);
            check(repository.Discussion(report)[1] is { Status: "completed", Answer: "Testovací rozbor" } &&
                File.ReadAllText(capture).Contains("First answer", StringComparison.Ordinal), "worker runs independent CLI answer with complete discussion context");
        }
        finally { Environment.SetEnvironmentVariable("MENTOR_CAPTURE_FILE", null); }

        var count = repository.Discussion(report).Count;
        try
        {
            repository.EnqueueAnswer(report, new string('x', DiscussionPrompt.MaximumInputCharacters));
            throw new Exception("Oversized context accepted");
        }
        catch (ArgumentException)
        {
            check(repository.Discussion(report).Count == count && repository.ClaimAnswer() == null,
                "oversized context rejected without truncation, job or history mutation");
        }

        var accepted = new ConcurrentBag<string>();
        Parallel.For(0, 12, i =>
        {
            try
            {
                if (i % 2 == 0)
                    repository.EnqueueAnswer(report, "Race question");
                else
                    repository.EnqueueAnalysis("manual", day);
                accepted.Add(i % 2 == 0 ? "answer" : "analysis");
            }
            catch (AnalysisConflictException) { }
        });
        check(accepted.Count == 1, "atomic day reservation accepts exactly one competing answer or analysis");
        if (accepted.Single() == "answer")
        {
            var running = repository.ClaimAnswer()!.Value;
            repository.AnswerDone(running.Id, "Race answer");
        }
        else
        {
            var running = repository.ClaimAnalysis()!.Value;
            repository.AnalysisFailed(running.Id, "Race analysis failed");
        }
        var replacement = repository.EnqueueAnalysis("manual", day);
        Reject(() => repository.EnqueueAnswer(report, "During replacement"), check, "queued replacement blocks question");
        repository.ClaimAnalysis();
        Reject(() => repository.EnqueueAnswer(report, "During generation"), check, "running replacement blocks question");
        using var database = new SqliteConnection(Mentor.Infrastructure.Persistence.MentorDbContext.ConnectionString(path));
        database.Open();
        using var command = database.CreateCommand();
        command.CommandText = "CREATE TRIGGER reject_discussion_delete BEFORE DELETE ON discussion_turns BEGIN SELECT RAISE(ABORT, 'Test rollback'); END";
        command.ExecuteNonQuery();
        try
        {
            repository.AnalysisDone(replacement, "Replacement");
            throw new Exception("Cascade failure ignored");
        }
        catch (Microsoft.EntityFrameworkCore.DbUpdateException)
        {
            check(repository.FindAnalysis(report)?.Result == "Original result" && repository.Discussion(report).Count >= count &&
                repository.FindAnalysis(replacement)?.Status == "running", "failed cascade deletion rolls back replacement and keeps discussion readable");
        }
        command.CommandText = "DROP TRIGGER reject_discussion_delete";
        command.ExecuteNonQuery();
        repository.AnalysisFailed(replacement, "Replacement failed");
        check(repository.Discussion(report).Count >= count, "failed analysis preserves old discussion");
        replacement = repository.EnqueueAnalysis("manual", day);
        repository.ClaimAnalysis();
        repository.AnalysisDone(replacement, "Replacement");
        repository = TestRepository.Create(path, "{{data}}", clock);
        check(repository.Discussion(report).Count == 0 && repository.Discussion(replacement).Count == 0 &&
            repository.DiscussionSummaries().Count == 0, "successful replacement atomically deletes old discussion and starts empty after restart");
        Reject(() => repository.EnqueueAnswer(report, "Stale report"), check, "stale report cannot receive questions");
    }

    private static void Reject(Action action, Action<bool, string> check, string name)
    {
        try
        {
            action();
        }
        catch (AnalysisConflictException) { check(true, name); return; }
        catch (ArgumentException) { check(true, name); return; }
        throw new Exception("Did not reject: " + name);
    }
}
