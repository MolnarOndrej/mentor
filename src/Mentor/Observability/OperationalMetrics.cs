using System.Diagnostics.Metrics;
using Mentor.Application.Analyses;
using Mentor.Application.Diagnostics;
using Mentor.Hosting;

namespace Mentor.Observability;

public sealed class OperationalMetrics : IDisposable
{
    private readonly Meter meter = new("Mentor.Operations");

    public OperationalMetrics(IRepositoryDiagnostics repository, WorkerStatus worker, TimeProvider clock)
    {
        meter.CreateObservableGauge("mentor.queue.depth", () => ObserveQueue(repository));
        meter.CreateObservableGauge("mentor.worker.running", () => worker.Snapshot().Running ? 1 : 0);
        meter.CreateObservableGauge("mentor.worker.heartbeat.age", () =>
            worker.Snapshot().LastHeartbeat is { } heartbeat ? (clock.GetUtcNow() - heartbeat).TotalSeconds : -1, "s");
    }

    private static IEnumerable<Measurement<int>> ObserveQueue(IRepositoryDiagnostics repository)
    {
        QueueStatistics stats;
        try
        {
            stats = repository.GetQueueStatistics();
        }
        catch { return []; } // Readiness reports DB failures; metric callbacks must not crash collection.
        return
        [
            Depth(stats.PendingImports, "import", "queued"), Depth(stats.RunningImports, "import", "running"),
            Depth(stats.PendingAnalyses, "analysis", "queued"), Depth(stats.RunningAnalyses, "analysis", "running"),
            Depth(stats.FailedImports, "import", "failed"), Depth(stats.FailedAnalyses, "analysis", "failed")
        ];
    }

    private static Measurement<int> Depth(int value, string kind, string status) => new(value,
        new KeyValuePair<string, object?>("job.kind", kind), new KeyValuePair<string, object?>("job.status", status));

    public void Dispose() => meter.Dispose();
}
