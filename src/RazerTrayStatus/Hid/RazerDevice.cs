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
/// Protocol (reverse engineered by OpenRazer):
///   90-byte report: [0]=status [1]=transaction id [5]=data size [6]=command class
///   [7]=command id [8..]=arguments [88]=CRC (XOR of bytes 2..87)
///   Battery: class 0x07 id 0x80, args[1] is 0..255 -> percent
///   Charging: class 0x07 id 0x84, args[1] is 0/1
///
/// Reply statuses: 0x00 new, **0x01 busy (read again)**, 0x02 success, 0x03 failure,
/// 0x04 timeout, 0x05 not supported. The reply is never ready immediately: OpenRazer waits
/// ~31 ms and then re-reads while the device reports busy, which is what this class does.
/// </summary>
internal sealed class RazerDevice
{
    public const ushort RazerVendorId = 0x1532;

    private const byte MiscCommandClass = 0x07;
    private const byte GetBatteryLevel = 0x80;
    private const byte GetChargingStatus = 0x84;

    private const int ReportLength = 90;
    private const int MinReportLength = 91;

    private const byte StatusNewCommand = 0x00;
    private const byte StatusBusy = 0x01;
    private const byte StatusSuccess = 0x02;
    private const byte StatusFailure = 0x03;
    private const byte StatusTimeout = 0x04;
    private const byte StatusNotSupported = 0x05;

    private const int ResponseDelayMilliseconds = 30;
    private const int BusyRetryDelayMilliseconds = 10;
    private const int BusyRetryAttempts = 10;
    private const int InputReportAttempts = 5;

    private enum ResponseState
    {
        Success,
        Busy,
        Failed,
        NoReply,
    }

    /// <summary>Transaction ids to try, in order: modern mice, older receivers, then the legacy default.</summary>
    public static readonly byte[] TransactionIds = { 0x1F, 0x3F, 0xFF };

    public RazerDevice(
        string devicePath,
        string productName,
        string serial,
        ushort productId,
        int featureReportLength,
        int inputReportLength,
        int outputReportLength,
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
        InputReportByteLength = inputReportLength;
        OutputReportByteLength = outputReportLength;
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
    public int InputReportByteLength { get; }
    public int OutputReportByteLength { get; }
    public ushort UsagePage { get; }
    public ushort Usage { get; }

    /// <summary>False when the collection could not be opened at all; it is still listed and retried.</summary>
    public bool Opened { get; }

    public string? OpenError { get; }

    /// <summary>True for the collection Windows itself uses as a mouse (usage page 1, usage 2).</summary>
    public bool IsMouseCollection => UsagePage == 0x01 && Usage == 0x02;

    /// <summary>Can this collection carry a 90-byte Razer report, by feature or by input/output?</summary>
    public bool CanCarryControlReport =>
        FeatureReportLength >= MinReportLength ||
        (InputReportByteLength >= MinReportLength && OutputReportByteLength >= MinReportLength);

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
            if (!Supports(mode))
            {
                lastError = DescribeMissingChannel(mode);
                continue;
            }

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
                        if (TryQuery(handle, mode, transactionId, out int percent, out bool charging, out string error))
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
            if (!Supports(mode))
            {
                lines.Add($"    {mode}: skipped - {DescribeMissingChannel(mode)}");
                continue;
            }

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

    private bool Supports(IoMode mode) => mode == IoMode.Feature
        ? FeatureReportLength >= MinReportLength
        : InputReportByteLength >= MinReportLength && OutputReportByteLength >= MinReportLength;

    private string DescribeMissingChannel(IoMode mode) => mode == IoMode.Feature
        ? $"no {MinReportLength}-byte feature report (feature {FeatureReportLength}B)"
        : $"no {MinReportLength}-byte input/output reports (in {InputReportByteLength}B, out {OutputReportByteLength}B)";

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

        int length = Math.Max(MinReportLength, FeatureReportLength);
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

            // The reply is never ready immediately; OpenRazer waits about 31 ms before reading.
            Thread.Sleep(ResponseDelayMilliseconds);

            for (int attempt = 1; attempt <= BusyRetryAttempts; attempt++)
            {
                var response = new byte[length];
                response[0] = 0x00; // report id, required on input

                if (!NativeHid.HidD_GetFeature(handle, response, length))
                {
                    error = $"GetFeature failed ({NativeHid.DescribeError(Marshal.GetLastWin32Error())})";
                    return false;
                }

                switch (Evaluate(response, commandId, out payload, out error))
                {
                    case ResponseState.Success:
                        return true;

                    case ResponseState.Busy:
                        if (attempt == BusyRetryAttempts)
                        {
                            error = "device stayed busy (status 0x01) after " + BusyRetryAttempts + " reads";
                            return false;
                        }

                        Thread.Sleep(BusyRetryDelayMilliseconds);
                        continue;

                    default:
                        return false;
                }
            }

            return false;
        }

