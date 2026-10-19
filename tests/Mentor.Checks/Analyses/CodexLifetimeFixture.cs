using System.Diagnostics;
using Mentor.Application.Analyses;
using Mentor.Application.Diagnostics;
using Mentor.Checks.Persistence;
using Mentor.Checks.TestDoubles;
using Mentor.Infrastructure.Analyses;
using Mentor.Infrastructure.Configuration;
using Mentor.Infrastructure.Processes;
using Microsoft.Extensions.Options;

namespace Mentor.Checks.Analyses;

internal static class CodexLifetimeFixture
{
    public static async Task RunHostAsync(string directory)
    {
        var clock = new TestClock(LocalTime.At("2026-10-18T12:00:00"));
        var repository = TestRepository.Create(Path.Combine(directory, "mentor.db"), "{{data}}", clock);
        var conversation = repository.Add("https://chatgpt.com/share/00000000-0000-0000-0000-000000000001").Conversation;
        repository.ClaimImport();
        repository.ImportDone(conversation.Id, new("Test", [new("user", "Message", 1)]));
        var original = repository.EnqueueAnalysis("manual", "2026-10-18");
        repository.ClaimAnalysis();
        repository.AnalysisDone(original, "Original report");
        repository.EnqueueAnalysis("manual", "2026-10-18");
        var batch = repository.ClaimAnalysis()!.Value;
        using var telemetry = new MentorTelemetry();
        var analyzer = new CodexCliAnalyzer(Options.Create(new MentorInfrastructureOptions
        {
            CodexExecutable = Environment.ProcessPath!,
            DataDirectory = directory
        }), telemetry, new CliProcessRunner());
        await analyzer.AnalyzeAsync(batch.Input, default);
    }

    public static async Task HangAsync(string[] arguments)
    {
        var directory = Environment.GetEnvironmentVariable("MENTOR_LIFETIME_DIRECTORY")!;
        var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true };
        start.ArgumentList.Add("--lifetime-descendant");
        using var descendant = Process.Start(start)!;
        File.WriteAllText(Path.Combine(directory, "arguments.txt"), string.Join('\n', arguments));
        File.WriteAllText(Path.Combine(directory, "processes.tmp"), $"{Environment.ProcessId}\n{descendant.Id}");
        File.Move(Path.Combine(directory, "processes.tmp"), Path.Combine(directory, "processes.txt"));
        await Task.Delay(Timeout.InfiniteTimeSpan);
    }
}
