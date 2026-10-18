using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Mentor.Application.Diagnostics;

public sealed class MentorTelemetry : IDisposable
{
    public const string Name = "Mentor";
    public ActivitySource Activities { get; } = new(Name);
    private readonly Meter meter = new(Name);
    private readonly Counter<long> jobs;
    private readonly Histogram<double> duration;

    public MentorTelemetry()
    {
        jobs = meter.CreateCounter<long>("mentor.jobs", description: "Finished background jobs by kind and outcome");
        duration = meter.CreateHistogram<double>("mentor.job.duration", "s");
    }

    public void RecordJob(string kind, string outcome, double seconds)
    {
        var tags = new TagList { { "job.kind", kind }, { "job.outcome", outcome } };
        jobs.Add(1, tags);
        duration.Record(seconds, tags);
    }

    public void Dispose()
    {
        Activities.Dispose();
        meter.Dispose();
    }
}
