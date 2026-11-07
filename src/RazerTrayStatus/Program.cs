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

        if (!isFirstInstance)
        {
            // Silently exiting here would look exactly like "the new build does not work", because
            // the old instance stays in the tray. Say so instead.
            MessageBox.Show(
                "Razer tray status is already running.\n\nLook for its icon in the notification area " +
                "(click the ^ arrow next to the clock).\n\nRight-click that icon and choose Exit before " +
                "starting a different build.",
                "Razer tray status",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);

        var store = new SettingsStore();

        // Never die silently: a WinExe has no console, so surface failures and keep a log.
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, eventArgs) => ReportCrash(store, eventArgs.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, eventArgs) => ReportCrash(store, eventArgs.ExceptionObject as Exception);

        Application.Run(new TrayAppContext());
    }

    private static void ReportCrash(SettingsStore store, Exception? exception)
    {
        store.Log("crash: " + exception);

        MessageBox.Show(
            "Razer tray status hit an error:\n\n" + exception?.Message + "\n\nDetails: " +
            Path.Combine(store.DirectoryPath, "agent.log"),
            "Razer tray status",
            MessageBoxButtons.OK,
            MessageBoxIcon.Error);
    }
}
