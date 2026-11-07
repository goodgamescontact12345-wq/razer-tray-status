using System.Runtime.InteropServices;
using System.Threading;

namespace RazerTrayStatus.Hid;

internal enum HidAccess
{
    /// <summary>Feature-report-only access; works for collections the mouse driver holds open.</summary>
    None,

    Read,

    ReadWrite,
}

/// <summary>
/// Thin P/Invoke layer over setupapi.dll / hid.dll / kernel32.dll.
/// No third-party HID library is used, so the app has no runtime dependencies.
/// </summary>
internal static class NativeHid
{
    public static readonly IntPtr InvalidHandle = new(-1);

    public const int HidpStatusSuccess = 0x00110000;
    public const int ErrorIoPending = 997;
    // Kept short: a silent device must not stall a poll for long.
    public const int IoTimeoutMilliseconds = 400;

    private const int DigcfPresent = 0x02;
    private const int DigcfDeviceInterface = 0x10;

    private const uint GenericRead = 0x80000000;
    private const uint GenericWrite = 0x40000000;
    private const uint FileShareRead = 0x01;
    private const uint FileShareWrite = 0x02;
    private const uint OpenExisting = 3;
    private const uint FileFlagOverlapped = 0x40000000;

    [StructLayout(LayoutKind.Sequential)]
    private struct SP_DEVICE_INTERFACE_DATA
    {
        public int cbSize;
        public Guid InterfaceClassGuid;
        public int Flags;
        public IntPtr Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct HIDD_ATTRIBUTES
    {
        public int Size;
        public ushort VendorID;
        public ushort ProductID;
        public ushort VersionNumber;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct HIDP_CAPS
    {
        public ushort Usage;
        public ushort UsagePage;
        public ushort InputReportByteLength;
        public ushort OutputReportByteLength;
        public ushort FeatureReportByteLength;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)]
        public ushort[] Reserved;

        public ushort NumberLinkCollectionNodes;
        public ushort NumberInputButtonCaps;
        public ushort NumberInputValueCaps;
        public ushort NumberInputDataIndices;
        public ushort NumberOutputButtonCaps;
        public ushort NumberOutputValueCaps;
        public ushort NumberOutputDataIndices;
        public ushort NumberFeatureButtonCaps;
        public ushort NumberFeatureValueCaps;
        public ushort NumberFeatureDataIndices;

