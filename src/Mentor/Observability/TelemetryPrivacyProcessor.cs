using System.Diagnostics;
using Mentor.Application.Conversations;
using OpenTelemetry;

namespace Mentor.Observability;

// HTTP instrumentation must never export shared conversation IDs or URL query strings.
public sealed class TelemetryPrivacyProcessor : BaseProcessor<Activity>
{
    public override void OnEnd(Activity activity)
    {
        foreach (var key in new[] { "url.full", "url.path", "url.query", "http.url", "http.target" })
            activity.SetTag(key, null);
    }
}
