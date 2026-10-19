using Mentor.Application.Analyses;
using Mentor.Application.Conversations;
using Mentor.Application.Discussions;

namespace Mentor.Application.Processing;

// Claims, snapshots and completion must be atomic in the persistence implementation.
public interface IWorkQueue
{
    Conversation? ClaimImport();
    void ImportDone(long id, ImportedConversation data);
    void ImportFailed(long id, string error);
    long EnqueueAnalysis(string trigger, string? day = null, string? promptOverride = null, string? expectedData = null);
    (long Id, AnalysisInput Input)? ClaimAnalysis();
    AnalysisInput RefreshAnalysisInput(long id);
    void AnalysisDone(long id, string result);
    void AnalysisFailed(long id, string error);
    List<DiscussionTurn> Discussion(long analysisId);
    List<DiscussionSummary> DiscussionSummaries();
    long EnqueueAnswer(long analysisId, string question);
    void RetryAnswer(long id);
    (long Id, string Prompt)? ClaimAnswer();
    void AnswerDone(long id, string answer);
    void AnswerFailed(long id, string error);
}
