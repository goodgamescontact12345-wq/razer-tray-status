using System.Diagnostics;
using System.Windows.Forms;
using RazerTrayStatus.Hid;

namespace RazerTrayStatus;

internal sealed record DeviceSnapshot(
    string Key,
    string Name,
    int? Percent,
    bool Charging,
    byte TransactionId,
    string? Error);

internal sealed class TrayAppContext : ApplicationContext
{
    private const int MaxTooltipLength = 63;

    private readonly NotifyIcon _tray;
    private readonly ContextMenuStrip _menu = new();
    private readonly Control _anchor = new();
    private readonly SettingsStore _store = new();
    private readonly System.Windows.Forms.Timer _pollTimer = new();
    private readonly Dictionary<string, DeviceSnapshot> _snapshots = new(StringComparer.OrdinalIgnoreCase);

    private Settings _settings;
    private bool _polling;
    private Icon? _icon;
    private IntPtr _iconHandle = IntPtr.Zero;

    public TrayAppContext()
    {
        _ = _anchor.Handle; // needed so background polls can marshal back to the UI thread
        _settings = _store.Load();

        _tray = new NotifyIcon
        {
            Text = "Razer tray status",
            Visible = true,
        };
        SetTrayIcon(TrayState.NoDevice, -1);

        // No ContextMenuStrip assignment: that would make a left click open the menu too,
        // and left click must open Synapse.
        _tray.MouseClick += OnTrayMouseClick;
        _menu.Opening += (_, _) => RebuildMenu();

        ApplyPollInterval();
        _pollTimer.Tick += (_, _) => StartPoll();
        _pollTimer.Start();

        AutoStart.Apply(_settings.StartWithWindows);
        StartPoll();
    }

    private void SetTrayIcon(TrayState state, int percent)
    {
        Icon icon = TrayIconRenderer.Create(state, percent, out IntPtr handle);

        Icon? previousIcon = _icon;
        IntPtr previousHandle = _iconHandle;

        // Assign first: the shell must never be left pointing at a destroyed icon handle.
        _tray.Icon = icon;
        _icon = icon;
        _iconHandle = handle;

        previousIcon?.Dispose();
        if (previousHandle != IntPtr.Zero) NativeHid.DestroyIcon(previousHandle);
    }

    private void OnTrayMouseClick(object? sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
            OpenSynapse();
        else if (e.Button == MouseButtons.Right)
            _menu.Show(Cursor.Position);
    }

    private void OpenSynapse()
    {
        if (SynapseLauncher.TryOpen(out string error)) return;

        MessageBox.Show(
            "Razer Synapse could not be started.\n\nLooked in:\n" + SynapseLauncher.SearchSummary + "\n\n" + error,
            "Razer tray status",
            MessageBoxButtons.OK,
            MessageBoxIcon.Warning);
    }

    private void ApplyPollInterval()
    {
        int seconds = Math.Clamp(_settings.PollSeconds, 5, 3600);
        _pollTimer.Interval = seconds * 1000;
    }

