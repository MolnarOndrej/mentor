using Mentor.Application.Discussions;

namespace Mentor.Checks.TestDoubles;

internal sealed class CancellingResponder(CancellationTokenSource stopping) : IDiscussionResponder
{
    public Task<string> RespondAsync(string prompt, CancellationToken cancellationToken)
    {
        stopping.Cancel();
        return Task.FromResult("Late answer");
    }
}
