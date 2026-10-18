using Mentor.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace Mentor.Infrastructure.Hosting;

public sealed class WorkspaceLease : IDisposable
{
    private readonly FileStream lease;

    public WorkspaceLease(IOptions<MentorInfrastructureOptions> options)
    {
        Directory.CreateDirectory(options.Value.DataDirectory);
        lease = new FileStream(Path.Combine(options.Value.DataDirectory, "instance.lock"),
            FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    }

    public void Dispose() => lease.Dispose();
}
