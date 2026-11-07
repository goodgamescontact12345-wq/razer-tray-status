using System.Text;
using System.Windows.Forms;
using RazerTrayStatus.Hid;

namespace RazerTrayStatus;

/// <summary>
/// <c>--probe</c> prints what the app can see, so an unsupported mouse can be diagnosed
/// instead of silently showing nothing.
/// </summary>
internal static class Probe
{
    private const uint AttachParentProcess = 0xFFFFFFFF;

    public static void Run()
    {
        NativeHid.AttachConsole(AttachParentProcess);

        var store = new SettingsStore();
        string report = BuildReport(store.DirectoryPath);

        bool printed = false;
        try
        {
            using Stream stdout = Console.OpenStandardOutput();
            using var writer = new StreamWriter(stdout) { AutoFlush = true };
            writer.Write(report);
            printed = true;
        }
        catch
        {
            printed = false;
        }

        if (!printed)
        {
            string preview = report.Length > 1500 ? report[..1500] + "\n…" : report;
            MessageBox.Show(preview, "Razer tray status - probe");
        }
    }

    private static string BuildReport(string logDirectory)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Razer tray status - device probe");
        builder.AppendLine($"Time: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        builder.AppendLine();

        List<string> interfaces = DeviceScanner.DescribeAll();
        builder.AppendLine($"Razer HID interfaces present: {interfaces.Count}");
        foreach (string line in interfaces) builder.AppendLine("  " + line);

        builder.AppendLine();

        List<RazerDevice> devices = DeviceScanner.Scan();
        builder.AppendLine($"Candidate mice: {devices.Count}");

        foreach (RazerDevice device in devices)
        {
            builder.AppendLine();
            builder.AppendLine($"Device: {device.DisplayName} (PID 0x{device.ProductId:X4})");
            builder.AppendLine($"  path:    {device.DevicePath}");
            builder.AppendLine($"  serial:  {(string.IsNullOrWhiteSpace(device.Serial) ? "(none)" : device.Serial)}");
            builder.AppendLine($"  feature report length: {device.FeatureReportLength}");

            foreach (byte transactionId in RazerDevice.TransactionIds)
            {
                bool ok = device.TryProbe(transactionId, out string details);
                builder.AppendLine($"  transaction 0x{transactionId:X2}: {(ok ? "OK" : "no answer")} - {details}");
            }
        }

        if (devices.Count == 0)
        {
            builder.AppendLine();
            builder.AppendLine("No Razer HID interface with a 90-byte feature report was found.");
            builder.AppendLine("Check that the mouse/dongle is plugged in and awake, then run the probe again.");
        }

        builder.AppendLine();
        builder.AppendLine($"Log: {Path.Combine(logDirectory, "probe.log")}");

        string report = builder.ToString();
        TryWriteLog(logDirectory, report);
        return report;
    }

    private static void TryWriteLog(string directory, string report)
    {
        try
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "probe.log"), report);
        }
        catch
        {
            // Diagnostics must never throw.
        }
    }
}
