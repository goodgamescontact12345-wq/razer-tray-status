# razer-tray-status

Battery level of your Razer wireless mouse in the Windows system tray.

It shows every Razer mouse you have connected, lets you pin one of them to the tray, and opens Razer
Synapse when you click the icon.

## Download

Get `RazerTrayStatus.exe` from the
[latest release](https://github.com/goodgamescontact12345-wq/razer-tray-status/releases/latest) and
run it. There is nothing to install.

The file is not signed, so Windows SmartScreen may warn you the first time. Click *More info*, then
*Run anyway*.

## What it does

- Shows the battery percentage of every connected Razer mouse.
- Pin one mouse to the tray icon. It stays pinned across restarts and replugging.
- The icon is a small battery with the percentage inside it, and its colour tells you the level.
- Left click opens Razer Synapse. Right click opens the menu: your mice, *Open Razer Synapse*,
  *Refresh now*, *Settings*, *Device diagnostics* and *Exit*.
- *Settings* sets how often it checks (15 seconds to 5 minutes), when it warns you about a low
  battery, and whether it starts with Windows.
- No drivers and no admin rights.

## Colours

| Colour | Meaning |
| --- | --- |
| Green | Plenty left |
| Orange | Getting low, 15% or less by default |
| Red | 5% or less, charge it |
| Blue | Charging |
| Grey `?` | The mouse did not answer |

The menu uses the same words: `72% · good`, `30% · getting low`.

## What you need

Windows 10 or 11, 64-bit. A Razer wireless mouse, or its dongle, plugged in. Razer Synapse 4 or 3 if
you want the click to open it.

## If something is wrong

- **The mouse is not in the menu.** Check the cable or dongle, then click *Refresh now*.
- **It says `no battery answer`.** Windows sees the mouse but it is not replying, usually because it
  is asleep. It comes back as soon as it answers.
- **Synapse does not open.** Razer Synapse 4 or 3 has to be installed.
- **Anything else.** *Device diagnostics* writes a log and opens it, which is the thing to attach if
  you report a problem.
