namespace Mentor.Mcp;

public sealed class MentorMcpOptions
{
    public string? Token
    {
        get; set;
    }

    public bool IsValid() => Token == null || (Token.Length is >= 32 and <= 256 &&
        Token.All(c => c is >= '!' and <= '~'));
}
