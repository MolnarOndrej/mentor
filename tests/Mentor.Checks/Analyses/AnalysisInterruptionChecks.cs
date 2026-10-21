using Mentor.Application.Conversations;
using Mentor.Application.Diagnostics;
using Mentor.Application.Processing;
using Mentor.Checks.Persistence;
using Mentor.Checks.TestDoubles;

namespace Mentor.Checks.Analyses;

internal static class AnalysisInterruptionChecks
{
    public static async Task RunAsync(Action<bool, string> check)
    {
        const string day = "2026-10-18";
        // Use an archived day so the test reaches the analyzer without an HTTP refresh.
        var clock = new TestClock(LocalTime.At("2026-10-19T12:00:00"));
        var path = Path.Combine(Path.GetTempPath(), "mentor-interruption-" + Guid.NewGuid(), "mentor.db");
        var repository = TestRepository.Create(path, "{{data}}", new TestClock(LocalTime.At(day + "T12:00:00")));
        var conversation = repository.Add("https://chatgpt.com/share/00000000-0000-0000-0000-000000000001").Conversation;
        repository.ClaimImport();
        repository.ImportDone(conversation.Id, new("Test", [new("user", "Message", 1)]));
        var original = repository.EnqueueAnalysis("manual", day);
        repository.ClaimAnalysis();
        repository.AnalysisDone(original, "Original report");
        var interrupted = repository.EnqueueAnalysis("manual", day);
        using var stopping = new CancellationTokenSource();
        using var telemetry = new MentorTelemetry();
        var importer = new TestImporter();
        var processor = new WorkProcessor(repository, importer, new CancellingAnalyzer(stopping),
            clock, telemetry, new TestLogger(), new ConversationRefresher(repository, importer, clock));
        try
        {
            await processor.ProcessNextAsync(repository.Settings(), stopping.Token);
            throw new Exception("Shutdown cancellation ignored");
        }
        catch (OperationCanceledException) when (stopping.IsCancellationRequested) { }
        check(repository.FindAnalysis(interrupted) is { Status: "failed", CompletedAt: not null, Result: null } &&
            repository.FindAnalysis(original)?.Result == "Original report",
            "graceful shutdown marks generation interrupted even if a provider returns a late result");
        repository = TestRepository.Create(path, "{{data}}", clock);
        check(repository.FindAnalysis(interrupted)?.Error?.Contains("přerušen", StringComparison.Ordinal) == true &&
            repository.EnqueueAnalysis("manual", day) > interrupted,
            "gracefully interrupted generation remains retryable after restart");
    }
}
