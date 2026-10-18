namespace Mentor.Application.Analyses;

public interface IConversationAnalyzer
{
    Task<string> AnalyzeAsync(AnalysisInput input, CancellationToken cancellationToken);
}
