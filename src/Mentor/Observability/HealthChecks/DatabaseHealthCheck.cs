using Mentor.Application.Diagnostics;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Mentor.Observability.HealthChecks;

public sealed class DatabaseHealthCheck(IRepositoryDiagnostics database) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            database.Probe();
            return Task.FromResult(HealthCheckResult.Healthy());
        }
        catch { return Task.FromResult(HealthCheckResult.Unhealthy("Database is unavailable.")); }
    }
}
