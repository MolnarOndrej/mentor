namespace Mentor.Hosting;

public record WorkerSnapshot(bool Running, DateTimeOffset? LastHeartbeat, DateTimeOffset? JobStarted);
