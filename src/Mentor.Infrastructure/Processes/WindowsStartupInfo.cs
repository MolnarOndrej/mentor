using System.Runtime.InteropServices;

namespace Mentor.Infrastructure.Processes;

[StructLayout(LayoutKind.Sequential)]
internal struct WindowsStartupInfo
{
    public uint Size;
    public IntPtr Reserved;
    public IntPtr Desktop;
    public IntPtr Title;
    public uint X;
    public uint Y;
    public uint XSize;
    public uint YSize;
    public uint XCountChars;
    public uint YCountChars;
    public uint FillAttribute;
    public uint Flags;
    public ushort ShowWindow;
    public ushort ReservedSize;
    public IntPtr ReservedBytes;
    public IntPtr StandardInput;
    public IntPtr StandardOutput;
    public IntPtr StandardError;
}
