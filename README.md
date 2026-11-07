# razer-tray-status

Battery level of your Razer wireless mice in the Windows system tray. Works with more than one mouse,
and you can pin one of them to the tray.

Left click the icon to open Razer Synapse, right click for the menu.

The level is read straight from the mouse over USB HID, using the protocol
[OpenRazer](https://github.com/openrazer/openrazer) worked out. Synapse does not have to be running.

## Download

Get `RazerTrayStatus.exe` from the
[latest release](https://github.com/goodgamescontact12345-wq/razer-tray-status/releases/latest) and
run it. Nothing to install, no .NET needed. The file is not signed, so SmartScreen may warn you once:
*More info*, then *Run anyway*.

## What it does

- Every connected Razer mouse, with its battery percentage.
- Pin one mouse to the icon. It stays pinned across restarts and replugging.
- Right click: your mice, *Open Razer Synapse*, *Refresh now*, *Settings* (check interval, low battery
  level, start with Windows), *Device diagnostics...*, *Exit*.
- No drivers, no admin rights. Settings live in `%APPDATA%\RazerTrayStatus\settings.json`.

## Colours

| Colour | Meaning |
| --- | --- |
| Green | Plenty left |
| Orange | Getting low, 15% or less by default |
| Red | 5% or less, charge it |
| Blue | Charging |
| Grey `?` | The mouse did not answer |

The menu says the same in words: `72% · good`, `30% · getting low`.

## Requirements

Windows 10 or 11, 64-bit, a Razer wireless mouse or its dongle, and Synapse 4 or 3 if you want the
left click to open it.

## Build

Needs the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).

```bat
build.cmd
```

That writes `dist\RazerTrayStatus.exe`, which runs on its own.

## If something is wrong

Right-click the icon and choose *Device diagnostics...*. The `probe.log` it opens lists every HID
collection Windows sees for Razer hardware, and what each attempt returned. From a console:
`RazerTrayStatus.exe --probe`.

- Mouse not listed: check the cable or dongle, then *Refresh now*.
- `no battery answer`: the mouse did not reply, and the log says why.
- Synapse does not open: the app looks for
  `C:\Program Files\Razer\RazerAppEngine\RazerAppEngine.exe`, then for Synapse 3.

## Notes

No telemetry, no network access, one copy at a time.
