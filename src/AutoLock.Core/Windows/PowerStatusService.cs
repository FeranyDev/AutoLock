using System.Runtime.InteropServices;

namespace AutoLock.Core;

public static class PowerStatusService
{
    public static bool IsExternalPowerConnected()
    {
        return GetSystemPowerStatus(out var status) && status.AcLineStatus == 1;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetSystemPowerStatus(out SystemPowerStatus status);

    [StructLayout(LayoutKind.Sequential)]
    private struct SystemPowerStatus
    {
        public byte AcLineStatus;
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte SystemStatusFlag;
        public uint BatteryLifeTime;
        public uint BatteryFullLifeTime;
    }
}
