using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Mentor.Infrastructure.Processes;

internal sealed class WindowsProcessAttributes : IDisposable
{
    public IntPtr Pointer
    {
        get;
    }
    private readonly List<IntPtr> values = [];

    public WindowsProcessAttributes()
    {
        nuint size = 0;
        InitializeProcThreadAttributeList(IntPtr.Zero, 2, 0, ref size);
        var pointer = Marshal.AllocHGlobal(checked((nint)size));
        if (!InitializeProcThreadAttributeList(pointer, 2, 0, ref size))
        {
            var error = new Win32Exception(Marshal.GetLastWin32Error());
            Marshal.FreeHGlobal(pointer);
            throw error;
        }
        Pointer = pointer;
    }

    public void Add(nuint attribute, params IntPtr[] handles)
    {
        var size = checked(handles.Length * IntPtr.Size);
        var value = Marshal.AllocHGlobal(size);
        values.Add(value);
        Marshal.Copy(handles, 0, value, handles.Length);
        if (!UpdateProcThreadAttribute(Pointer, 0, attribute, value, (nuint)size, IntPtr.Zero, IntPtr.Zero))
            throw new Win32Exception(Marshal.GetLastWin32Error());
    }

    public void Dispose()
    {
        DeleteProcThreadAttributeList(Pointer);
        Marshal.FreeHGlobal(Pointer);
        foreach (var value in values)
            Marshal.FreeHGlobal(value);
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool InitializeProcThreadAttributeList(IntPtr list, int count, uint flags, ref nuint size);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UpdateProcThreadAttribute(IntPtr list, uint flags, nuint attribute, IntPtr value,
        nuint size, IntPtr previousValue, IntPtr returnSize);

    [DllImport("kernel32.dll")]
    private static extern void DeleteProcThreadAttributeList(IntPtr list);
}
