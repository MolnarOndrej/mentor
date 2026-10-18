namespace Mentor.Hosting;

public sealed class WorkerStatus(TimeProvider clock)
{
    private readonly object gate = new();
    private DateTimeOffset? heartbeat;
    private DateTimeOffset? jobStarted;
    private bool running;

    public void Start()
    {
        lock (gate)
        {
            running = true;
            heartbeat = clock.GetUtcNow();
        }
    }
    public void Heartbeat()
    {
        lock (gate)
            heartbeat = clock.GetUtcNow();
    }
    public void BeginJob()
    {
        lock (gate)
            jobStarted = clock.GetUtcNow();
    }
    public void EndJob()
    {
        lock (gate)
        {
            jobStarted = null;
            heartbeat = clock.GetUtcNow();
        }
    }
    public void Stop()
    {
        lock (gate)
        {
            running = false;
            jobStarted = null;
        }
    }

    public WorkerSnapshot Snapshot()
    {
        lock (gate)
            return new(running, heartbeat, jobStarted);
    }
}
