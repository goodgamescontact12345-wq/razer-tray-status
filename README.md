# razer-tray-status

Shows the battery level of your Razer wireless mice in the Windows system tray. It handles more than
one mouse and lets you pin one of them to the tray.

Left-click the icon to open Razer Synapse. Right-click it for the menu.

The level is read straight from the mouse over USB HID, using the protocol
[OpenRazer](https://github.com/openrazer/openrazer) worked out. Synapse does not have to be running
to read it. It only opens when you click the icon.

## Download

Grab `RazerTrayStatus.exe` from the
[latest release](https://github.com/goodgamescontact12345-wq/razer-tray-status/releases/latest) and
run it. There is nothing to install and no .NET needed. The exe is not signed, so SmartScreen may
warn you the first time: click *More info*, then *Run anyway*.

## What it does

- Every connected Razer mouse shows up in the menu with its battery percentage.
- Pin one mouse to the tray icon. The pin survives restarts and replugging.
- Left click: open Razer Synapse (4, falling back to 3).
- Right click: your mice, *Open Razer Synapse*, *Refresh now*, *Settings* (how often to check, low
  battery level, start with Windows), *Device diagnostics...* and *Exit*.
- The icon is a small battery with the percentage inside it, and its colour tells you the level.
- No drivers, no admin rights, nothing to install.
- Settings live in `%APPDATA%\RazerTrayStatus\settings.json`.

## Colours

| Colour | Meaning |
| --- | --- |
| Green | Plenty left |
| Orange | Getting low: at or below your *Low battery at* setting, 15% by default |
| Red | Charge now: 5% or less |
| Blue | Plugged in and charging |
| Grey with `?` | No answer from the mouse |

The menu says the same thing in words next to each mouse, like `72% · good` or `30% · getting low`,
so you never have to guess at a colour.

## Requirements

- Windows 10 or 11, 64-bit.
- A Razer wireless mouse, or its 2.4 GHz dongle, plugged in.
- Razer Synapse 4 or 3 if you want the left click to open it.

## Build

You need the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).

```bat
build.cmd
```

That gives you a single `dist\RazerTrayStatus.exe` that runs on its own, with no .NET installed on
the machine that runs it. To run it from source instead:

```bat
dotnet run --project src\RazerTrayStatus
```

## Using it

1. Start `RazerTrayStatus.exe`. If one mouse is connected, it is shown straight away.
2. Right-click the icon and click a mouse to pin it. A checkmark marks the pinned one.
3. Hover the icon for the exact percentage. Left-click it to open Synapse.
4. *Refresh now* reads the battery straight away. *Settings > Refresh every* changes how often it
   checks: 15 seconds, 30 seconds, 1 minute or 5 minutes, 1 minute by default.

If a mouse is found but does not answer, for example while it is wired and charging or asleep on its
dongle, it still shows up in the menu as `no battery answer` and you can still pin it. It just has no
percentage until it answers again.

## Which mice work

Any Razer wireless mouse that exposes the battery report over HID, including while it is plugged in
and charging. Newer models answer on one transaction id, older receivers on another. The app tries
all three and remembers which one your mouse used.

One mouse shows up in Windows as several HID collections (mouse, keyboard, vendor). Which one
carries the battery command depends on the model, so the app tries each of them and remembers the
one that answered. That is why a wired Viper V3 is found even though its battery channel sits
somewhere else.

Mice that only work wired have no battery and report nothing.

## If something is wrong

Right-click the icon and choose *Device diagnostics...*. It opens
`%APPDATA%\RazerTrayStatus\probe.log`, which lists every HID collection Windows sees for Razer
hardware, its usage page, its feature report length, and what every attempt returned. From a console
you get the same output with:

```bat
RazerTrayStatus.exe --probe
```

Things that come up:

- **"already running"** - an older copy is still in the tray. Right-click it, choose *Exit*, then
  start the new one.
- **The mouse is not listed** - Windows is not seeing it as a HID device. Check the cable or dongle
  and try *Refresh now*.
- **"no battery answer"** - Windows sees the mouse but it did not reply. The log says why for each
  attempt, for example `SetFeature failed (win32 87 (invalid parameter - feature report rejected))`,
  `no input report (timeout)` or `no valid response (status 0x03)`.
- **Nothing is logged** - check `%APPDATA%\RazerTrayStatus\agent.log`. The app shows errors in a
  dialog instead of vanishing.
- **Synapse does not open** - the app looks for
  `C:\Program Files\Razer\RazerAppEngine\RazerAppEngine.exe` (Synapse 4, also inside an
  `app-<version>` folder), then for Synapse 3's Razer Central. The error dialog lists the paths it
  tried.

## How it works

`src/RazerTrayStatus/Hid` has the whole hardware side. SetupAPI lists the HID interfaces, each
candidate is opened with `dwDesiredAccess = 0` so the mouse's exclusive `mouclass` lock does not get
in the way, and the app sends Razer's feature report (command class `0x07`, id `0x80` for the level,
`0x84` for charging). The level comes back as a `0..255` value which is turned into a percentage.
Reading runs on a background thread and the UI only touches the results.

## Notes

- Synapse is opened with `--url-params=apps=synapse`, the same argument Razer's own launcher uses.
- Only one copy runs at a time.
- No telemetry, no network access.
