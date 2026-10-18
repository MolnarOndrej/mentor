using Mentor.Application.Analyses;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Mentor.Observability.HealthChecks;

public sealed class CodexHealthCheck(ICodexAvailability codex) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default) =>
        Task.FromResult(codex.IsAvailable() ? HealthCheckResult.Healthy("Executable found; account and model access are verified only by an actual analysis.")
            : HealthCheckResult.Unhealthy("Codex executable was not found."));
}
