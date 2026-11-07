using System.Text.Json;

namespace RazerTrayStatus;

internal sealed class Settings
{
    /// <summary>Stable device key (serial) of the mouse pinned to the tray.</summary>
    public string? PinnedDeviceKey { get; set; }

    /// <summary>Cached display name, so the tray can still name a pinned mouse while it is plugged out.</summary>
    public string? PinnedDeviceName { get; set; }

    public int PollSeconds { get; set; } = 60;

    public int LowBatteryPercent { get; set; } = 15;

    public bool StartWithWindows { get; set; }

    /// <summary>Working protocol transaction id per device, learned on first successful read.</summary>
    public Dictionary<string, byte> TransactionIds { get; set; } = new();
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
            System.IO.Directory.CreateDirectory(DirectoryPath);
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
}
