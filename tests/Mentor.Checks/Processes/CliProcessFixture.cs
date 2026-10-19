using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace Mentor.Checks.Processes;

internal static class CliProcessFixture
{
    public static async Task<int> RunAsync(string[] arguments)
    {
        Console.InputEncoding = new UTF8Encoding(false);
        Console.OutputEncoding = new UTF8Encoding(false);
        var mode = arguments[0];
        if (mode is "hang" or "no-read" or "root-exit")
        {
            var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true };
            start.ArgumentList.Add("--lifetime-descendant");
            using var child = Process.Start(start)!;
            var marker = Path.Combine(arguments[1], "processes.txt");
            File.WriteAllText(marker + ".tmp", $"{Environment.ProcessId}\n{child.Id}");
            File.Move(marker + ".tmp", marker);
            if (mode == "root-exit")
                return 0;
            await Task.Delay(Timeout.InfiniteTimeSpan);
        }
        if (mode is "stdout-limit" or "stderr-limit")
        {
            var writer = mode == "stdout-limit" ? Console.Out : Console.Error;
            await writer.WriteAsync(new string('x', 64 * 1024));
            await writer.FlushAsync();
            await Task.Delay(Timeout.InfiniteTimeSpan);
        }
        if (mode == "duplex")
        {
            await Task.WhenAll(Console.Out.WriteAsync(new string('o', 128 * 1024)),
                Console.Error.WriteAsync(new string('e', 128 * 1024)));
            var data = await Console.In.ReadToEndAsync();
            Console.Write(data);
            return 0;
        }
        var input = await Console.In.ReadToEndAsync();
        Console.Write(JsonSerializer.Serialize(new
        {
            Arguments = arguments.Skip(1).ToArray(),
            Input = input,
            Directory = Environment.CurrentDirectory,
            Value = Environment.GetEnvironmentVariable("MENTOR_RUNNER_TEST")
        }));
        Console.Error.Write("stderr: český text");
        return mode == "exit" ? 7 : 0;
    }
}
