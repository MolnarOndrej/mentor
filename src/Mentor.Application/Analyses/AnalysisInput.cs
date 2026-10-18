using Mentor.Application.Conversations;

namespace Mentor.Application.Analyses;

public record AnalysisInput(string Day, string Prompt, List<Conversation> Conversations, string? FullPrompt = null);
