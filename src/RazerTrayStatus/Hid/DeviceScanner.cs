using System.Runtime.InteropServices;

namespace RazerTrayStatus.Hid;

internal static class DeviceScanner
{
    private const int MinFeatureReportLength = 90;

    private sealed record Candidate(
        string Path,
        string Product,
        string Serial,
        ushort ProductId,
        ushort UsagePage,
        ushort Usage,
        int FeatureReportLength);

    /// <summary>Finds every Razer mouse that exposes the 90-byte control report.</summary>
    public static List<RazerDevice> Scan()
    {
        List<Candidate> candidates = ReadCandidates();

        // Preferred: the mouse collection. Some models only answer on a vendor-defined collection,
        // so fall back to any interface that can carry a 90-byte feature report.
        List<Candidate> matches = candidates
            .Where(c => c.UsagePage == 0x01 && c.Usage == 0x02 && c.FeatureReportLength >= MinFeatureReportLength)
            .ToList();

        if (matches.Count == 0)
            matches = candidates.Where(c => c.FeatureReportLength >= MinFeatureReportLength).ToList();

        var devices = new List<RazerDevice>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (Candidate candidate in matches)
        {
            var device = new RazerDevice(
                candidate.Path,
                candidate.Product,
                candidate.Serial,
                candidate.ProductId,
                candidate.FeatureReportLength);

            // A dongle can expose the control report on more than one collection; keep one per device.
            if (seen.Add(device.Key)) devices.Add(device);
        }

        return devices;
    }

    /// <summary>Human-readable list of every Razer HID interface, for --probe.</summary>
    public static List<string> DescribeAll()
    {
        var lines = new List<string>();
        foreach (Candidate candidate in ReadCandidates())
        {
            lines.Add(
                $"PID 0x{candidate.ProductId:X4}  usagePage 0x{candidate.UsagePage:X2} usage 0x{candidate.Usage:X2}  " +
                $"feature {candidate.FeatureReportLength}B  \"{candidate.Product}\"  serial=\"{candidate.Serial}\"");
        }
        return lines;
    }

    private static List<Candidate> ReadCandidates()
    {
        var candidates = new List<Candidate>();

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

                string product = NativeHid.ReadString(handle, NativeHid.HidD_GetProductString);
                string serial = NativeHid.ReadString(handle, NativeHid.HidD_GetSerialNumberString);

                candidates.Add(new Candidate(
                    path,
                    product,
                    serial,
                    attributes.ProductID,
                    caps.UsagePage,
                    caps.Usage,
                    caps.FeatureReportByteLength));
            }
            catch
            {
                // A single inaccessible interface must never break discovery.
            }
            finally
            {
                NativeHid.CloseHandle(handle);
            }
        }

        return candidates;
    }
}
