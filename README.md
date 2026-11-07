# razer-tray-status

Battery level of your Razer wireless mouse in the Windows system tray.

## Download

Get `RazerTrayStatus.exe` from the
[latest release](https://github.com/goodgamescontact12345-wq/razer-tray-status/releases/latest) and
run it. There is nothing to install.

The file is not signed, so Windows SmartScreen may warn you the first time. Click *More info*, then
*Run anyway*.

## Colours

| Colour | Meaning |
| --- | --- |
| Green | Plenty left |
| Orange | Getting low, 15% or less by default |
| Red | 5% or less, charge it |
| Blue | Charging |
| Grey `?` | The mouse did not answer |

## What you need

Windows 10 or 11, 64-bit. A Razer wireless mouse, or its dongle, plugged in. Razer Synapse 4 or 3 if
you want the click to open it.

## If something is wrong

- **The mouse is not in the menu.** Check the cable or dongle, then click *Refresh now*.
- **It says `no battery answer`.** Windows sees the mouse but it is not replying, usually because it
  is asleep. It comes back as soon as it answers.
- **Synapse does not open.** Razer Synapse 4 or 3 has to be installed.
- **Anything else.** *Device diagnostics* writes a log and opens it, then open an issue so i can fix it
