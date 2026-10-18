using System.Globalization;
using Mentor.Application.Conversations;

namespace Mentor.Application.Analyses;

public static class DailyAnalysisPolicy
{
    public static string MessageKey(long conversationId, Message message) => $"{conversationId}:{message.Number}";

    public static void ValidateDay(string day)
    {
        if (!DateOnly.TryParseExact(day, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
            throw new ArgumentException("Den musí mít formát RRRR-MM-DD.");
    }

    public static List<Conversation> SelectDay(IEnumerable<Conversation> conversations, string day) => conversations
        .Where(c => c.Status == "ready" && DailySchedule.Day(DateTimeOffset.Parse(c.CreatedAt, CultureInfo.InvariantCulture)) == day)
        .Where(c => c.Messages.Count > 0)
        .OrderBy(c => c.Id)
        .ToList();

    public static List<Conversation> RestoreSnapshot(IEnumerable<Conversation> conversations, HashSet<string> keys) =>
        conversations.Select(c => c with
        {
            Messages = c.Messages.Where(m => keys.Contains(MessageKey(c.Id, m))).ToList()
        }).Where(c => c.Messages.Count > 0).OrderBy(c => c.Id).ToList();
}
