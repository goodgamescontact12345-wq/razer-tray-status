# razer-tray-status

A small Windows tray app that shows the battery level of your Razer wireless mice, with support
for more than one mouse and a single pinned device in the tray. **Left-clicking the tray icon opens
Razer Synapse.**

Battery levels are read straight from the mouse over USB HID (the protocol reverse-engineered by
[OpenRazer](https://github.com/openrazer/openrazer)). Synapse does not need to be running to read
the level — it only opens when you click the icon.

## Features

- Every connected Razer mouse is listed in the tray menu with its live battery percentage.
- Pin any one of them to the tray icon; the pin survives restarts and replugs.
- Left click the tray icon → opens Razer Synapse (Synapse 4, falling back to Synapse 3).
- Right click → device list, refresh, poll interval, low-battery threshold, start with Windows, exit.
- Icon changes colour and fill level: normal, charging (bolt), low, critical, idle/unreachable.
- No drivers, no admin rights, no Synapse dependency for reading, no bundled image assets.
- Settings live in `%APPDATA%\RazerTrayStatus\settings.json`.

## Requirements

- Windows 10 or 11, 64-bit.
- A Razer wireless mouse or its 2.4 GHz dongle plugged in.
- Razer Synapse 4 or 3 installed if you want the click-through to work.

## Build

Requires the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).

```bat
build.cmd
```

That produces a single self-contained `dist\RazerTrayStatus.exe` — no .NET runtime needed on the
machine that runs it. Or run it directly from source:

```bat
dotnet run --project src\RazerTrayStatus
```

## Usage

1. Start `RazerTrayStatus.exe`. If exactly one mouse is connected it is shown immediately.
2. Right-click the tray icon and click a mouse to pin it to the tray.
3. Hover the icon for the exact percentage; left-click it to open Razer Synapse.
4. Use `Refresh now` to force a read, and `Refresh every` to change the polling interval.

Only `Refresh every` values of 15 s, 30 s, 1 min and 5 min are offered; the default is 1 minute.

## Supported mice

Any Razer wireless mouse that exposes the 90-byte HID control report on a mouse collection
(usage page `0x01`, usage `0x02`) works: typical 2021+ models answer on transaction `0x1F`, older
receivers on `0x3F` or `0xFF`, and the app tries all three automatically and remembers the one your
device answered on.

Wired-only mice have no battery and are not listed.

## Troubleshooting

Run the probe from a console so it attaches to your terminal:

```bat
RazerTrayStatus.exe --probe
```

It prints every Razer HID interface it can open (product id, usage page/usage, feature report
length, serial) plus the raw battery and charging answer for each transaction id, and also writes
`%APPDATA%\RazerTrayStatus\probe.log`. If a mouse is missing from the list, or every transaction id
reports "no answer", that output is what identifies the cause.

Common cases:

- **Mouse is not listed** — the mouse or dongle is unplugged, or the mouse is asleep; wake it and
  refresh.
- **"not responding (idle or off)"** — the interface exists but the mouse did not answer the query.
- **Synapse does not open** — the app looks for
  `C:\Program Files\Razer\RazerAppEngine\RazerAppEngine.exe` (Synapse 4, also under its
  `app-<version>` folder) and then for Synapse 3's Razer Central. The error dialog lists the paths
  it tried.

## How it works

`src/RazerTrayStatus/Hid` contains the whole hardware side: SetupAPI enumerates HID interfaces,
each candidate is opened with `dwDesiredAccess = 0` so the mouse's exclusive `mouclass` lock does
not block us, and the app sends the Razer feature report (command class `0x07`, id `0x80` for the
level, `0x84` for charging). The level arrives as a `0..255` value which is scaled to a
percentage. Polling runs on a background thread; the UI only touches results.

## Notes

- The app opens Synapse with `--url-params=apps=synapse`, the argument Razer's own launcher uses to
  show the Synapse window.
- Only one instance runs at a time.
- No telemetry, no network access.
