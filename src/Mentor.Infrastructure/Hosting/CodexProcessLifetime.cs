using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Mentor.Infrastructure.Hosting;

public static class CodexProcessLifetime
{
    // Intentionally rooted for the entire OS process lifetime, not a DI scope or host lifetime.
    // Closing this handle also kills Mentor itself. Windows closes it even after a forced exit.
    private static readonly Lazy<SafeFileHandle> lifetimeJob = new(CreateLifetimeJob);

    public static void EnsureBound()
    {
        if (OperatingSystem.IsWindows())
            _ = lifetimeJob.Value;
    }

    private static SafeFileHandle CreateLifetimeJob()
    {
        var job = CreateJobObjectW(IntPtr.Zero, null);
        if (job.IsInvalid)
        {
            var error = new Win32Exception(Marshal.GetLastWin32Error());
            job.Dispose();
            throw error;
        }
        try
        {
            var limits = new WindowsJobLimits { LimitFlags = 0x2000 }; // JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE
            if (!SetInformationJobObject(job, 9, ref limits, (uint)Marshal.SizeOf<WindowsJobLimits>()))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            using var current = Process.GetCurrentProcess();
            // Bind the parent BEFORE creating Codex. All descendants inherit membership at creation,
            // so there is no start/assign race and no job handle is inherited by a child.
            if (!AssignProcessToJobObject(job, current.Handle))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            return job;
        }
        catch
        {
            job.Dispose();
            throw;
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateJobObjectW(IntPtr attributes, string? name);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetInformationJobObject(SafeFileHandle job, int informationClass,
        ref WindowsJobLimits information, uint informationLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AssignProcessToJobObject(SafeFileHandle job, IntPtr process);
}
