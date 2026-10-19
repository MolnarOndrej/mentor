using System.Diagnostics;
using System.Text.Json;
using Mentor.Infrastructure.Processes;

namespace Mentor.Checks.Processes;

internal static class CliProcessChecks
{
    public static async Task RunAsync(Action<bool, string> check)
    {
        var directory = Path.Combine(Path.GetTempPath(), "mentor-runner-" + Guid.NewGuid(), "space č folder");
        var runner = new CliProcessRunner();
        string[] arguments = ["", "plain", "with space", "a\"b", "trailing \\", "\\\"", "český text", "$(literal);&|"];
        var request = new CliProcessRequest(Environment.ProcessPath!, ["--runner-fixture", "echo", .. arguments],
            directory, "Unicode input žluťoučký\nline 2", TimeSpan.FromSeconds(15),
            Environment: new Dictionary<string, string?> { ["MENTOR_RUNNER_TEST"] = "value ž with spaces" });
        var result = await runner.RunAsync(request, default);
        using (var data = JsonDocument.Parse(result.StandardOutput))
        {
            var root = data.RootElement;
            check(result.ExitCode == 0 && root.GetProperty("Arguments").EnumerateArray().Select(x => x.GetString()).SequenceEqual(arguments),
                "generic runner preserves empty, Unicode, quoted and escaped arguments without a shell");
            check(root.GetProperty("Input").GetString() == request.StandardInput &&
                root.GetProperty("Directory").GetString() == directory &&
                root.GetProperty("Value").GetString() == "value ž with spaces" && result.StandardError == "stderr: český text",
                "generic runner preserves UTF-8 streams, working directory and per-run environment");
        }
        check(Environment.GetEnvironmentVariable("MENTOR_RUNNER_TEST") == null, "per-run environment does not alter the Mentor process");
        result = await runner.RunAsync(request with
        {
            Arguments = ["--runner-fixture", "exit"]
        }, default);
        check(result.ExitCode == 7 && result.StandardError.Contains("stderr", StringComparison.Ordinal),
            "generic runner returns nonzero exit and diagnostics without interpreting provider output");
        result = await runner.RunAsync(request with
        {
            Arguments = ["--runner-fixture", "duplex"],
            StandardInput = new string('i', 256 * 1024)
        }, default);
        check(result.StandardOutput == new string('o', 128 * 1024) + new string('i', 256 * 1024) &&
            result.StandardError == new string('e', 128 * 1024), "runner drains stdout and stderr while writing large stdin without deadlock");
        foreach (var mode in new[] { "stdout-limit", "stderr-limit" })
        {
            try
            {
                await runner.RunAsync(request with
                {
                    Arguments = ["--runner-fixture", mode],
                    StandardInput = "",
                    MaximumOutputCharacters = 1024,
                    MaximumErrorCharacters = 1024
                }, default);
                throw new Exception("Output limit ignored");
            }
            catch (InvalidOperationException error) when (error.Message.Contains("limit", StringComparison.Ordinal))
            {
                check(true, "runner terminates CLI immediately on " + mode);
            }
        }
        using (var cancelled = new CancellationTokenSource())
        {
            cancelled.Cancel();
            var unused = Path.Combine(directory, "must-not-start");
            try
            {
                await runner.RunAsync(request with
                {
                    WorkingDirectory = unused
                }, cancelled.Token);
                throw new Exception("Pre-cancelled invocation started");
            }
            catch (OperationCanceledException)
            {
                check(!Directory.Exists(unused), "pre-cancelled invocation has no launch side effects");
            }
        }
        await CheckConcurrentCancellationAsync(runner, request, check);
        var timeoutDirectory = Path.Combine(directory, "timeout");
        try
        {
            await runner.RunAsync(request with
            {
                WorkingDirectory = timeoutDirectory,
                Arguments = ["--runner-fixture", "hang", timeoutDirectory],
                StandardInput = "",
                Timeout = TimeSpan.FromSeconds(2)
            }, default);
            throw new Exception("Timeout ignored");
        }
        catch (TimeoutException)
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var children = await ReadProcessesAsync(timeoutDirectory, deadline.Token);
            try
            {
                await Task.WhenAll(children.Select(p => p.WaitForExitAsync(deadline.Token)));
                check(true, "timeout is distinct from cancellation and terminates the process tree");
            }
            finally { foreach (var child in children) child.Dispose(); }
        }
        if (OperatingSystem.IsWindows())
        {
            var rootDirectory = Path.Combine(directory, "root-exit");
            result = await runner.RunAsync(request with
            {
                WorkingDirectory = rootDirectory,
                Arguments = ["--runner-fixture", "root-exit", rootDirectory],
                StandardInput = ""
            }, default);
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var children = await ReadProcessesAsync(rootDirectory, deadline.Token);
            try
            {
                await Task.WhenAll(children.Select(p => p.WaitForExitAsync(deadline.Token)));
                check(result.ExitCode == 0, "root exit cleans up its surviving descendants without turning successful exit into failure");
            }
            finally { foreach (var child in children) child.Dispose(); }
        }
    }

    private static async Task CheckConcurrentCancellationAsync(CliProcessRunner runner, CliProcessRequest template, Action<bool, string> check)
    {
        var firstDirectory = Path.Combine(template.WorkingDirectory, "first");
        var secondDirectory = Path.Combine(template.WorkingDirectory, "second");
        using var firstCancellation = new CancellationTokenSource();
        using var secondCancellation = new CancellationTokenSource();
        var first = runner.RunAsync(template with
        {
            WorkingDirectory = firstDirectory,
            Arguments = ["--runner-fixture", "no-read", firstDirectory],
            StandardInput = new string('x', 1024 * 1024)
        }, firstCancellation.Token);
        var second = runner.RunAsync(template with
        {
            WorkingDirectory = secondDirectory,
            Arguments = ["--runner-fixture", "hang", secondDirectory],
            StandardInput = ""
        }, secondCancellation.Token);
        var children = new List<Process>();
        try
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            children.AddRange(await ReadProcessesAsync(firstDirectory, deadline.Token));
            children.AddRange(await ReadProcessesAsync(secondDirectory, deadline.Token));
            check(children.Count == 4 && children.All(p => !p.HasExited), "concurrent runner fixtures each start their own live process tree");
            // A raw child must also remain outside runner jobs on Windows.
            var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true };
            start.ArgumentList.Add("--lifetime-descendant");
            children.Add(Process.Start(start)!);
            firstCancellation.Cancel();
            try
            {
                await first;
                throw new Exception("Cancellation ignored");
            }
            catch (OperationCanceledException) when (firstCancellation.IsCancellationRequested) { }
            await Task.WhenAll(children.Take(2).Select(p => p.WaitForExitAsync(deadline.Token)));
            check(!second.IsCompleted && children.Skip(2).All(p => !p.HasExited),
                "cancelling blocked stdin kills only its own tree, leaving another run and unrelated processes alive");
            secondCancellation.Cancel();
            try
            {
                await second;
                throw new Exception("Cancellation ignored");
            }
            catch (OperationCanceledException) when (secondCancellation.IsCancellationRequested) { }
            await Task.WhenAll(children.Skip(2).Take(2).Select(p => p.WaitForExitAsync(deadline.Token)));
            check(!children[^1].HasExited, "disposing separate run jobs never terminates Mentor or an unrelated process");
        }
        finally
        {
            firstCancellation.Cancel();
            secondCancellation.Cancel();
            try
            {
                await Task.WhenAll(first, second);
            }
            catch (Exception) { }
            foreach (var child in children)
            {
                if (!child.HasExited)
                    child.Kill(entireProcessTree: true);
                child.Dispose();
            }
        }
    }

    private static async Task<List<Process>> ReadProcessesAsync(string directory, CancellationToken cancellationToken)
    {
        var marker = Path.Combine(directory, "processes.txt");
        while (!File.Exists(marker))
            await Task.Delay(25, cancellationToken);
        return File.ReadAllLines(marker).Select(int.Parse).Select(id =>
        {
            // A fast exit may remove the PID before this lookup (notably in the timeout assertion).
            try
            {
                return Process.GetProcessById(id);
            }
            catch (ArgumentException) { return null; }
        }).OfType<Process>().ToList();
    }
}
