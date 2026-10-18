namespace Mentor.Application.Diagnostics;

public record QueueStatistics(int PendingImports, int PendingAnalyses, int RunningImports,
    int RunningAnalyses, int FailedImports, int FailedAnalyses);
