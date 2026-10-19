using Mentor.Application.Analyses;

namespace Mentor.Checks.TestDoubles;

internal sealed class CancellingAnalyzer(CancellationTokenSource stopping) : IConversationAnalyzer
{
    public Task<string> AnalyzeAsync(AnalysisInput input, CancellationToken cancellationToken)
    {
        stopping.Cancel();
        return Task.FromResult("Result arriving during shutdown");
    }
}
