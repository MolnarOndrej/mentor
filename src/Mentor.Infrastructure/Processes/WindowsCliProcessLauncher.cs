using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Mentor.Infrastructure.Processes;

internal static class WindowsCliProcessLauncher
{
    public static RunningCliProcess Start(ProcessStartInfo start)
    {
        var job = new WindowsProcessJob();
        Process? process = null;
        StreamWriter? input = null;
        StreamReader? output = null;
        StreamReader? error = null;
        try
        {
            using var inputPipe = CreateInputPipe(out var childInput);
            using var inheritedInput = childInput;
            using var outputPipe = CreateOutputPipe(out var childOutput);
            using var inheritedOutput = childOutput;
            using var errorPipe = CreateOutputPipe(out var childError);
            using var inheritedError = childError;
            using var attributes = new WindowsProcessAttributes();
            attributes.Add(0x2000D, job.Handle.DangerousGetHandle()); // PROC_THREAD_ATTRIBUTE_JOB_LIST
            attributes.Add(0x20002, childInput.DangerousGetHandle(), childOutput.DangerousGetHandle(),
                childError.DangerousGetHandle()); // PROC_THREAD_ATTRIBUTE_HANDLE_LIST: never inherit the job itself.
            var startup = new WindowsStartupInfoEx
            {
                AttributeList = attributes.Pointer,
                StartupInfo = new WindowsStartupInfo
                {
                    Size = (uint)Marshal.SizeOf<WindowsStartupInfoEx>(),
                    Flags = 0x100, // STARTF_USESTDHANDLES
                    StandardInput = childInput.DangerousGetHandle(),
                    StandardOutput = childOutput.DangerousGetHandle(),
                    StandardError = childError.DangerousGetHandle()
                }
            };
            var command = new StringBuilder(string.Join(' ', new[] { start.FileName }.Concat(start.ArgumentList).Select(Quote)));
            var environment = string.Join('\0', start.Environment.Where(x => x.Value != null)
                .OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase).Select(x => $"{x.Key}={x.Value}")) + "\0\0";
            // Suspended only to attach the managed Process handle safely. Job membership is atomic with creation.
            const uint flags = 0x08000000 | 0x00080000 | 0x00000400 | 0x00000004;
            if (!CreateProcessW(null, command, IntPtr.Zero, IntPtr.Zero, true, flags, environment,
                start.WorkingDirectory, ref startup, out var info))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            using var nativeProcess = new SafeFileHandle(info.Process, true);
            using var nativeThread = new SafeFileHandle(info.Thread, true);
            process = Process.GetProcessById(info.ProcessId);
            _ = process.Handle;
            // FileStream takes ownership; duplicate the parent handles before the using declarations close them.
            input = new StreamWriter(new FileStream(Duplicate(inputPipe), FileAccess.Write), new UTF8Encoding(false));
            output = new StreamReader(new FileStream(Duplicate(outputPipe), FileAccess.Read), Encoding.UTF8);
            error = new StreamReader(new FileStream(Duplicate(errorPipe), FileAccess.Read), Encoding.UTF8);
            if (ResumeThread(nativeThread) == uint.MaxValue)
                throw new Win32Exception(Marshal.GetLastWin32Error());
            return new RunningCliProcess(process, input, output, error, job);
        }
        catch
        {
            job.Dispose(); // Also terminates a process created successfully before any later startup failure.
            input?.Dispose();
            output?.Dispose();
            error?.Dispose();
            process?.Dispose();
            throw;
        }
    }

    private static SafeFileHandle CreateInputPipe(out SafeFileHandle child)
    {
        var attributes = new WindowsSecurityAttributes { Length = (uint)Marshal.SizeOf<WindowsSecurityAttributes>(), InheritHandle = 1 };
        if (!CreatePipe(out child, out var parent, ref attributes, 0))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        return MakeParentHandleNonInheritable(parent, child);
    }

    private static SafeFileHandle CreateOutputPipe(out SafeFileHandle child)
    {
        var attributes = new WindowsSecurityAttributes { Length = (uint)Marshal.SizeOf<WindowsSecurityAttributes>(), InheritHandle = 1 };
        if (!CreatePipe(out var parent, out child, ref attributes, 0))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        return MakeParentHandleNonInheritable(parent, child);
    }

    private static SafeFileHandle MakeParentHandleNonInheritable(SafeFileHandle parent, SafeFileHandle child)
    {
        if (SetHandleInformation(parent, 1, 0))
            return parent;
        var error = new Win32Exception(Marshal.GetLastWin32Error());
        parent.Dispose();
        child.Dispose();
        throw error;
    }

    private static SafeFileHandle Duplicate(SafeFileHandle handle)
    {
        var current = GetCurrentProcess();
        if (!DuplicateHandle(current, handle, current, out var copy, 0, false, 2))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        return copy;
    }

    // Windows argv escaping (not shell escaping), including embedded quotes and trailing backslashes.
    private static string Quote(string argument)
    {
        var result = new StringBuilder("\"");
        var slashes = 0;
        foreach (var character in argument)
        {
            if (character == '\\')
            {
                slashes++;
                continue;
            }
            result.Append('\\', character == '"' ? slashes * 2 + 1 : slashes);
            result.Append(character);
            slashes = 0;
        }
        return result.Append('\\', slashes * 2).Append('"').ToString();
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateProcessW(string? application, StringBuilder command, IntPtr processAttributes,
        IntPtr threadAttributes, [MarshalAs(UnmanagedType.Bool)] bool inheritHandles, uint flags, string environment,
        string directory, ref WindowsStartupInfoEx startup, out WindowsProcessInformation information);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreatePipe(out SafeFileHandle read, out SafeFileHandle write,
        ref WindowsSecurityAttributes attributes, uint size);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetHandleInformation(SafeFileHandle handle, uint mask, uint flags);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint ResumeThread(SafeFileHandle thread);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DuplicateHandle(IntPtr sourceProcess, SafeFileHandle source, IntPtr targetProcess,
        out SafeFileHandle target, uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint options);
}