    private void StartPoll()
    {
        if (_polling) return;
        _polling = true;

        // Snapshot the cached transaction ids so the worker never touches the live settings object.
        var cachedTransactionIds = new Dictionary<string, byte>(_settings.TransactionIds, StringComparer.OrdinalIgnoreCase);

        Task.Run(() =>
        {
            List<DeviceSnapshot> snapshots;
            try
            {
                snapshots = PollDevices(cachedTransactionIds);
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex);
                snapshots = new List<DeviceSnapshot>();
            }

            Post(() =>
            {
                ApplyPollResult(snapshots);
                _polling = false;
            });
        });
    }

    private void Post(Action action)
    {
        try
        {
            _anchor.BeginInvoke(action);
        }
        catch (ObjectDisposedException)
        {
            _polling = false;
        }
        catch (InvalidOperationException)
        {
            _polling = false;
        }
    }

    private static List<DeviceSnapshot> PollDevices(Dictionary<string, byte> cachedTransactionIds)
    {
        var snapshots = new List<DeviceSnapshot>();

        foreach (RazerDevice device in DeviceScanner.Scan())
        {
            byte transactionId = cachedTransactionIds.TryGetValue(device.Key, out byte cached)
                ? cached
                : RazerDevice.TransactionIds[0];

            bool ok = device.TryRead(transactionId, out int percent, out bool charging, out string error);

            if (!ok)
            {
                foreach (byte candidate in RazerDevice.TransactionIds)
                {
                    if (candidate == transactionId) continue;
                    if (!device.TryRead(candidate, out percent, out charging, out error)) continue;

                    transactionId = candidate;
                    ok = true;
                    break;
                }
            }

            snapshots.Add(new DeviceSnapshot(
                device.Key,
                device.DisplayName,
                ok ? percent : null,
                ok && charging,
                transactionId,
                ok ? null : error));
        }

        return snapshots;
    }

    private void ApplyPollResult(List<DeviceSnapshot> snapshots)
    {
        _snapshots.Clear();
        bool settingsChanged = false;

        foreach (DeviceSnapshot snapshot in snapshots)
        {
            _snapshots[snapshot.Key] = snapshot;

            if (snapshot.Error is not null) continue;
            if (_settings.TransactionIds.TryGetValue(snapshot.Key, out byte existing) && existing == snapshot.TransactionId) continue;

            _settings.TransactionIds[snapshot.Key] = snapshot.TransactionId;
            settingsChanged = true;
        }

        if (settingsChanged) _store.Save(_settings);

        UpdateTray();
    }

    private void UpdateTray()
    {
        DeviceSnapshot? pinned = null;

        if (!string.IsNullOrEmpty(_settings.PinnedDeviceKey) &&
            _snapshots.TryGetValue(_settings.PinnedDeviceKey, out DeviceSnapshot? found))
        {
            pinned = found;
        }
        else if (string.IsNullOrEmpty(_settings.PinnedDeviceKey) && _snapshots.Count == 1)
        {
            // Nothing pinned and only one mouse around: showing it is what the user expects.
            pinned = _snapshots.Values.First();
        }

        TrayState state;
        int percent = -1;
        string text;

        if (pinned is null)
        {
            if (!string.IsNullOrEmpty(_settings.PinnedDeviceKey))
            {
                string pinnedName = string.IsNullOrWhiteSpace(_settings.PinnedDeviceName)
                    ? "Pinned mouse"
                    : _settings.PinnedDeviceName;

                state = TrayState.Disconnected;
                text = pinnedName + ": not connected";
            }
            else
            {
                state = TrayState.NoDevice;
                text = _snapshots.Count == 0
                    ? "No Razer mouse detected"
                    : "Razer mouse: pick one in the menu";
            }
        }
        else if (pinned.Percent is null)
        {
            state = TrayState.Asleep;
            text = $"{pinned.Name}: not responding (idle or off)";
        }
        else
        {
            percent = pinned.Percent.Value;
            state = pinned.Charging
                ? TrayState.Charging
                : percent <= 5
                    ? TrayState.Critical
                    : percent <= _settings.LowBatteryPercent
                        ? TrayState.Low
                        : TrayState.Normal;

            text = $"{pinned.Name}: {percent}%" + (pinned.Charging ? " (charging)" : string.Empty);
        }

        _tray.Text = Truncate(text, MaxTooltipLength);
        SetTrayIcon(state, percent);
    }

    private void PinDevice(string key, string name)
    {
        _settings.PinnedDeviceKey = key;
        _settings.PinnedDeviceName = name;
        _store.Save(_settings);
        UpdateTray();
    }

    private void RebuildMenu()
    {
        _menu.Items.Clear();

        if (_snapshots.Count == 0)
        {
            _menu.Items.Add(new ToolStripMenuItem(_settings.PinnedDeviceName is { Length: > 0 } pinnedName
                ? $"No mouse connected ({pinnedName})"
                : "No Razer mouse detected")
            {
                Enabled = false,
            });
        }
        else
        {
            foreach (DeviceSnapshot snapshot in _snapshots.Values
                         .OrderBy(s => s.Name, StringComparer.CurrentCultureIgnoreCase))
            {
                string status = snapshot.Percent is int value ? $"{value}%" : "—";
                if (snapshot.Charging) status += " (charging)";

                var item = new ToolStripMenuItem($"{status}   {snapshot.Name}")
                {
                    Checked = string.Equals(snapshot.Key, _settings.PinnedDeviceKey, StringComparison.OrdinalIgnoreCase),
                    CheckOnClick = false,
                };

                string key = snapshot.Key;
                string name = snapshot.Name;
                item.Click += (_, _) => PinDevice(key, name);
                _menu.Items.Add(item);
            }
        }

        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(Action("Open Razer Synapse", OpenSynapse));
        _menu.Items.Add(Action("Refresh now", StartPoll));
        _menu.Items.Add(PollIntervalMenu());
        _menu.Items.Add(LowBatteryMenu());

        var startWithWindows = new ToolStripMenuItem("Start with Windows")
        {
            Checked = _settings.StartWithWindows,
            CheckOnClick = true,
        };
        startWithWindows.Click += (_, _) =>
        {
            _settings.StartWithWindows = startWithWindows.Checked;
            AutoStart.Apply(startWithWindows.Checked);
            _store.Save(_settings);
        };
        _menu.Items.Add(startWithWindows);

        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(Action("Exit", ExitThread));
    }

    private ToolStripMenuItem PollIntervalMenu()
    {
        var root = new ToolStripMenuItem("Refresh every");

        foreach (int seconds in new[] { 15, 30, 60, 300 })
        {
            int value = seconds;
            string label = seconds < 60
                ? $"{seconds} seconds"
                : seconds == 60
                    ? "1 minute"
                    : $"{seconds / 60} minutes";

            var item = new ToolStripMenuItem(label) { Checked = _settings.PollSeconds == value };
            item.Click += (_, _) =>
            {
                _settings.PollSeconds = value;
                ApplyPollInterval();
                _store.Save(_settings);
            };
            root.DropDownItems.Add(item);
        }

        return root;
    }

    private ToolStripMenuItem LowBatteryMenu()
    {
        var root = new ToolStripMenuItem("Low battery at");

        foreach (int percent in new[] { 10, 15, 20, 25, 30 })
        {
            int value = percent;
            var item = new ToolStripMenuItem($"{value}%") { Checked = _settings.LowBatteryPercent == value };
            item.Click += (_, _) =>
            {
                _settings.LowBatteryPercent = value;
                _store.Save(_settings);
                UpdateTray();
            };
            root.DropDownItems.Add(item);
        }

        return root;
    }

    private static ToolStripMenuItem Action(string text, Action action)
    {
        var item = new ToolStripMenuItem(text);
        item.Click += (_, _) => action();
        return item;
    }

    private static string Truncate(string value, int length) =>
        value.Length <= length ? value : value[..(length - 1)] + "…";

    protected override void ExitThreadCore()
    {
        _pollTimer.Stop();
        _pollTimer.Dispose();

        _tray.Visible = false;
        _tray.Dispose();
        _menu.Dispose();

        _icon?.Dispose();
        if (_iconHandle != IntPtr.Zero) NativeHid.DestroyIcon(_iconHandle);
        _iconHandle = IntPtr.Zero;

        _anchor.Dispose();

        base.ExitThreadCore();
    }
}
