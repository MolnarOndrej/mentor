namespace Mentor.Infrastructure.Processes;

public interface ICliProcessRunner
{
    Task<CliProcessResult> RunAsync(CliProcessRequest request, CancellationToken cancellationToken);
}
