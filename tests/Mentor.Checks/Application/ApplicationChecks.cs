using System.Diagnostics;
using System.Diagnostics.Metrics;
using Mentor.Application.Analyses;
using Mentor.Application.Configuration;
using Mentor.Application.Conversations;
using Mentor.Application.Dashboard;
using Mentor.Application.Diagnostics;
using Mentor.Application.Processing;
using Mentor.Checks.Persistence;
using Mentor.Checks.TestDoubles;
using Mentor.Hosting;
using Mentor.Infrastructure.Configuration;
using Mentor.Observability;
using Mentor.Observability.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Mentor.Checks.Application;

internal static class ApplicationChecks
{
    public static async Task RunAsync(Action<bool, string> check)
    {
        var clock = new TestClock(DateTimeOffset.Parse("2026-10-18T10:00:00Z"));
        var directory = Path.Combine(Path.GetTempPath(), "mentor-application-" + Guid.NewGuid());
        var repository = TestRepository.Create(Path.Combine(directory, "mentor.db"),
            "Editable instructions, SENSITIVE_PROMPT\n{{data}}", clock);
        var importer = new TestImporter(clock);
        var analyzer = new TestAnalyzer();
        var logger = new TestLogger();
        var completedActivities = new List<Activity>();
        using var activities = new ActivityListener
        {
            ShouldListenTo = source => source.Name == MentorTelemetry.Name,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = completedActivities.Add
        };
        ActivitySource.AddActivityListener(activities);
        var outcomes = new List<string>();
        using var meter = new MeterListener();
        meter.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == MentorTelemetry.Name)
                listener.EnableMeasurementEvents(instrument);
        };
        meter.SetMeasurementEventCallback<long>((instrument, _, tags, _) =>
        {
            if (instrument.Name != "mentor.jobs")
                return;
            foreach (var tag in tags)
                if (tag.Key == "job.outcome")
                    outcomes.Add((string)tag.Value!);
        });
        meter.Start();
        using var telemetry = new MentorTelemetry();
        var service = new MentorService(repository, repository, repository, repository, importer, clock);
        var processor = new WorkProcessor(repository, repository, importer, analyzer, clock, telemetry, logger);

        const string url = "https://chatgpt.com/share/6ad4ab23-61e0-83eb-8533-14def95d3491";
        service.AddConversation(url);
        service.AddConversation(url[..^1] + "2");
        check(await processor.ProcessNextAsync(default) && await processor.ProcessNextAsync(default),
            "application imports through injected provider without HTTP or CLI");
        var rendered = service.RenderPrompt("2026-10-18", null);
        var id = service.AnalyzeDay("2026-10-18", rendered.Prompt, rendered.Data);
        try
        {
            service.AnalyzeDay("2026-10-18", "Different reviewed instructions\n{{data}}", rendered.Data);
            throw new Exception("Concurrent reviewed prompt was silently ignored.");
        }
        catch (AnalysisConflictException)
        {
            check(true, "concurrent different reviewed prompt receives conflict instead of false acceptance");
        }
        await processor.ProcessNextAsync(default);
        check(service.GetAnalysis(id)?.Status == "completed" && analyzer.Input?.Conversations.Count == 2,
            "business service and worker produce one daily analysis of N conversations");
        check(analyzer.Input?.FullPrompt == rendered.Prompt,
            "layered pipeline preserves exact reviewed prompt");
        check(service.GetState().Conversations.Sum(c => c.Days.Sum(d => d.AnalyzedCount)) == 4,
            "read model reports successful message coverage");

        service.AddConversation(url[..^1] + "3");
        await processor.ProcessNextAsync(default);
        analyzer.Fail = true;
        var failedId = service.AnalyzeDay("2026-10-18", null, null);
        await processor.ProcessNextAsync(default);
        check(service.GetAnalysis(failedId)?.Status == "failed" && service.GetAnalysis(id)?.Status == "completed",
            "failed provider preserves prior daily report");
        check(service.GetState().Conversations.Sum(c => c.Days.Sum(d => d.MessageCount - d.AnalyzedCount)) == 2,
            "failed run leaves newly imported messages pending");
        check(outcomes.Count(x => x == "failed") == 1 && outcomes.Count(x => x == "completed") == 4,
            "job metrics distinguish successful and failed work");
        check(completedActivities.Count == 5 && completedActivities.Any(a => a.Status == ActivityStatusCode.Error),
            "application spans capture actual failure state");
        check(!logger.Messages.Any(m => m.Contains("SENSITIVE", StringComparison.Ordinal)) &&
              !completedActivities.SelectMany(a => a.TagObjects).Any(t => t.Value?.ToString()?.Contains("SENSITIVE", StringComparison.Ordinal) == true),
            "logs and trace tags do not expose private prompt or exception text");
        check(repository.GetQueueStatistics() is { PendingImports: 0, PendingAnalyses: 0, FailedAnalyses: 1 },
            "monitoring reflects durable queue state");

        var state = new WorkerStatus(clock);
        var health = new WorkerHealthCheck(state, clock, Options.Create(new MentorInfrastructureOptions()));
        check((await health.CheckHealthAsync(new())).Status == HealthStatus.Unhealthy,
            "stopped worker is not ready");
        state.Start();
        check((await health.CheckHealthAsync(new())).Status == HealthStatus.Healthy,
            "live worker heartbeat is ready");
        clock.Advance(TimeSpan.FromMinutes(1));
        check((await health.CheckHealthAsync(new())).Status == HealthStatus.Unhealthy,
            "stale worker heartbeat is unhealthy");
        state.BeginJob();
        check((await health.CheckHealthAsync(new())).Status == HealthStatus.Healthy,
            "legitimate long-running analysis does not falsely fail heartbeat health");
        clock.Advance(TimeSpan.FromMinutes(11));
        check((await health.CheckHealthAsync(new())).Status == HealthStatus.Unhealthy,
            "job beyond configured timeout fails readiness");
        check((await new DatabaseHealthCheck(repository).CheckHealthAsync(new())).Status == HealthStatus.Healthy,
            "database readiness verifies schema and settings");

        using var redacted = new Activity("HTTP request").Start();
        redacted.SetTag("url.full", "https://chatgpt.com/share/SENSITIVE");
        redacted.SetTag("url.query", "token=SENSITIVE");
        new TelemetryPrivacyProcessor().OnEnd(redacted);
        check(redacted.GetTagItem("url.full") == null && redacted.GetTagItem("url.query") == null,
            "HTTP trace export removes private URLs and queries");
        check(!typeof(MentorService).Assembly.GetReferencedAssemblies().Any(a =>
                a.Name!.Contains("Infrastructure", StringComparison.Ordinal) ||
                a.Name.Contains("Sqlite", StringComparison.Ordinal) || a.Name.Contains("AspNetCore", StringComparison.Ordinal)),
            "application assembly cannot depend on persistence or web framework");
    }
}
