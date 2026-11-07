using System.Globalization;
using System.Runtime.InteropServices;

namespace RazerTrayStatus.Hid;

internal static class DeviceScanner
{
    private const int MinControlReportLength = 90;

    /// <summary>
    /// Finds Razer devices from the HID interface paths, so detection never depends on being able
    /// to open a collection (a mouse collection is often locked by the mouse driver). Each device is
    /// returned with all of its collections ranked, best candidate first.
    /// </summary>
    public static List<RazerMouse> Scan(Dictionary<string, DevicePreference> preferences)
    {
        var groups = new Dictionary<string, List<RazerDevice>>(StringComparer.OrdinalIgnoreCase);

        foreach (RazerDevice device in EnumerateInterfaces())
        {
            string key = GroupKeyFor(device);
            if (!groups.TryGetValue(key, out List<RazerDevice>? group))
                groups[key] = group = new List<RazerDevice>();
            group.Add(device);
        }

        var mice = new List<RazerMouse>();

        foreach (KeyValuePair<string, List<RazerDevice>> group in groups)
        {
            preferences.TryGetValue(group.Key, out DevicePreference? preference);
            string? preferredPath = preference?.ControlPath;

            group.Value.Sort((left, right) => Rank(left, preferredPath).CompareTo(Rank(right, preferredPath)));

            // Prefer a real product name from any collection; some only report the id.
            string? productName = group.Value
                .Select(device => device.ProductName)
                .FirstOrDefault(name => !string.IsNullOrWhiteSpace(name));

            string displayName = string.IsNullOrWhiteSpace(productName)
                ? group.Value[0].DisplayName
                : productName.Trim();

            mice.Add(new RazerMouse(group.Key, displayName, group.Value));
        }

        return mice
            .OrderBy(mouse => mouse.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    /// <summary>Human-readable list of every Razer HID collection, for diagnostics.</summary>
    public static List<string> DescribeAll()
    {
        var lines = new List<string>();

        foreach (RazerDevice device in EnumerateInterfaces())
        {
            lines.Add(
                $"PID 0x{device.ProductId:X4}  usagePage 0x{device.UsagePage:X2} usage 0x{device.Usage:X2}  " +
                $"feature {device.FeatureReportLength}B in {device.InputReportByteLength}B out {device.OutputReportByteLength}B  " +
                $"mouse={device.IsMouseCollection}  " +
                $"\"{device.DisplayName}\"  serial=\"{device.Serial}\"" +
                (device.Opened ? string.Empty : $"  OPEN FAILED: {device.OpenError}") +
                $"\n      {device.DevicePath}");
        }

        return lines;
    }

    /// <summary>Total number of HID collections Windows reported, for diagnostics context.</summary>
    public static int CountHidInterfaces() => NativeHid.EnumerateHidDevicePaths(NativeHid.GetHidInterfaceGuid()).Count;

    private static string GroupKeyFor(RazerDevice device)
    {
        // Grouped by product id: one mouse exposes many collections, and some of them cannot be
        // read at all, so a serial would split one mouse into several menu rows. Two mice of the
        // same model therefore share a row; different models stay separate.
        return device.ProductId.ToString("X4", CultureInfo.InvariantCulture);
    }

    private static int Rank(RazerDevice device, string? preferredPath)
    {
        int score = 0;

        if (!string.IsNullOrEmpty(preferredPath) &&
            string.Equals(device.DevicePath, preferredPath, StringComparison.OrdinalIgnoreCase))
        {
            score -= 1000; // the collection that answered last time always wins
        }

        if (!device.Opened) score += 500; // prefer one we can actually talk to
        if (device.IsMouseCollection) score -= 10;
        if (device.CanCarryControlReport) score -= 30;
        if (device.FeatureReportLength >= MinControlReportLength) score -= 20;
        score -= Math.Min(device.FeatureReportLength, 4096) / 16;

        return score;
    }

    private static List<RazerDevice> EnumerateInterfaces()
    {
        var devices = new List<RazerDevice>();
        Guid hidGuid = NativeHid.GetHidInterfaceGuid();

        foreach (string path in NativeHid.EnumerateHidDevicePaths(hidGuid))
        {
            // Identify Razer hardware from the interface path itself: this works even when the
            // collection is locked and cannot be opened.
            if (!TryParseVidPid(path, out ushort vendorId, out ushort productId)) continue;
            if (vendorId != RazerDevice.RazerVendorId) continue;

            string product = string.Empty;
            string serial = string.Empty;
            int featureReportLength = 0;
            int inputReportLength = 0;
            int outputReportLength = 0;
            ushort usagePage = 0;
            ushort usage = 0;
            bool opened = false;
            string? openError = null;

            IntPtr handle = NativeHid.OpenDevice(path, HidAccess.None);
            if (handle == NativeHid.InvalidHandle)
            {
                int firstError = Marshal.GetLastWin32Error();
                handle = NativeHid.OpenDevice(path, HidAccess.ReadWrite);
                if (handle == NativeHid.InvalidHandle)
                    openError = NativeHid.DescribeError(firstError);
            }

            if (handle != NativeHid.InvalidHandle)
            {
                opened = true;

                try
                {
                    product = NativeHid.ReadString(handle, NativeHid.HidD_GetProductString);
                    serial = NativeHid.ReadString(handle, NativeHid.HidD_GetSerialNumberString);

                    if (NativeHid.HidD_GetPreparsedData(handle, out IntPtr preparsed))
                    {
                        try
                        {
                            NativeHid.HIDP_CAPS caps = NativeHid.HIDP_CAPS.Create();
                            if (NativeHid.HidP_GetCaps(preparsed, ref caps) == NativeHid.HidpStatusSuccess)
                            {
                                featureReportLength = caps.FeatureReportByteLength;
                                inputReportLength = caps.InputReportByteLength;
                                outputReportLength = caps.OutputReportByteLength;
                                usagePage = caps.UsagePage;
                                usage = caps.Usage;
                            }
                        }
                        finally
                        {
                            NativeHid.HidD_FreePreparsedData(preparsed);
                        }
                    }
                }
                catch
                {
                    // Descriptors are optional detail; the path already told us it is Razer hardware.
                }
                finally
                {
                    NativeHid.CloseHandle(handle);
                }
            }

            devices.Add(new RazerDevice(
                path,
                product,
                serial,
                productId,
                featureReportLength,
                inputReportLength,
                outputReportLength,
                usagePage,
                usage,
                opened,
                openError));
        }

        return devices;
    }

    /// <summary>HID interface paths look like \\?\hid#vid_1532&amp;pid_00c1&amp;mi_02&amp;col02#...</summary>
    private static bool TryParseVidPid(string path, out ushort vendorId, out ushort productId)
    {
        vendorId = 0;
        productId = 0;

        return TryParseHexWord(path, "vid_", out vendorId) && TryParseHexWord(path, "pid_", out productId);
    }

    private static bool TryParseHexWord(string text, string marker, out ushort value)
    {
        value = 0;

        int index = text.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (index < 0 || index + marker.Length + 4 > text.Length) return false;

        return ushort.TryParse(
            text.AsSpan(index + marker.Length, 4),
            NumberStyles.HexNumber,
            CultureInfo.InvariantCulture,
            out value);
    }
}
