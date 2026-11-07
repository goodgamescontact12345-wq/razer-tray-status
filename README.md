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
- Right click → your mice, *Open Razer Synapse*, *Refresh now*, *Settings* (poll interval, low-battery
  level, start with Windows), *Device diagnostics…* and *Exit*.
- The tray icon is a badge with the battery percentage drawn as big, crisp digits. Its colour
  explains the level at a glance (see below), and the menu spells the same thing out next to each
  mouse: `95% · good`, `30% · getting low`, `12% · charge now`, `95% · charging`.
- No drivers, no admin rights, no Synapse dependency for reading, no bundled image assets.
- Settings live in `%APPDATA%\RazerTrayStatus\settings.json`.

## What the colours mean

| Colour | Meaning |
| --- | --- |
| Green | Plenty left |
| Orange | Getting low — at or below your *Low battery at* setting (default 15%) |
| Red | Charge now — 5% or less |
| Blue | On the cable, charging |
| Grey with `?` | No battery answer (mouse asleep or unreachable) |

Green, orange, red and blue are also the words shown in the menu and the hover tooltip, so the
colours never have to be guessed.

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
2. Right-click the tray icon and click a mouse to pin it to the tray (a checkmark marks the pinned one).
3. Hover the icon for the exact percentage; left-click it to open Razer Synapse.
4. *Refresh now* forces a read; *Settings → Refresh every* changes the polling interval (15 s, 30 s,
   1 minute or 5 minutes, default 1 minute).

When a mouse is detected but does not answer the battery query (for example while it is wired and
charging, or asleep on its dongle) it is still listed, marked `no battery answer`, and is still
pinnable — it simply shows no percentage until it answers again.

## Supported mice

Any Razer wireless mouse that exposes the 90-byte HID control report works, **including while it is
plugged in and charging over the cable**. Typical 2021+ models answer on transaction `0x1F`, older
receivers on `0x3F` or `0xFF`; the app tries all three and remembers which one your device answered
on.

A mouse exposes several HID collections (mouse, keyboard, vendor) and which one carries the battery
command differs by model and by wired/wireless mode — so the app probes every collection of the
device and remembers the one that answered. This is why a wired Viper V3 is found even though its
battery channel is not on the same collection as on other models.

Wired-only mice have no battery and report nothing.

## Troubleshooting

Right-click the tray icon and choose **Device diagnostics…** — it opens
`%APPDATA%\RazerTrayStatus\probe.log` describing every HID collection Windows exposes for Razer
hardware, its usage page and feature report length, and the result of **every I/O strategy**:
feature report vs. output/input report, both handle access modes, and all three transaction ids.
From a console you can get the same output with:

```bat
RazerTrayStatus.exe --probe
```

Common cases:

- **"already running" dialog** — an older build is still in the tray (the app is single-instance).
  Right-click its icon, choose *Exit*, then start the new build.
- **Mouse is not listed at all** — Windows does not enumerate it as a HID device; check the cable or
  dongle while the mouse is connected and try *Refresh now*. Detection reads the device interface
  paths, so it does not depend on the collection being openable.
- **"no battery answer"** — Windows sees the mouse but it did not answer any strategy. The log shows
  the exact reason per attempt, for example `SetFeature failed (win32 87 (invalid parameter - feature
  report rejected))`, `no input report (timeout)` or `no valid response (status 0x03)`.
- **Nothing is logged** — check `%APPDATA%\RazerTrayStatus\agent.log` for crashes; the app reports
  unhandled errors in a dialog instead of disappearing.
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
