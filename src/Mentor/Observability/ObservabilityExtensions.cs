using Mentor.Application.Diagnostics;
using Mentor.Hosting;
using Mentor.Infrastructure.Configuration;
using Mentor.Observability.HealthChecks;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;
using Serilog.Events;
using Serilog.Formatting.Compact;

namespace Mentor.Observability;

public static class ObservabilityExtensions
{
    public static WebApplicationBuilder AddMentorObservability(this WebApplicationBuilder builder)
    {
        builder.Services.AddSerilog((provider, configuration) =>
        {
            var options = provider.GetRequiredService<IOptions<MentorInfrastructureOptions>>().Value;
            configuration.MinimumLevel.Information()
                .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
                .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Warning)
                .MinimumLevel.Override("System.Net.Http.HttpClient", LogEventLevel.Fatal)
                .Enrich.FromLogContext()
                .WriteTo.Console(new RenderedCompactJsonFormatter())
                .WriteTo.File(new RenderedCompactJsonFormatter(), Path.Combine(options.DataDirectory, "logs", "mentor-.jsonl"),
                    rollingInterval: RollingInterval.Day, fileSizeLimitBytes: 10 * 1024 * 1024,
                    rollOnFileSizeLimit: true, retainedFileCountLimit: 14);
        });

        builder.Services.AddSingleton<OperationalMetrics>();
        var endpoint = builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"];
        var telemetry = builder.Services.AddOpenTelemetry().ConfigureResource(resource => resource.AddService("Mentor"));
        telemetry.WithTracing(tracing =>
        {
            tracing.AddSource(MentorTelemetry.Name)
                .AddAspNetCoreInstrumentation(options => options.Filter = context => !IsPoll(context.Request.Path))
                .AddHttpClientInstrumentation()
                .AddProcessor(new TelemetryPrivacyProcessor());
            if (!string.IsNullOrWhiteSpace(endpoint))
                tracing.AddOtlpExporter();
        });
        telemetry.WithMetrics(metrics =>
        {
            metrics.AddMeter(MentorTelemetry.Name, "Mentor.Operations")
                .AddAspNetCoreInstrumentation().AddHttpClientInstrumentation().AddRuntimeInstrumentation();
            if (!string.IsNullOrWhiteSpace(endpoint))
                metrics.AddOtlpExporter();
        });

        builder.Services.AddHealthChecks()
            .AddCheck<DatabaseHealthCheck>("database", tags: ["ready"])
            .AddCheck<WorkerHealthCheck>("worker", tags: ["ready"])
            .AddCheck<CodexHealthCheck>("codex", tags: ["ready"]);
        return builder;
    }

    public static WebApplication MapMentorOperations(this WebApplication app)
    {
        _ = app.Services.GetRequiredService<OperationalMetrics>();
        app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
        app.MapHealthChecks("/health/ready", new HealthCheckOptions
        {
            Predicate = check => check.Tags.Contains("ready"),
            ResponseWriter = (context, report) => context.Response.WriteAsJsonAsync(new
            {
                status = report.Status.ToString(),
                checks = report.Entries.ToDictionary(entry => entry.Key,
                    entry => new { status = entry.Value.Status.ToString(), description = entry.Value.Description })
            })
        });
        app.MapGet("/api/operations", (IRepositoryDiagnostics repository, WorkerStatus worker) =>
            new
            {
                worker = worker.Snapshot(),
                queue = repository.GetQueueStatistics()
            });
        return app;
    }

    public static bool IsPoll(PathString path) => path.StartsWithSegments("/health") ||
        path == "/api/state" || path == "/api/operations";
}
