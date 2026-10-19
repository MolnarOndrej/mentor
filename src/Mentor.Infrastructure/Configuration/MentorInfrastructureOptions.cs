namespace Mentor.Infrastructure.Configuration;

public sealed class MentorInfrastructureOptions
{
    public string DataDirectory { get; set; } = "Data";
    public string CodexExecutable { get; set; } = OperatingSystem.IsWindows() ? "codex.exe" : "codex";
    public TimeSpan AnalysisTimeout { get; set; } = TimeSpan.FromMinutes(10);
    public TimeSpan ImportTimeout { get; set; } = TimeSpan.FromSeconds(45);
    public int MaxImportBytes { get; set; } = 8 * 1024 * 1024;
}
