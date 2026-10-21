using System.Diagnostics;
using Mentor.Application.Analyses;
using Mentor.Application.Configuration;
using Mentor.Application.Conversations;
using Mentor.Application.Diagnostics;
using Mentor.Application.Discussions;
using Microsoft.Extensions.Logging;

namespace Mentor.Application.Processing;

// One iteration is independently testable; scheduling the loop belongs to the host.
public sealed class WorkProcessor(
    IWorkQueue queue,
    IConversationImporter importer,
    IConversationAnalyzer analyzer,
    TimeProvider clock,
    MentorTelemetry telemetry,
    ILogger<WorkProcessor> logger,
    ConversationRefresher refresher,
    IDiscussionResponder? responder = null)
{
    public async Task<bool> ProcessNextAsync(Settings current, CancellationToken cancellationToken)
    {
        var due = DailySchedule.DueDate(clock.GetUtcNow(), current.LastDailyDate, current.Runtime.DailyTime);
        if (due != null)
            queue.EnqueueAnalysis("daily", due);

        var conversation = queue.ClaimImport();
        if (conversation != null)
        {
            await ExecuteJobAsync("import", conversation.Id,
                async () => queue.ImportDone(conversation.Id, await importer.ImportAsync(conversation.Url!, cancellationToken)),
                error => queue.ImportFailed(conversation.Id, error), cancellationToken);
            return true;
        }

        var answer = queue.ClaimAnswer();
        if (answer != null)
        {
            await ExecuteJobAsync("answer", answer.Value.Id, async () =>
            {
                var result = await (responder ?? throw new InvalidOperationException("Generování odpovědí není dostupné."))
                    .RespondAsync(answer.Value.Prompt, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                queue.AnswerDone(answer.Value.Id, result);
            }, error => queue.AnswerFailed(answer.Value.Id, error), cancellationToken);
            return true;
        }

        var batch = queue.ClaimAnalysis();
        if (batch == null)
            return false;

        await ExecuteJobAsync("analysis", batch.Value.Id, async () =>
        {
            var input = batch.Value.Input;
            if (input.Day == DailySchedule.Day(clock.GetUtcNow()))
            {
                await refresher.RefreshDayAsync(input.Day, cancellationToken);
                input = queue.RefreshAnalysisInput(batch.Value.Id);
            }
            if (input.Conversations.Count == 0)
                throw new InvalidOperationException("Pro tento den nejsou analyzovatelné zprávy.");
            var result = await analyzer.AnalyzeAsync(input, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            queue.AnalysisDone(batch.Value.Id, result);
        }, error => queue.AnalysisFailed(batch.Value.Id, error), cancellationToken);
        return true;
    }

    private async Task ExecuteJobAsync(string kind, long id, Func<Task> execute, Action<string> fail,
        CancellationToken cancellationToken)
    {
        using var activity = telemetry.Activities.StartActivity($"mentor.{kind}");
        activity?.SetTag("job.id", id);
        var started = Stopwatch.GetTimestamp();
        var outcome = "completed";
        using var scope = logger.BeginScope(new Dictionary<string, object> { ["JobKind"] = kind, ["JobId"] = id });
        logger.LogInformation("Background job started");
        try
        {
            await execute();
            activity?.SetStatus(ActivityStatusCode.Ok);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            outcome = "cancelled";
            if (kind == "analysis")
                fail("Rozbor byl přerušen ukončením aplikace. Spusťte nový rozbor.");
            if (kind == "answer")
                fail("Odpověď byla přerušena ukončením aplikace. Zkuste ji znovu.");
            // An interrupted import is recovered from its durable claim on restart.
            throw;
        }
        catch (Exception error)
        {
            outcome = "failed";
            fail(error.Message);
            activity?.SetStatus(ActivityStatusCode.Error);
            activity?.SetTag("error.type", error.GetType().Name);
            // Detailed user-facing errors stay in the private DB, never in telemetry.
            logger.LogWarning("Background job failed with {ErrorType}", error.GetType().Name);
        }
        finally
        {
            var seconds = Stopwatch.GetElapsedTime(started).TotalSeconds;
            telemetry.RecordJob(kind, outcome, seconds);
            logger.LogInformation("Background job finished with {Outcome} after {ElapsedSeconds:F3}s", outcome, seconds);
        }
    }
}
