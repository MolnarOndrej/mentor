using System.Runtime.InteropServices;

namespace Mentor.Infrastructure.Processes;

[StructLayout(LayoutKind.Sequential)]
internal struct WindowsProcessInformation
{
    public IntPtr Process;
    public IntPtr Thread;
    public int ProcessId;
    public int ThreadId;
}
