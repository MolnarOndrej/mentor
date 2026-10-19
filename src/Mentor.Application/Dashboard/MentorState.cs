using Mentor.Application.Analyses;
using Mentor.Application.Configuration;
using Mentor.Application.Conversations;
using Mentor.Application.Discussions;

namespace Mentor.Application.Dashboard;

public record MentorState(List<ConversationSummary> Conversations, List<AnalysisSummary> Analyses,
    Settings Settings, string Today, string TimeZone, List<string> ChangedDays, List<DiscussionSummary> Discussions);
