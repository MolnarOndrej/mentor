namespace Mentor.Application.Conversations;

public record RefreshDayResult(int CheckedCount, int UpdatedCount, List<string> Messages);
