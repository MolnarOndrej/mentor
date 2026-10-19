namespace Mentor.Infrastructure.Processes;

public sealed record CliProcessResult(int ExitCode, string StandardOutput, string StandardError);
