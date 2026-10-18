namespace Mentor.Application.Conversations;

public record Message(string Role, string Text, DateTimeOffset CreatedAt, int Number);
