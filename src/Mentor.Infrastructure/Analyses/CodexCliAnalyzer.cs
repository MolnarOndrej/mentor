using System.Diagnostics;
using System.Text;
using Mentor.Application.Analyses;
using Mentor.Application.Configuration;
using Mentor.Application.Diagnostics;
using Mentor.Application.Prompts;
using Mentor.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace Mentor.Infrastructure.Analyses;

public sealed class CodexCliAnalyzer(IOptions<MentorInfrastructureOptions> options, MentorTelemetry telemetry) : IConversationAnalyzer, ICodexAvailability
{
    public bool IsAvailable()
    {
        var executable = options.Value.CodexExecutable;
        if (Path.IsPathRooted(executable))
            return File.Exists(executable);
        return (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator)
            .Any(directory => !string.IsNullOrWhiteSpace(directory) && File.Exists(Path.Combine(directory.Trim('"'), executable)));
    }
    public async Task<string> AnalyzeAsync(AnalysisInput input, CancellationToken cancellationToken)
    {
        using var activity = telemetry.Activities.StartActivity("mentor.codex");
        var settings = options.Value;
        var workDirectory = Path.Combine(settings.DataDirectory, "codex-work");
        var prompt = AnalysisPrompt.Build(input);
        // Never silently truncate: a successful batch must cover all selected conversations.
        if (prompt.Length > AnalysisPrompt.MaximumInputCharacters)
            throw new InvalidOperationException($"Denní dávka překračuje limit {AnalysisPrompt.MaximumInputCharacters} znaků. Rozbor nebyl proveden ani zkrácen.");
        Directory.CreateDirectory(workDirectory);
        var start = new ProcessStartInfo
        {
            FileName = settings.CodexExecutable,
            WorkingDirectory = workDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = new UTF8Encoding(false),
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        foreach (var arg in new[]{"exec","--ignore-user-config","--ignore-rules","--ephemeral","--skip-git-repo-check","--sandbox","read-only",
            "-c","approval_policy=\"never\"","-c","features.shell_tool=false","-c","features.apply_patch_freeform=false",
            "-c","web_search=\"disabled\"","-c","project_doc_max_bytes=0",
            "--disable","plugins","--disable","remote_plugin","--disable","skill_search",
            "--disable","skill_mcp_dependency_install","--enable","skip_host_skill_discovery",
            "--disable","shell_snapshot","--disable","request_permissions_tool","--json","-"})
            start.ArgumentList.Add(arg);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(settings.AnalysisTimeout);
        using var process = new Process { StartInfo = start };
        var started = false;
        try
        {
            started = process.Start();
            if (!started)
                throw new InvalidOperationException("Codex CLI se nepodařilo spustit.");
            var output = ReadBoundedAsync(process.StandardOutput, 8 * 1024 * 1024, process, timeout.Token);
            var error = ReadBoundedAsync(process.StandardError, 1024 * 1024, process, timeout.Token);
            await process.StandardInput.WriteAsync(prompt.AsMemory(), timeout.Token);
            process.StandardInput.Close();
            await process.WaitForExitAsync(timeout.Token);
            var streams = await Task.WhenAll(output, error);
            var stdout = streams[0];
            var stderr = streams[1];
            if (process.ExitCode != 0)
                throw new InvalidOperationException($"Codex CLI skončil s kódem {process.ExitCode}. {Tail(stderr)} {Tail(stdout)}");
            var result = CodexResponseParser.Parse(stdout, stderr);
            activity?.SetStatus(ActivityStatusCode.Ok);
            return result;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            activity?.SetStatus(ActivityStatusCode.Error);
            activity?.SetTag("error.type", nameof(TimeoutException));
            throw new TimeoutException($"Codex CLI nedokončil analýzu do {settings.AnalysisTimeout.TotalSeconds:g} sekund.");
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            activity?.SetStatus(ActivityStatusCode.Error);
            activity?.SetTag("error.type", error.GetType().Name);
            throw;
        }
        finally
        {
            if (started)
                TryKill(process);
        }
    }

    private static async Task<string> ReadBoundedAsync(StreamReader reader, int limit, Process process,
        CancellationToken cancellationToken)
    {
        var text = new StringBuilder();
        var buffer = new char[4096];
        int read;
        while ((read = await reader.ReadAsync(buffer.AsMemory(), cancellationToken)) > 0)
        {
            if (text.Length + read > limit)
            {
                TryKill(process);
                throw new InvalidOperationException("Výstup Codex CLI překročil bezpečný limit velikosti.");
            }
            text.Append(buffer, 0, read);
        }
        return text.ToString();
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException) { } // The process may have exited between the check and kill.
    }
    private static string Tail(string value) => value.Length <= 3000 ? value : value[^3000..];
}
