using System.Runtime.InteropServices;

namespace Mentor.Infrastructure.Processes;

[StructLayout(LayoutKind.Sequential)]
internal struct WindowsStartupInfoEx
{
    public WindowsStartupInfo StartupInfo;
    public IntPtr AttributeList;
}
