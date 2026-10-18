using Mentor.Application.Conversations;

namespace Mentor.Application.Analyses;

public sealed class AnalysisConflictException(string message) : Exception(message);
