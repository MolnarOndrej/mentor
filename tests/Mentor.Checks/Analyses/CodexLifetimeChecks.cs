using System.Diagnostics;
using Mentor.Application.Diagnostics;
using Mentor.Checks.Persistence;
using Mentor.Checks.TestDoubles;
using Mentor.Infrastructure.Analyses;
using Mentor.Infrastructure.Configuration;
using Mentor.Infrastructure.Processes;
using Microsoft.Extensions.Options;

namespace Mentor.Checks.Analyses;

internal static class CodexLifetimeChecks
{
    public static async Task RunAsync(Action<bool, string> check)
    {
        if (!OperatingSystem.IsWindows())
            return;
        var directory = Path.Combine(Path.GetTempPath(), "mentor-lifetime-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        var start = new ProcessStartInfo(Environment.ProcessPath!)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true
        };
        start.ArgumentList.Add("--lifetime-host");
        start.ArgumentList.Add(directory);
        start.Environment["MENTOR_FAKE_MODE"] = "hang";
        start.Environment["MENTOR_LIFETIME_DIRECTORY"] = directory;
        using var host = Process.Start(start)!;
        var hostError = host.StandardError.ReadToEndAsync();
        var children = new List<Process>();
        try
        {
            using var readyTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            var marker = Path.Combine(directory, "processes.txt");
            while (!File.Exists(marker))
            {
                if (host.HasExited)
                    throw new Exception($"Lifetime fixture exited with {host.ExitCode}: {await hostError}");
                await Task.Delay(25, readyTimeout.Token);
            }
            // Keep real process handles: PID reuse cannot turn this into a false positive.
            children.AddRange(File.ReadAllLines(marker).Select(id => Process.GetProcessById(int.Parse(id))));
            check(children.Count == 2 && children.All(p => !p.HasExited), "Codex and its descendant are running before host termination");
            var arguments = File.ReadAllLines(Path.Combine(directory, "arguments.txt"));
            check(arguments.Contains("--ephemeral") && !arguments.Contains("resume"),
                "generation remains ephemeral without session resumption");
            host.Kill(); // Deliberately NOT Kill(entireProcessTree: true): Windows must do the cleanup.
            using var exitTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await host.WaitForExitAsync(exitTimeout.Token);
            await Task.WhenAll(children.Select(p => p.WaitForExitAsync(exitTimeout.Token)));
            check(children.All(p => p.HasExited), "forced Mentor termination kills Codex and its descendant");

            var clock = new TestClock(LocalTime.At("2026-10-18T12:00:00"));
            var repository = TestRepository.Create(Path.Combine(directory, "mentor.db"), "{{data}}", clock);
            var attempts = repository.Analyses();
            check(attempts.Count == 2 && attempts[0] is { Status: "failed", CompletedAt: not null, Error: not null } &&
                attempts[1] is { Status: "completed", Result: "Original report" } && repository.ClaimAnalysis() == null,
                "restart marks killed generation interrupted and preserves the last successful report");
            var retry = repository.EnqueueAnalysis("manual", "2026-10-18");
            var batch = repository.ClaimAnalysis()!.Value;
            using var telemetry = new MentorTelemetry();
            var analyzer = new CodexCliAnalyzer(Options.Create(new MentorInfrastructureOptions
            {
                CodexExecutable = Environment.ProcessPath!,
                DataDirectory = directory
            }), telemetry, new CliProcessRunner());
            repository.AnalysisDone(retry, await analyzer.AnalyzeAsync(batch.Input, default));
            check(repository.Analyses() is [{ Status: "completed", Result: "Testovací rozbor" }],
                "interrupted generation can be retried and replaces the original only after success");
        }
        finally
        {
            if (!host.HasExited)
                host.Kill(entireProcessTree: true);
            foreach (var child in children)
            {
                if (!child.HasExited)
                    child.Kill(entireProcessTree: true);
                child.Dispose();
            }
        }
    }
}
