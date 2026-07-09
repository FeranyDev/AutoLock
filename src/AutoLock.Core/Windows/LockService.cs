using System.ComponentModel;
using System.Runtime.InteropServices;

namespace AutoLock.Core;

public static class LockService
{
    public static LockResult LockWorkStation()
    {
        if (NativeMethods.LockWorkStation())
        {
            return LockResult.Success;
        }

        var error = Marshal.GetLastWin32Error();
        return new LockResult(false, error, new Win32Exception(error).Message);
    }

    private static class NativeMethods
    {
        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool LockWorkStation();
    }
}

public sealed record LockResult(bool Succeeded, int ErrorCode, string? ErrorMessage)
{
    public static LockResult Success { get; } = new(true, 0, null);
}
