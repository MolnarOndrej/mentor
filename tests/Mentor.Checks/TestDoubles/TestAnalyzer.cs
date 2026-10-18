using Mentor.Application.Analyses;

namespace Mentor.Checks.TestDoubles;

internal sealed class TestAnalyzer : IConversationAnalyzer
{
    public AnalysisInput? Input
    {
        get; private set;
    }
    public bool Fail
    {
        get; set;
    }

    public Task<string> AnalyzeAsync(AnalysisInput input, CancellationToken cancellationToken)
    {
        Input = input;
        return Fail ? Task.FromException<string>(new InvalidOperationException("SENSITIVE_PROVIDER_ERROR")) : Task.FromResult("Daily feedback");
    }
}
