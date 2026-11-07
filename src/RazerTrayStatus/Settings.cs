using System.Text.Json;

namespace RazerTrayStatus;

/// <summary>What worked for one device, remembered so later polls need a single round trip.</summary>
internal sealed class DevicePreference
{
    /// <summary>HID collection that answered.</summary>
    public string? ControlPath { get; set; }

    /// <summary>Razer protocol transaction id that answered.</summary>
    public byte TransactionId { get; set; }

    /// <summary>I/O strategy that answered: Feature or OutputInput.</summary>
    public string? IoMode { get; set; }
}

internal sealed class Settings
{
    /// <summary>Stable device key (serial) of the mouse pinned to the tray.</summary>
    public string? PinnedDeviceKey { get; set; }

    /// <summary>Cached display name, so the tray can still name a pinned mouse while it is plugged out.</summary>
    public string? PinnedDeviceName { get; set; }

    public int PollSeconds { get; set; } = 60;

    public int LowBatteryPercent { get; set; } = 15;

    public bool StartWithWindows { get; set; }

    /// <summary>Learned working strategy per device.</summary>
    public Dictionary<string, DevicePreference> Preferences { get; set; } = new();
}

internal sealed class SettingsStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

    public string DirectoryPath { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RazerTrayStatus");

    private string FilePath => Path.Combine(DirectoryPath, "settings.json");

    public Settings Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return new Settings();
            return JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? new Settings();
        }
        catch
        {
            return new Settings();
        }
    }

    public void Save(Settings settings)
    {
        try
        {
            Directory.CreateDirectory(DirectoryPath);
            string json = JsonSerializer.Serialize(settings, SerializerOptions);
            string temporary = FilePath + ".tmp";
            File.WriteAllText(temporary, json);
            File.Move(temporary, FilePath, true);
        }
        catch
        {
            // Settings are a convenience: never crash the tray over a failed write.
        }
    }

    /// <summary>Appends a line to the runtime log (kept small, only written on problems).</summary>
    public void Log(string message)
    {
        try
        {
            Directory.CreateDirectory(DirectoryPath);
            File.AppendAllText(
                Path.Combine(DirectoryPath, "agent.log"),
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {message}{Environment.NewLine}");
        }
        catch
        {
            // Logging must never throw.
        }
    }
}
