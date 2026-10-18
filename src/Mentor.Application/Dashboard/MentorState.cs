using Mentor.Application.Analyses;
using Mentor.Application.Configuration;
using Mentor.Application.Conversations;

namespace Mentor.Application.Dashboard;

public record MentorState(List<ConversationSummary> Conversations, List<AnalysisSummary> Analyses,
    Settings Settings, string Today, string DailyTime = "22:00", string TimeZone = "Europe/Prague");
