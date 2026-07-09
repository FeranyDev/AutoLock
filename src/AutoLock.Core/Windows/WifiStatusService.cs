using System.Runtime.InteropServices;
using System.Text;

namespace AutoLock.Core;

public static class WifiStatusService
{
    private const int Dot11SsidMaxLength = 32;
    private const int WlanApiVersion2 = 2;
    private const int WlanInterfaceStateConnected = 1;
    private const int WlanIntfOpcodeCurrentConnection = 7;

    public static string? GetConnectedSsid()
    {
        var openResult = WlanOpenHandle(WlanApiVersion2, IntPtr.Zero, out _, out var clientHandle);
        if (openResult != 0)
        {
            return null;
        }

        try
        {
            var enumResult = WlanEnumInterfaces(clientHandle, IntPtr.Zero, out var interfacesPointer);
            if (enumResult != 0 || interfacesPointer == IntPtr.Zero)
            {
                return null;
            }

            try
            {
                var header = Marshal.PtrToStructure<WlanInterfaceInfoListHeader>(interfacesPointer);
                var itemPointer = IntPtr.Add(interfacesPointer, Marshal.SizeOf<WlanInterfaceInfoListHeader>());
                var itemSize = Marshal.SizeOf<WlanInterfaceInfo>();

                for (var i = 0; i < header.NumberOfItems; i++)
                {
                    var info = Marshal.PtrToStructure<WlanInterfaceInfo>(IntPtr.Add(itemPointer, i * itemSize));
                    if ((int)info.State != WlanInterfaceStateConnected)
                    {
                        continue;
                    }

                    var queryResult = WlanQueryInterface(
                        clientHandle,
                        ref info.InterfaceGuid,
                        WlanIntfOpcodeCurrentConnection,
                        IntPtr.Zero,
                        out _,
                        out var dataPointer,
                        out _);

                    if (queryResult != 0 || dataPointer == IntPtr.Zero)
                    {
                        continue;
                    }

                    try
                    {
                        var attributes = Marshal.PtrToStructure<WlanConnectionAttributes>(dataPointer);
                        return DecodeSsid(attributes.AssociationAttributes.Dot11Ssid);
                    }
                    finally
                    {
                        WlanFreeMemory(dataPointer);
                    }
                }

                return null;
            }
            finally
            {
                WlanFreeMemory(interfacesPointer);
            }
        }
        finally
        {
            WlanCloseHandle(clientHandle, IntPtr.Zero);
        }
    }

    public static bool IsConnectedToTrustedWifi(string? trustedSsid)
    {
        if (string.IsNullOrWhiteSpace(trustedSsid))
        {
            return false;
        }

        return IsConnectedToTrustedWifi([trustedSsid]);
    }

    public static bool IsConnectedToTrustedWifi(IEnumerable<string>? trustedSsids)
    {
        var trusted = (trustedSsids ?? [])
            .Select(x => x.Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToArray();

        if (trusted.Length == 0)
        {
            return false;
        }

        var current = GetConnectedSsid()?.Trim();
        return !string.IsNullOrWhiteSpace(current) &&
            trusted.Any(x => string.Equals(current, x, StringComparison.OrdinalIgnoreCase));
    }

    private static string DecodeSsid(Dot11Ssid ssid)
    {
        var length = Math.Clamp((int)ssid.Length, 0, ssid.Ssid.Length);
        return Encoding.UTF8.GetString(ssid.Ssid, 0, length);
    }

    [DllImport("wlanapi.dll", SetLastError = true)]
    private static extern uint WlanOpenHandle(
        uint clientVersion,
        IntPtr reserved,
        out uint negotiatedVersion,
        out IntPtr clientHandle);

    [DllImport("wlanapi.dll", SetLastError = true)]
    private static extern uint WlanCloseHandle(IntPtr clientHandle, IntPtr reserved);

    [DllImport("wlanapi.dll", SetLastError = true)]
    private static extern uint WlanEnumInterfaces(
        IntPtr clientHandle,
        IntPtr reserved,
        out IntPtr interfaceList);

    [DllImport("wlanapi.dll", SetLastError = true)]
    private static extern uint WlanQueryInterface(
        IntPtr clientHandle,
        ref Guid interfaceGuid,
        int opCode,
        IntPtr reserved,
        out uint dataSize,
        out IntPtr data,
        out int wlanOpcodeValueType);

    [DllImport("wlanapi.dll")]
    private static extern void WlanFreeMemory(IntPtr memory);

    [StructLayout(LayoutKind.Sequential)]
    private struct WlanInterfaceInfoListHeader
    {
        public uint NumberOfItems;
        public uint Index;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WlanInterfaceInfo
    {
        public Guid InterfaceGuid;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string InterfaceDescription;

        public uint State;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WlanConnectionAttributes
    {
        public uint IsState;
        public uint ConnectionMode;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string ProfileName;

        public WlanAssociationAttributes AssociationAttributes;
        public WlanSecurityAttributes SecurityAttributes;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WlanAssociationAttributes
    {
        public Dot11Ssid Dot11Ssid;
        public uint Dot11BssType;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 6)]
        public byte[] Dot11Bssid;

        public uint Dot11PhyType;
        public uint Dot11PhyIndex;
        public uint WlanSignalQuality;
        public uint RxRate;
        public uint TxRate;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WlanSecurityAttributes
    {
        [MarshalAs(UnmanagedType.Bool)]
        public bool SecurityEnabled;

        [MarshalAs(UnmanagedType.Bool)]
        public bool OneXEnabled;

        public uint AuthAlgorithm;
        public uint CipherAlgorithm;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Dot11Ssid
    {
        public uint Length;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = Dot11SsidMaxLength)]
        public byte[] Ssid;
    }
}
