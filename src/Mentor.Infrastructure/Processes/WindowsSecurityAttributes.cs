using System.Runtime.InteropServices;

namespace Mentor.Infrastructure.Processes;

[StructLayout(LayoutKind.Sequential)]
internal struct WindowsSecurityAttributes
{
    public uint Length;
    public IntPtr SecurityDescriptor;
    public int InheritHandle;
}
