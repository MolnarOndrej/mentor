using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace Mentor.Infrastructure.Processes;

public sealed class CliProcessRunner : ICliProcessRunner
{
    public async Task<CliProcessResult> RunAsync(CliProcessRequest request, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Executable);
        if (request.Timeout <= TimeSpan.Zero || request.Timeout.TotalMilliseconds > uint.MaxValue - 1 ||
            request.MaximumOutputCharacters <= 0 || request.MaximumErrorCharacters <= 0)
            throw new ArgumentOutOfRangeException(nameof(request), "Timeout a limity výstupu musí být kladné a podporované.");
        cancellationToken.ThrowIfCancellationRequested();
        Directory.CreateDirectory(request.WorkingDirectory);
        var start = new ProcessStartInfo
        {
            FileName = request.Executable,
            WorkingDirectory = request.WorkingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = new UTF8Encoding(false),
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        foreach (var argument in request.Arguments)
            start.ArgumentList.Add(argument);
        if (request.Environment != null)
            foreach (var (key, value) in request.Environment)
                start.Environment[key] = value;

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(request.Timeout);
        using var process = OperatingSystem.IsWindows()
            ? WindowsCliProcessLauncher.Start(start)
            : PortableCliProcessLauncher.Start(start);
        // Killing also unblocks pending pipe I/O, including writes to a CLI that stopped reading stdin.
        using var cancellation = timeout.Token.Register(() =>
        {
            try
            {
                process.Kill();
            }
            // Never throw from a timer callback. The awaited cleanup below retries and reports a failure.
            catch (Win32Exception) { }
        });
        var output = ReadBoundedAsync(process.Output, request.MaximumOutputCharacters, process, timeout.Token);
        var error = ReadBoundedAsync(process.Error, request.MaximumErrorCharacters, process, timeout.Token);
        var input = WriteInputAsync(process, request.StandardInput, timeout.Token);
        var exit = WaitForExitAsync(process);
        var operations = Task.WhenAll(output, error, input, exit);
        try
        {
            await operations.WaitAsync(timeout.Token);
            timeout.Token.ThrowIfCancellationRequested();
            return new CliProcessResult(process.Process.ExitCode, await output, await error);
        }
        catch (Exception) when (timeout.IsCancellationRequested)
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw new TimeoutException($"Proces CLI nedokončil běh do {request.Timeout.TotalSeconds:g} sekund.");
        }
        finally
        {
            // Await every pipe task before releasing its stream, and observe failures after cancellation.
            process.Kill();
            try
            {
                await operations;
            }
            catch (Exception) { /* The original failure or cancellation is propagated above. */ }
        }
    }

    private static async Task WriteInputAsync(RunningCliProcess process, string input, CancellationToken cancellationToken)
    {
        try
        {
            await process.Input.WriteAsync(input.AsMemory(), cancellationToken);
            process.Input.Close();
        }
        catch
        {
            process.Kill();
            throw;
        }
    }

    private static async Task WaitForExitAsync(RunningCliProcess process)
    {
        await process.Process.WaitForExitAsync();
        // On Windows this also terminates descendants that outlive the root and keep its pipes open.
        process.Kill();
    }

    private static async Task<string> ReadBoundedAsync(StreamReader reader, int limit, RunningCliProcess process,
        CancellationToken cancellationToken)
    {
        var text = new StringBuilder();
        var buffer = new char[4096];
        int read;
        try
        {
            while ((read = await reader.ReadAsync(buffer.AsMemory(), cancellationToken)) > 0)
            {
                if (text.Length + read > limit)
                    throw new InvalidOperationException("Výstup procesu CLI překročil bezpečný limit velikosti.");
                text.Append(buffer, 0, read);
            }
            return text.ToString();
        }
        catch
        {
            process.Kill();
            throw;
        }
    }
}
