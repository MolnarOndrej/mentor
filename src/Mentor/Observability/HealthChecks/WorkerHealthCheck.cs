using Mentor.Hosting;
using Mentor.Infrastructure.Configuration;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace Mentor.Observability.HealthChecks;

public sealed class WorkerHealthCheck(WorkerStatus status, TimeProvider clock,
    IOptions<MentorInfrastructureOptions> options) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var snapshot = status.Snapshot();
        var now = clock.GetUtcNow();
        var maxJobDuration = options.Value.AnalysisTimeout > options.Value.ImportTimeout
            ? options.Value.AnalysisTimeout : options.Value.ImportTimeout;
        var healthy = snapshot.Running &&
            (snapshot.JobStarted is { } started
                ? now - started < maxJobDuration + TimeSpan.FromSeconds(30)
                : snapshot.LastHeartbeat is { } heartbeat && now - heartbeat < TimeSpan.FromSeconds(30));
        return Task.FromResult(healthy ? HealthCheckResult.Healthy() : HealthCheckResult.Unhealthy("Worker is stopped or unresponsive."));
    }
}
