using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Mentor.Infrastructure.Processes;

internal sealed class WindowsProcessJob : IDisposable
{
    public SafeFileHandle Handle
    {
        get;
    }

    public WindowsProcessJob()
    {
        Handle = CreateJobObjectW(IntPtr.Zero, null);
        if (Handle.IsInvalid)
        {
            var error = new Win32Exception(Marshal.GetLastWin32Error());
            Handle.Dispose();
            throw error;
        }
        var limits = new WindowsJobLimits { LimitFlags = 0x2000 }; // JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE
        if (!SetInformationJobObject(Handle, 9, ref limits, (uint)Marshal.SizeOf<WindowsJobLimits>()))
        {
            var error = new Win32Exception(Marshal.GetLastWin32Error());
            Handle.Dispose();
            throw error;
        }
    }

    public void Terminate()
    {
        if (!TerminateJobObject(Handle, 1))
            throw new Win32Exception(Marshal.GetLastWin32Error());
    }

    public void Dispose() => Handle.Dispose();

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateJobObjectW(IntPtr attributes, string? name);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetInformationJobObject(SafeFileHandle job, int informationClass,
        ref WindowsJobLimits information, uint informationLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TerminateJobObject(SafeFileHandle job, uint exitCode);
}
