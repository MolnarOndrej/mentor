using System.Diagnostics;
using Mentor.Application.Analyses;
using Mentor.Application.Configuration;
using Mentor.Application.Conversations;
using Mentor.Application.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Mentor.Application.Processing;

// One iteration is independently testable; scheduling the loop belongs to the host.
public sealed class WorkProcessor(
    IWorkQueue queue,
    ISettingsRepository settings,
    IConversationImporter importer,
    IConversationAnalyzer analyzer,
    TimeProvider clock,
    MentorTelemetry telemetry,
    ILogger<WorkProcessor> logger)
{
    public async Task<bool> ProcessNextAsync(CancellationToken cancellationToken)
    {
        var due = DailySchedule.DueDate(clock.GetUtcNow(), settings.Settings().LastDailyDate);
        if (due != null)
            queue.EnqueueAnalysis("daily", due);

        var conversation = queue.ClaimImport();
        if (conversation != null)
        {
            await ExecuteJobAsync("import", conversation.Id,
                async () => queue.ImportDone(conversation.Id, await importer.ImportAsync(conversation.Url, DateTimeOffset.Parse(conversation.CreatedAt), cancellationToken)),
                error => queue.ImportFailed(conversation.Id, error), cancellationToken);
            return true;
        }

        var batch = queue.ClaimAnalysis();
        if (batch == null)
            return false;

        await ExecuteJobAsync("analysis", batch.Value.Id, async () =>
        {
            var input = batch.Value.Input;
            var result = input.Conversations.Count == 0
                ? "Pro tento den nejsou uložené žádné zprávy. Neúspěšné importy nejsou součástí rozboru."
                : await analyzer.AnalyzeAsync(input, cancellationToken);
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
                fail("Aplikace byla během rozboru ukončena. Spusťte nový rozbor.");
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
