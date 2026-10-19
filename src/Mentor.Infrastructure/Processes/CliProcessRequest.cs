namespace Mentor.Infrastructure.Processes;

public sealed record CliProcessRequest(
    string Executable,
    IReadOnlyList<string> Arguments,
    string WorkingDirectory,
    string StandardInput,
    TimeSpan Timeout,
    int MaximumOutputCharacters = 8 * 1024 * 1024,
    int MaximumErrorCharacters = 1024 * 1024,
    IReadOnlyDictionary<string, string?>? Environment = null);
