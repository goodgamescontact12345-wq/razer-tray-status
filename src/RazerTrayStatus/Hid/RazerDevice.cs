using System.Runtime.InteropServices;

namespace RazerTrayStatus.Hid;

/// <summary>
/// A single Razer HID control interface and the battery queries that run against it.
///
/// Protocol (reverse engineered by OpenRazer, reused by every Windows Razer battery tool):
///   90-byte feature report: [0]=status [1]=transaction id [5]=data size
///   [6]=command class [7]=command id [8..]=arguments [88..89]=CRC (XOR of bytes 2..87)
///   Battery: class 0x07 id 0x80, args[1] is 0..255 -> percent
///   Charging: class 0x07 id 0x84, args[1] is 0/1
/// </summary>
internal sealed class RazerDevice
{
    public const ushort RazerVendorId = 0x1532;

    private const byte MiscCommandClass = 0x07;
    private const byte GetBatteryLevel = 0x80;
    private const byte GetChargingStatus = 0x84;
    private const int ReportLength = 90;
    private const byte StatusSuccess = 0x02;

    /// <summary>Transaction ids to try, in order: modern mice, older receivers, then the legacy default.</summary>
    public static readonly byte[] TransactionIds = { 0x1F, 0x3F, 0xFF };

    public RazerDevice(string devicePath, string productName, string serial, ushort productId, int featureReportLength)
    {
        DevicePath = devicePath;
        ProductName = productName;
        Serial = serial;
        ProductId = productId;
        FeatureReportLength = featureReportLength;
    }

    public string DevicePath { get; }
    public string ProductName { get; }
    public string Serial { get; }
    public ushort ProductId { get; }
    public int FeatureReportLength { get; }

    /// <summary>Stable identity for pinning: the device serial, falling back to its HID path.</summary>
    public string Key => string.IsNullOrWhiteSpace(Serial) ? DevicePath : Serial.Trim();

    public string DisplayName =>
        string.IsNullOrWhiteSpace(ProductName) ? $"Razer device 0x{ProductId:X4}" : ProductName.Trim();

    public static int PercentFromRaw(byte raw) => Math.Clamp((int)Math.Round(raw * 100.0 / 255.0), 0, 100);

    public bool TryRead(byte transactionId, out int percent, out bool charging, out string error)
    {
        percent = 0;
        charging = false;
        error = string.Empty;

        IntPtr handle = NativeHid.OpenDevice(DevicePath);
        if (handle == NativeHid.InvalidHandle)
        {
            error = "could not open device";
            return false;
        }

        try
        {
            if (!TryQuery(handle, transactionId, GetBatteryLevel, out byte[] battery, out error)) return false;

            percent = PercentFromRaw(battery[9]);

            if (TryQuery(handle, transactionId, GetChargingStatus, out byte[] charge, out _))
                charging = charge[9] != 0;

            return true;
        }
        finally
        {
            NativeHid.CloseHandle(handle);
        }
    }

    /// <summary>Diagnostic read used by --probe; reports every step instead of failing silently.</summary>
    public bool TryProbe(byte transactionId, out string details)
    {
        details = string.Empty;

        IntPtr handle = NativeHid.OpenDevice(DevicePath);
        if (handle == NativeHid.InvalidHandle)
        {
            details = "could not open device";
            return false;
        }

        try
        {
            var parts = new List<string>();

            bool batteryOk = TryQuery(handle, transactionId, GetBatteryLevel, out byte[] battery, out string batteryError);
            parts.Add(batteryOk
                ? $"battery raw=0x{battery[9]:X2} -> {PercentFromRaw(battery[9])}%"
                : "battery: " + batteryError);

            if (TryQuery(handle, transactionId, GetChargingStatus, out byte[] charge, out string chargeError))
                parts.Add($"charging={(charge[9] != 0 ? "yes" : "no")}");
            else
                parts.Add("charging: " + chargeError);

            details = string.Join(", ", parts);
            return batteryOk;
        }
        finally
        {
            NativeHid.CloseHandle(handle);
        }
    }

    private bool TryQuery(IntPtr handle, byte transactionId, byte commandId, out byte[] payload, out string error)
    {
        payload = Array.Empty<byte>();
        error = string.Empty;

        int length = Math.Max(91, FeatureReportLength > 0 ? FeatureReportLength : 91);
        byte[] frame = BuildRequest(transactionId, MiscCommandClass, commandId);

        var request = new byte[length];
        request[0] = 0x00; // report id
        Array.Copy(frame, 0, request, 1, frame.Length);

        if (!NativeHid.HidD_SetFeature(handle, request, length))
        {
            error = $"SetFeature failed (win32 {Marshal.GetLastWin32Error()})";
            return false;
        }

        var response = new byte[length];
        response[0] = 0x00; // report id, required on input

        if (!NativeHid.HidD_GetFeature(handle, response, length))
        {
            error = $"GetFeature failed (win32 {Marshal.GetLastWin32Error()})";
            return false;
        }

        return TryParseResponse(response, out payload, out error);
    }

    public static byte[] BuildRequest(byte transactionId, byte commandClass, byte commandId, byte dataSize = 0x02)
    {
        var frame = new byte[ReportLength];
        frame[0] = 0x00; // status: new command
        frame[1] = transactionId;
        frame[2] = 0x00; // remaining packets
        frame[3] = 0x00;
        frame[4] = 0x00; // protocol type
        frame[5] = dataSize;
        frame[6] = commandClass;
        frame[7] = commandId;

        byte crc = 0;
        for (int i = 2; i < 88; i++) crc ^= frame[i];
        frame[88] = crc;
        frame[89] = 0x00;

        return frame;
    }

    private static bool TryParseResponse(byte[] response, out byte[] payload, out string error)
    {
        // HidD_GetFeature includes the report id as byte 0 when the device uses one; try both layouts.
        foreach (int offset in new[] { 1, 0 })
        {
            if (response.Length < offset + ReportLength) continue;
            if (response[offset] != StatusSuccess) continue;

            payload = new byte[ReportLength];
            Array.Copy(response, offset, payload, 0, ReportLength);
            error = string.Empty;
            return true;
        }

        payload = Array.Empty<byte>();
        byte status = response.Length > 1 ? response[1] : response[0];
        error = $"no valid response (status 0x{status:X2})";
        return false;
    }
}
