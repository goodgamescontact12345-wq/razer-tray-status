namespace RazerTrayStatus.Hid;

/// <summary>
/// One physical Razer mouse, with every HID collection it exposes ordered by how likely it is to
/// carry the battery command. The first collection that answers is remembered and tried first next
/// time, which keeps reads to a single round trip on later polls.
/// </summary>
internal sealed class RazerMouse
{
    public RazerMouse(string key, string displayName, List<RazerDevice> interfaces)
    {
        Key = key;
        DisplayName = displayName;
        Interfaces = interfaces;
    }

    /// <summary>Stable identity for pinning: the serial, or the model when the device reports none.</summary>
    public string Key { get; }

    public string DisplayName { get; }

    public IReadOnlyList<RazerDevice> Interfaces { get; }
}
