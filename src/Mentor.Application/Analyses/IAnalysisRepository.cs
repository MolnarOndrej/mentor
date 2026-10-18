namespace Mentor.Application.Analyses;

public interface IAnalysisRepository
{
    List<Analysis> Analyses();
    Analysis? FindAnalysis(long id);
    string? FullPrompt(long id);
}
