using System.Runtime.InteropServices;

namespace RazerTrayStatus.Hid;

internal enum IoMode
{
    /// <summary>HidD_SetFeature / HidD_GetFeature.</summary>
    Feature,

    /// <summary>HID output report + input report (what several Razer devices demand on Windows).</summary>
    OutputInput,
}

internal sealed record ReadOutcome(
    bool Success,
    int Percent,
    bool Charging,
    byte TransactionId,
    IoMode Mode,
    string? Error);

/// <summary>
/// One Razer HID collection and the battery queries that run against it.
///
/// Protocol (reverse engineered by OpenRazer, reused by every Windows Razer battery tool):
///   90-byte feature report: [0]=status [1]=transaction id [5]=data size
///   [6]=command class [7]=command id [8..]=arguments [88..89]=CRC (XOR of bytes 2..87)
///   Battery: class 0x07 id 0x80, args[1] is 0..255 -> percent
///   Charging: class 0x07 id 0x84, args[1] is 0/1
///
/// A physical mouse exposes several collections (mouse, keyboard, vendor) and which one answers
/// differs by model and by wired/wireless mode. <see cref="Read"/> therefore walks every
/// transaction id and both I/O strategies until something replies.
/// </summary>
internal sealed class RazerDevice
{
    public const ushort RazerVendorId = 0x1532;

    private const byte MiscCommandClass = 0x07;
    private const byte GetBatteryLevel = 0x80;
    private const byte GetChargingStatus = 0x84;
    private const int ReportLength = 90;
    private const byte StatusSuccess = 0x02;
    private const int InputReportAttempts = 2;

    /// <summary>Transaction ids to try, in order: modern mice, older receivers, then the legacy default.</summary>
    public static readonly byte[] TransactionIds = { 0x1F, 0x3F, 0xFF };

    public RazerDevice(
        string devicePath,
        string productName,
        string serial,
        ushort productId,
        int featureReportLength,
        ushort usagePage,
        ushort usage,
        bool opened = true,
        string? openError = null)
    {
        DevicePath = devicePath;
        ProductName = productName;
        Serial = serial;
        ProductId = productId;
        FeatureReportLength = featureReportLength;
        UsagePage = usagePage;
        Usage = usage;
        Opened = opened;
        OpenError = openError;
    }

    public string DevicePath { get; }
    public string ProductName { get; }
    public string Serial { get; }
    public ushort ProductId { get; }
    public int FeatureReportLength { get; }
    public ushort UsagePage { get; }
    public ushort Usage { get; }

    /// <summary>False when the collection could not be opened at all; it is still listed and retried.</summary>
    public bool Opened { get; }

    public string? OpenError { get; }

    /// <summary>True for the collection Windows itself uses as a mouse (usage page 1, usage 2).</summary>
    public bool IsMouseCollection => UsagePage == 0x01 && Usage == 0x02;

    /// <summary>Can this collection hold a 90-byte Razer report (report id included)?</summary>
    public bool CanCarryControlReport => FeatureReportLength >= 91;

    public string Key => string.IsNullOrWhiteSpace(Serial) ? DevicePath : Serial.Trim();

    public string DisplayName =>
        string.IsNullOrWhiteSpace(ProductName) ? $"Razer device 0x{ProductId:X4}" : ProductName.Trim();

    public static int PercentFromRaw(byte raw) => Math.Clamp((int)Math.Round(raw * 100.0 / 255.0), 0, 100);

    /// <summary>
    /// Reads level and charging state, trying the remembered strategy first and then every
    /// remaining combination of I/O mode, handle access and transaction id.
    /// </summary>
    public ReadOutcome Read(DevicePreference? preference)
    {
        List<byte> transactionIds = TransactionIds.ToList();
        if (preference?.TransactionId is byte preferredId && transactionIds.Contains(preferredId))
        {
            transactionIds.Remove(preferredId);
            transactionIds.Insert(0, preferredId);
        }

        List<IoMode> modes = new() { IoMode.Feature, IoMode.OutputInput };
        if (string.Equals(preference?.IoMode, nameof(IoMode.OutputInput), StringComparison.OrdinalIgnoreCase))
            modes.Reverse();

        string lastError = Opened ? "no answer" : "could not open device (" + (OpenError ?? "unknown error") + ")";
        byte lastTransactionId = transactionIds[0];

        foreach (IoMode mode in modes)
        {
            lastTransactionId = transactionIds[0];

            // Feature reports work on a zero-access handle; output/input reports need read/write
            // access plus overlapped I/O.
            HidAccess[] accessModes = mode == IoMode.Feature
                ? new[] { HidAccess.None, HidAccess.ReadWrite }
                : new[] { HidAccess.ReadWrite };

            foreach (HidAccess access in accessModes)
            {
                foreach (byte transactionId in transactionIds)
                {
                    lastTransactionId = transactionId;
                    IntPtr handle = NativeHid.OpenDevice(DevicePath, access, overlapped: mode == IoMode.OutputInput);

                    if (handle == NativeHid.InvalidHandle)
                    {
                        lastError = "could not open device (" + NativeHid.DescribeError(Marshal.GetLastWin32Error()) + ")";
                        break; // a different transaction id will not open the device either
                    }

                    try
                    {
                        int percent = 0;
                        bool charging = false;

                        if (TryQuery(handle, mode, transactionId, out percent, out charging, out string error))
                            return new ReadOutcome(true, percent, charging, transactionId, mode, null);

                        lastError = error;
                    }
                    finally
                    {
                        NativeHid.CloseHandle(handle);
                    }
                }
            }
        }

        return new ReadOutcome(false, 0, false, lastTransactionId, IoMode.Feature, lastError);
    }

