using System.Runtime.InteropServices;

namespace RazerTrayStatus.Hid;

internal static class DeviceScanner
{
    private const int MinControlReportLength = 90;

    /// <summary>
    /// Finds Razer mice, grouped per physical device, each with its HID collections ranked.
    /// <paramref name="preferredControlPaths"/> holds the collection that answered last time.
    /// </summary>
    public static List<RazerMouse> Scan(Dictionary<string, string> preferredControlPaths)
    {
        List<RazerDevice> interfaces = EnumerateInterfaces();

        // Normally the battery channel advertises a Razer-sized feature report. If the connected
        // mouse advertises none (some wired/charging states), still surface it as a mouse so the
        // user sees the device instead of nothing at all.
        List<RazerDevice> candidates = interfaces
            .Where(device => device.FeatureReportLength >= MinControlReportLength)
            .ToList();

        if (candidates.Count == 0)
            candidates = interfaces.Where(device => device.IsMouseCollection).ToList();

        var groups = new Dictionary<string, List<RazerDevice>>(StringComparer.OrdinalIgnoreCase);

        foreach (RazerDevice device in candidates)
        {
            string key = GroupKeyFor(device);
            if (!groups.TryGetValue(key, out List<RazerDevice>? group))
                groups[key] = group = new List<RazerDevice>();
            group.Add(device);
        }

        var mice = new List<RazerMouse>();

        foreach (KeyValuePair<string, List<RazerDevice>> group in groups)
        {
            preferredControlPaths.TryGetValue(group.Key, out string? preferredPath);
            group.Value.Sort((left, right) => Rank(left, preferredPath).CompareTo(Rank(right, preferredPath)));

            mice.Add(new RazerMouse(group.Key, group.Value[0].DisplayName, group.Value));
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
                $"feature {device.FeatureReportLength}B  \"{device.DisplayName}\"  serial=\"{device.Serial}\"");
        }

        return lines;
    }

    private static string GroupKeyFor(RazerDevice device)
    {
        // The serial is the stable identity. Falling back to the model keeps one mouse's several
        // HID collections from turning into several menu rows.
        if (!string.IsNullOrWhiteSpace(device.Serial)) return device.Serial.Trim();
        return $"{device.ProductId:X4}:{device.DisplayName}";
    }

    private static int Rank(RazerDevice device, string? preferredPath)
    {
        int score = 0;

        // Remembered control collection always wins.
        if (!string.IsNullOrEmpty(preferredPath) &&
            string.Equals(device.DevicePath, preferredPath, StringComparison.OrdinalIgnoreCase))
        {
            score -= 1000;
        }

        if (device.IsMouseCollection) score -= 10;
        score -= Math.Min(device.FeatureReportLength, 4096) / 16;

        return score;
    }

    private static List<RazerDevice> EnumerateInterfaces()
    {
        var devices = new List<RazerDevice>();

        foreach (string path in NativeHid.EnumerateHidDevicePaths())
        {
            IntPtr handle = NativeHid.OpenDevice(path);
            if (handle == NativeHid.InvalidHandle) continue;

            try
            {
                var attributes = new NativeHid.HIDD_ATTRIBUTES
                {
                    Size = Marshal.SizeOf<NativeHid.HIDD_ATTRIBUTES>(),
                };

                if (!NativeHid.HidD_GetAttributes(handle, ref attributes)) continue;
                if (attributes.VendorID != RazerDevice.RazerVendorId) continue;

                if (!NativeHid.HidD_GetPreparsedData(handle, out IntPtr preparsed)) continue;

                NativeHid.HIDP_CAPS caps;
                try
                {
                    caps = NativeHid.HIDP_CAPS.Create();
                    if (NativeHid.HidP_GetCaps(preparsed, ref caps) != NativeHid.HidpStatusSuccess) continue;
                }
                finally
                {
                    NativeHid.HidD_FreePreparsedData(preparsed);
                }

                devices.Add(new RazerDevice(
                    path,
                    NativeHid.ReadString(handle, NativeHid.HidD_GetProductString),
                    NativeHid.ReadString(handle, NativeHid.HidD_GetSerialNumberString),
                    attributes.ProductID,
                    caps.FeatureReportByteLength,
                    caps.UsagePage,
                    caps.Usage));
            }
            catch
            {
                // A single inaccessible collection must never break discovery.
            }
            finally
            {
                NativeHid.CloseHandle(handle);
            }
        }

        return devices;
    }
}
