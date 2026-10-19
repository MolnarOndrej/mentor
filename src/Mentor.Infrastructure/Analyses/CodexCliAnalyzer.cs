using System.Diagnostics;
using Mentor.Application.Analyses;
using Mentor.Application.Diagnostics;
using Mentor.Application.Prompts;
using Mentor.Infrastructure.Configuration;
using Mentor.Infrastructure.Processes;
using Microsoft.Extensions.Options;

namespace Mentor.Infrastructure.Analyses;

public sealed class CodexCliAnalyzer(IOptions<MentorInfrastructureOptions> options, MentorTelemetry telemetry,
    ICliProcessRunner runner) : IConversationAnalyzer, ICodexAvailability
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
        string[] arguments = ["exec","--ignore-user-config","--ignore-rules","--ephemeral","--skip-git-repo-check","--sandbox","read-only",
            "-c","approval_policy=\"never\"","-c","features.shell_tool=false","-c","features.apply_patch_freeform=false",
            "-c","web_search=\"disabled\"","-c","project_doc_max_bytes=0",
            "--disable","plugins","--disable","remote_plugin","--disable","skill_search",
            "--disable","skill_mcp_dependency_install","--enable","skip_host_skill_discovery",
            "--disable","shell_snapshot","--disable","request_permissions_tool","--json","-"];
        try
        {
            var execution = await runner.RunAsync(new CliProcessRequest(settings.CodexExecutable, arguments,
                workDirectory, prompt, settings.AnalysisTimeout), cancellationToken);
            if (execution.ExitCode != 0)
                throw new InvalidOperationException($"Codex CLI skončil s kódem {execution.ExitCode}. {Tail(execution.StandardError)} {Tail(execution.StandardOutput)}");
            var result = CodexResponseParser.Parse(execution.StandardOutput, execution.StandardError);
            activity?.SetStatus(ActivityStatusCode.Ok);
            return result;
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            activity?.SetStatus(ActivityStatusCode.Error);
            activity?.SetTag("error.type", error.GetType().Name);
            throw;
        }
    }
    private static string Tail(string value) => value.Length <= 3000 ? value : value[^3000..];
}