        /// <summary>Creates an initialized instance (the Reserved array must not be null when marshaled).</summary>
        public static HIDP_CAPS Create() => new() { Reserved = new ushort[17] };
    }

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr SetupDiGetClassDevs(ref Guid classGuid, IntPtr enumerator, IntPtr hwndParent, int flags);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SetupDiEnumDeviceInterfaces(
        IntPtr deviceInfoSet,
        IntPtr deviceInfoData,
        ref Guid interfaceClassGuid,
        int memberIndex,
        ref SP_DEVICE_INTERFACE_DATA deviceInterfaceData);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SetupDiGetDeviceInterfaceDetail(
        IntPtr deviceInfoSet,
        ref SP_DEVICE_INTERFACE_DATA deviceInterfaceData,
        IntPtr deviceInterfaceDetailData,
        int deviceInterfaceDetailDataSize,
        out int requiredSize,
        IntPtr deviceInfoData);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);

    [DllImport("hid.dll", SetLastError = true)]
    public static extern bool HidD_GetAttributes(IntPtr device, ref HIDD_ATTRIBUTES attributes);

    [DllImport("hid.dll", SetLastError = true)]
    public static extern bool HidD_GetPreparsedData(IntPtr device, out IntPtr preparsedData);

    [DllImport("hid.dll", SetLastError = true)]
    public static extern bool HidD_FreePreparsedData(IntPtr preparsedData);

    [DllImport("hid.dll", SetLastError = true)]
    public static extern int HidP_GetCaps(IntPtr preparsedData, ref HIDP_CAPS capabilities);

    [DllImport("hid.dll", SetLastError = true)]
    public static extern bool HidD_GetProductString(IntPtr device, byte[] buffer, int bufferLength);

    [DllImport("hid.dll", SetLastError = true)]
    public static extern bool HidD_GetSerialNumberString(IntPtr device, byte[] buffer, int bufferLength);

    [DllImport("hid.dll", SetLastError = true)]
    public static extern bool HidD_GetFeature(IntPtr device, byte[] reportBuffer, int reportBufferLength);

    [DllImport("hid.dll", SetLastError = true)]
    public static extern bool HidD_SetFeature(IntPtr device, byte[] reportBuffer, int reportBufferLength);

    [DllImport("hid.dll")]
    public static extern void HidD_GetHidGuid(out Guid hidGuid);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateFile(
        string fileName,
        uint desiredAccess,
        uint shareMode,
        IntPtr securityAttributes,
        uint creationDisposition,
        uint flagsAndAttributes,
        IntPtr templateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool CloseHandle(IntPtr handle);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool AttachConsole(uint processId);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool DestroyIcon(IntPtr icon);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool ReadFile(
        IntPtr hFile,
        byte[] lpBuffer,
        uint nNumberOfBytesToRead,
        IntPtr lpNumberOfBytesRead,
        ref NativeOverlapped lpOverlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool WriteFile(
        IntPtr hFile,
        byte[] lpBuffer,
        uint nNumberOfBytesToWrite,
        IntPtr lpNumberOfBytesWritten,
        ref NativeOverlapped lpOverlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetOverlappedResult(
        IntPtr hFile,
        ref NativeOverlapped lpOverlapped,
        out uint lpNumberOfBytesTransferred,
        bool bWait);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CancelIoEx(IntPtr hFile, ref NativeOverlapped lpOverlapped);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateEvent(IntPtr lpEventAttributes, bool bManualReset, bool bInitialState, string? lpName);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint WaitForSingleObject(IntPtr hHandle, uint dwMilliseconds);

    public static Guid GetHidInterfaceGuid()
    {
        HidD_GetHidGuid(out Guid guid);
        return guid;
    }

    /// <summary>Returns every HID device interface path currently present on the system.</summary>
    public static List<string> EnumerateHidDevicePaths(Guid interfaceGuid)
    {
        var paths = new List<string>();
        Guid guid = interfaceGuid;
        IntPtr deviceInfoSet = SetupDiGetClassDevs(ref guid, IntPtr.Zero, IntPtr.Zero, DigcfPresent | DigcfDeviceInterface);
        if (deviceInfoSet == InvalidHandle) return paths;

        try
        {
            var interfaceData = new SP_DEVICE_INTERFACE_DATA
            {
                cbSize = Marshal.SizeOf<SP_DEVICE_INTERFACE_DATA>(),
            };

            for (int index = 0; ; index++)
            {
                if (!SetupDiEnumDeviceInterfaces(deviceInfoSet, IntPtr.Zero, ref guid, index, ref interfaceData)) break;

                if (!SetupDiGetDeviceInterfaceDetail(deviceInfoSet, ref interfaceData, IntPtr.Zero, 0, out int requiredSize, IntPtr.Zero))
                {
                    if (requiredSize <= 0) continue;
                }

                IntPtr buffer = Marshal.AllocHGlobal(requiredSize);
                try
                {
                    // cbSize of SP_DEVICE_INTERFACE_DETAIL_DATA: 8 on 64-bit, 6 on 32-bit.
                    Marshal.WriteInt32(buffer, 0, IntPtr.Size == 8 ? 8 : 6);
                    if (!SetupDiGetDeviceInterfaceDetail(deviceInfoSet, ref interfaceData, buffer, requiredSize, out _, IntPtr.Zero))
                        continue;

                    // The path is a NUL-terminated wide string at offset 4. requiredSize includes
                    // struct padding on 64-bit, so never trust a computed character count: reading
                    // too far appends junk and CreateFile then fails with ERROR_INVALID_NAME (123).
                    string? path = Marshal.PtrToStringUni(IntPtr.Add(buffer, 4));
                    if (string.IsNullOrWhiteSpace(path)) continue;
                    paths.Add(NormalizePath(path));
                }
                finally
                {
                    Marshal.FreeHGlobal(buffer);
                }
            }
        }
        finally
        {
            SetupDiDestroyDeviceInfoList(deviceInfoSet);
        }

        return paths;
    }

    /// <summary>Strips a terminator and any junk that ended up after the interface GUID.</summary>
    private static string NormalizePath(string path)
    {
        int terminator = path.IndexOf('\0');
        if (terminator >= 0) path = path[..terminator];

        // A HID interface path always ends with its class GUID, closed by '}'.
        int guidEnd = path.LastIndexOf('}');
        if (guidEnd >= 0 && guidEnd < path.Length - 1) path = path[..(guidEnd + 1)];

        return path.Trim();
    }

    public static IntPtr OpenDevice(string devicePath, HidAccess access = HidAccess.None, bool overlapped = false)
    {
        uint desiredAccess = access switch
        {
            HidAccess.Read => GenericRead,
            HidAccess.ReadWrite => GenericRead | GenericWrite,
            _ => 0,
        };

        uint flags = overlapped ? FileFlagOverlapped : 0;

        return CreateFile(
            devicePath,
            desiredAccess,
            FileShareRead | FileShareWrite,
            IntPtr.Zero,
            OpenExisting,
            flags,
            IntPtr.Zero);
    }

    /// <summary>Writes a HID output report (used by devices that reject feature reports on Windows).</summary>
    public static bool WriteOutputReport(IntPtr handle, byte[] buffer, int length, int timeoutMilliseconds)
    {
        IntPtr eventHandle = CreateEvent(IntPtr.Zero, true, false, null);
        if (eventHandle == IntPtr.Zero) return false;

        try
        {
            var overlapped = new NativeOverlapped { EventHandle = eventHandle };

            if (!WriteFile(handle, buffer, (uint)length, IntPtr.Zero, ref overlapped))
            {
                if (Marshal.GetLastWin32Error() != ErrorIoPending) return false;
                if (WaitForSingleObject(eventHandle, (uint)timeoutMilliseconds) != 0)
                {
                    CancelIoEx(handle, ref overlapped);
                    return false;
                }
            }

            return GetOverlappedResult(handle, ref overlapped, out _, true);
        }
        finally
        {
            CloseHandle(eventHandle);
        }
    }

    /// <summary>Reads one HID input report, bounded by a timeout. Returns bytes read, or -1.</summary>
    public static int ReadInputReport(IntPtr handle, byte[] buffer, int timeoutMilliseconds)
    {
        IntPtr eventHandle = CreateEvent(IntPtr.Zero, true, false, null);
        if (eventHandle == IntPtr.Zero) return -1;

        try
        {
            var overlapped = new NativeOverlapped { EventHandle = eventHandle };

            if (!ReadFile(handle, buffer, (uint)buffer.Length, IntPtr.Zero, ref overlapped))
            {
                if (Marshal.GetLastWin32Error() != ErrorIoPending) return -1;
                if (WaitForSingleObject(eventHandle, (uint)timeoutMilliseconds) != 0)
                {
                    CancelIoEx(handle, ref overlapped);
                    return -1;
                }
            }

            if (!GetOverlappedResult(handle, ref overlapped, out uint transferred, true)) return -1;
            return (int)Math.Min(transferred, (uint)buffer.Length);
        }
        finally
        {
            CloseHandle(eventHandle);
        }
    }

    public static string ReadString(IntPtr handle, Func<IntPtr, byte[], int, bool> getter)
    {
        var buffer = new byte[512];
        if (!getter(handle, buffer, buffer.Length)) return string.Empty;

        string value = System.Text.Encoding.Unicode.GetString(buffer);
        int terminator = value.IndexOf('\0');
        if (terminator >= 0) value = value[..terminator];
        return value.Trim();
    }

    /// <summary>Turns a Win32 error into something readable in the diagnostics log.</summary>
    public static string DescribeError(int error) => error switch
    {
        0 => "ok",
        2 => "win32 2 (file not found)",
        5 => "win32 5 (access denied)",
        21 => "win32 21 (device not ready)",
        23 => "win32 23 (data error)",
        31 => "win32 31 (general failure)",
        32 => "win32 32 (in use by another process)",
        87 => "win32 87 (invalid parameter - feature report rejected)",
        1167 => "win32 1167 (device not connected)",
        121 => "win32 121 (semaphore timeout)",
        _ => $"win32 {error}",
    };
}
