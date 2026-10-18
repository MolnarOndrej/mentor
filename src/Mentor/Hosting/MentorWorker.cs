using Mentor.Application.Processing;
using Mentor.Application.Configuration;

namespace Mentor.Hosting;

public sealed class MentorWorker(IServiceScopeFactory scopeFactory, WorkerStatus status,
    TimeProvider clock, ILogger<MentorWorker> logger, ISettingsRepository settings) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        status.Start();
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                status.Heartbeat();
                var started = clock.GetTimestamp();
                var pollSeconds = new RuntimeSettings().PollIntervalSeconds;
                try
                {
                    pollSeconds = settings.Settings().Runtime.PollIntervalSeconds;
                    await using var scope = scopeFactory.CreateAsyncScope();
                    var processor = scope.ServiceProvider.GetRequiredService<WorkProcessor>();
                    status.BeginJob();
                    var processed = await processor.ProcessNextAsync(stoppingToken);
                    status.EndJob();
                    if (processed)
                        continue;
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception error)
                {
                    status.EndJob();
                    logger.LogError("Worker iteration failed with {ErrorType} after {ElapsedSeconds:F3}s",
                        error.GetType().Name, clock.GetElapsedTime(started).TotalSeconds);
                }

                status.Heartbeat(pollSeconds);
                await Task.Delay(TimeSpan.FromSeconds(pollSeconds), clock, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        finally { status.Stop(); }
    }
}
