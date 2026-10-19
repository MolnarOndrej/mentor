using System.Diagnostics;

namespace Mentor.Infrastructure.Processes;

internal static class PortableCliProcessLauncher
{
    public static RunningCliProcess Start(ProcessStartInfo start)
    {
        var process = new Process { StartInfo = start };
        try
        {
            if (!process.Start())
                throw new InvalidOperationException("Proces CLI se nepodařilo spustit.");
            return new RunningCliProcess(process, process.StandardInput, process.StandardOutput, process.StandardError);
        }
        catch
        {
            process.Dispose();
            throw;
        }
    }
}
