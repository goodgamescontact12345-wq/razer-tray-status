using System.Text;
using System.Windows.Forms;
using RazerTrayStatus.Hid;

namespace RazerTrayStatus;

/// <summary>
/// Diagnostics behind <c>--probe</c> and the tray's "Device diagnostics…" item. It reports what is
/// present and what each collection answers, so a mouse that reads as "no battery answer" can be
/// diagnosed instead of simply not working.
/// </summary>
internal static class Probe
{
    private const uint AttachParentProcess = 0xFFFFFFFF;

    public static string LogPath => Path.Combine(new SettingsStore().DirectoryPath, "probe.log");

    public static void Run()
    {
        NativeHid.AttachConsole(AttachParentProcess);

        string report = BuildReport();

        try
        {
            using Stream stdout = Console.OpenStandardOutput();
            using var writer = new StreamWriter(stdout) { AutoFlush = true };
            writer.Write(report);
        }
        catch
        {
            string preview = report.Length > 1500 ? report[..1500] + "\n…" : report;
            MessageBox.Show(preview, "Razer tray status - diagnostics");
        }
    }

    public static string BuildReport()
    {
        var builder = new StringBuilder();
        builder.AppendLine("Razer tray status - diagnostics");
        builder.AppendLine($"Time: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        builder.AppendLine($"OS:   {Environment.OSVersion}");
        builder.AppendLine();

        List<string> interfaces = DeviceScanner.DescribeAll();
        builder.AppendLine($"Razer HID collections present: {interfaces.Count}");
        foreach (string line in interfaces) builder.AppendLine("  " + line);

        builder.AppendLine();

        List<RazerMouse> mice = DeviceScanner.Scan(new Dictionary<string, string>());
        builder.AppendLine($"Mouse groups: {mice.Count}");

        foreach (RazerMouse mouse in mice)
        {
            builder.AppendLine();
            builder.AppendLine($"Mouse: {mouse.DisplayName}  (key: {mouse.Key})");

            foreach (RazerDevice device in mouse.Interfaces)
            {
                builder.AppendLine($"  collection: {device.DevicePath}");
                builder.AppendLine(
                    $"    usagePage 0x{device.UsagePage:X2} usage 0x{device.Usage:X2} " +
                    $"feature {device.FeatureReportLength}B mouseCollection={device.IsMouseCollection}");

                foreach (byte transactionId in RazerDevice.TransactionIds)
                {
                    bool ok = device.TryProbe(transactionId, out string details);
                    builder.AppendLine($"    transaction 0x{transactionId:X2}: {(ok ? "OK" : "no answer")} - {details}");
                }
            }
        }

        if (mice.Count == 0)
        {
            builder.AppendLine();
            builder.AppendLine("No Razer mouse collection was found at all.");
            builder.AppendLine("Check that the mouse or its dongle is plugged in, then run diagnostics again.");
        }

        builder.AppendLine();
        builder.AppendLine($"Log: {LogPath}");

        string report = builder.ToString();
        TryWriteLog(report);
        return report;
    }

    private static void TryWriteLog(string report)
    {
        try
        {
            Directory.CreateDirectory(new SettingsStore().DirectoryPath);
            File.WriteAllText(LogPath, report);
        }
        catch
        {
            // Diagnostics must never throw.
        }
    }
}