    /// <summary>Every strategy with its outcome, for the diagnostics log.</summary>
    public List<string> ProbeAll()
    {
        var lines = new List<string>();

        foreach (IoMode mode in new[] { IoMode.Feature, IoMode.OutputInput })
        {
            HidAccess[] accessModes = mode == IoMode.Feature
                ? new[] { HidAccess.None, HidAccess.ReadWrite }
                : new[] { HidAccess.ReadWrite };

            foreach (HidAccess access in accessModes)
            {
                IntPtr handle = NativeHid.OpenDevice(DevicePath, access, overlapped: mode == IoMode.OutputInput);
                if (handle == NativeHid.InvalidHandle)
                {
                    lines.Add($"    {mode}/{access}: could not open ({NativeHid.DescribeError(Marshal.GetLastWin32Error())})");
                    continue;
                }

                try
                {
                    foreach (byte transactionId in TransactionIds)
                    {
                        bool ok = TryQuery(handle, mode, transactionId, out int percent, out bool charging, out string error);
                        lines.Add(ok
                            ? $"    {mode}/{access}/tid 0x{transactionId:X2}: OK {percent}%{(charging ? " charging" : string.Empty)}"
                            : $"    {mode}/{access}/tid 0x{transactionId:X2}: {error}");
                    }
                }
                finally
                {
                    NativeHid.CloseHandle(handle);
                }
            }
        }

        return lines;
    }

    private bool TryQuery(
        IntPtr handle,
        IoMode mode,
        byte transactionId,
        out int percent,
        out bool charging,
        out string error)
    {
        percent = 0;
        charging = false;

        if (!Exchange(handle, mode, transactionId, GetBatteryLevel, out byte[] battery, out error)) return false;

        percent = PercentFromRaw(battery[9]);

        if (Exchange(handle, mode, transactionId, GetChargingStatus, out byte[] charge, out _))
            charging = charge[9] != 0;

        return true;
    }

    private bool Exchange(
        IntPtr handle,
        IoMode mode,
        byte transactionId,
        byte commandId,
        out byte[] payload,
        out string error)
    {
        payload = Array.Empty<byte>();
        error = string.Empty;

        int length = Math.Max(91, FeatureReportLength);
        byte[] frame = BuildRequest(transactionId, MiscCommandClass, commandId);

        var request = new byte[length];
        request[0] = 0x00; // report id
        Array.Copy(frame, 0, request, 1, frame.Length);

        if (mode == IoMode.Feature)
        {
            if (!NativeHid.HidD_SetFeature(handle, request, length))
            {
                error = $"SetFeature failed ({NativeHid.DescribeError(Marshal.GetLastWin32Error())})";
                return false;
            }

            var featureResponse = new byte[length];
            featureResponse[0] = 0x00; // report id, required on input

            if (!NativeHid.HidD_GetFeature(handle, featureResponse, length))
            {
                error = $"GetFeature failed ({NativeHid.DescribeError(Marshal.GetLastWin32Error())})";
                return false;
            }

            return TryParseResponse(featureResponse, out payload, out error);
        }

        if (!NativeHid.WriteOutputReport(handle, request, length, NativeHid.IoTimeoutMilliseconds))
        {
            error = $"WriteFile failed ({NativeHid.DescribeError(Marshal.GetLastWin32Error())})";
            return false;
        }

        var response = new byte[length];

        for (int attempt = 0; attempt < InputReportAttempts; attempt++)
        {
            int read = NativeHid.ReadInputReport(handle, response, NativeHid.IoTimeoutMilliseconds);
            if (read <= 0)
            {
                error = "no input report (timeout)";
                return false;
            }

            if (TryParseResponse(response, out payload, out _)) return true;
        }

        error = "input reports contained no battery reply";
        return false;
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
        // The report id is byte 0 when the device uses one; try both layouts.
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
