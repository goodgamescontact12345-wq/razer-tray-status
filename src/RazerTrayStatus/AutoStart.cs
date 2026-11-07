using Microsoft.Win32;

namespace RazerTrayStatus;

internal static class AutoStart
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "RazerTrayStatus";

    public static void Apply(bool enabled)
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            if (key is null) return;

            if (enabled)
            {
                string? executable = Environment.ProcessPath;
                if (string.IsNullOrEmpty(executable)) return;
                key.SetValue(ValueName, $"\"{executable}\"");
            }
            else
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            }
        }
        catch
        {
            // Startup registration is cosmetic; ignore policy/lock failures.
        }
    }
}
