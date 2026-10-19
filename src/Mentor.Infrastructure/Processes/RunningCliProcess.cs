using System.Diagnostics;

namespace Mentor.Infrastructure.Processes;

internal sealed class RunningCliProcess(Process process, StreamWriter input, StreamReader output,
    StreamReader error, WindowsProcessJob? job = null) : IDisposable
{
    public Process Process { get; } = process;
    public StreamWriter Input { get; } = input;
    public StreamReader Output { get; } = output;
    public StreamReader Error { get; } = error;

    public void Kill()
    {
        if (job != null)
        {
            job.Terminate();
            return;
        }
        try
        {
            if (!Process.HasExited)
                Process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException) { } // Concurrent exit.
    }

    public void Dispose()
    {
        job?.Dispose();
        try
        {
            Input.Dispose();
        }
        // A cancelled write can leave buffered stdin. Disposing it must not mask cancellation
        // with a broken-pipe error after the child has already been terminated.
        catch (IOException) when (Process.HasExited) { }
        Output.Dispose();
        Error.Dispose();
        Process.Dispose();
    }
}
