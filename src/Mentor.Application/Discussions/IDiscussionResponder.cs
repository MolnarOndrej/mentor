namespace Mentor.Application.Discussions;

public interface IDiscussionResponder
{
    Task<string> RespondAsync(string prompt, CancellationToken cancellationToken);
}