        if (!NativeHid.WriteOutputReport(handle, request, length, NativeHid.IoTimeoutMilliseconds, out string writeError))
        {
            error = writeError;
            return false;
        }

        Thread.Sleep(ResponseDelayMilliseconds);

        for (int attempt = 1; attempt <= InputReportAttempts; attempt++)
        {
            var response = new byte[length];

            int read = NativeHid.ReadInputReport(handle, response, NativeHid.IoTimeoutMilliseconds, out string readError);
            if (read <= 0)
            {
                error = readError;
                continue;
            }

            switch (Evaluate(response, commandId, out payload, out error))
            {
                case ResponseState.Success:
                    return true;

                case ResponseState.Busy:
                    Thread.Sleep(BusyRetryDelayMilliseconds);
                    continue;

                default:
                    return false;
            }
        }

        if (string.IsNullOrEmpty(error)) error = "no usable input report";
        return false;
    }

    /// <summary>Interprets a reply: success only on status 0x02 with a matching command echo.</summary>
    private static ResponseState Evaluate(byte[] response, byte commandId, out byte[] payload, out string error)
    {
        payload = Array.Empty<byte>();
        error = string.Empty;

        if (!TryLocatePayload(response, out int offset, out byte status))
        {
            error = "no Razer reply in the report";
            return ResponseState.NoReply;
        }

        if (status is StatusBusy or StatusNewCommand)
        {
            error = $"device busy (status 0x{status:X2})";
            return ResponseState.Busy;
        }

        if (status != StatusSuccess)
        {
            error = DescribeStatus(status);
            return ResponseState.Failed;
        }

        var parsed = new byte[ReportLength];
        Array.Copy(response, offset, parsed, 0, ReportLength);

        // Guard against a stale reply to an earlier command.
        if (parsed[6] != MiscCommandClass || (parsed[7] & 0x7F) != (commandId & 0x7F))
        {
            error = $"reply does not match the request (class 0x{parsed[6]:X2} id 0x{parsed[7]:X2})";
            return ResponseState.Busy;
        }

        payload = parsed;
        return ResponseState.Success;
    }

    /// <summary>Finds the 90-byte payload, allowing for the optional report id at byte 0.</summary>
    private static bool TryLocatePayload(byte[] response, out int offset, out byte status)
    {
        if (response.Length >= MinReportLength && response[0] == 0x00 && IsKnownStatus(response[1]))
        {
            offset = 1;
            status = response[1];
            return true;
        }

        if (response.Length > 0 && IsKnownStatus(response[0]))
        {
            offset = 0;
            status = response[0];
            return true;
        }

        offset = 0;
        status = 0xFF;
        return false;
    }

    private static bool IsKnownStatus(byte value) => value <= StatusNotSupported;

    private static string DescribeStatus(byte status) => status switch
    {
        StatusFailure => "device reported failure (status 0x03)",
        StatusTimeout => "device timed out (status 0x04) - it could not be reached",
        StatusNotSupported => "command not supported by this device (status 0x05)",
        _ => $"unexpected status 0x{status:X2}",
    };

    public static byte[] BuildRequest(byte transactionId, byte commandClass, byte commandId, byte dataSize = 0x02)
    {
        var frame = new byte[ReportLength];
        frame[0] = StatusNewCommand;
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
}
