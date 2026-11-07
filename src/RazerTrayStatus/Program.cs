using System.Windows.Forms;

namespace RazerTrayStatus;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        if (args.Any(argument => argument.Equals("--probe", StringComparison.OrdinalIgnoreCase)))
        {
            Probe.Run();
            return;
        }

        using var mutex = new Mutex(initiallyOwned: true, @"Local\RazerTrayStatus.SingleInstance", out bool isFirstInstance);
        if (!isFirstInstance) return;

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.Run(new TrayAppContext());
    }
}
