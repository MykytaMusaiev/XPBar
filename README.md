# XPBar

Community plugin for [GameHelper2](https://github.com/Gordin/GameHelper2) for Path of Exile 2.

XPBar started as an adaptation of the original [Curvu/XPBar](https://github.com/Curvu/XPBar) concept for GameHelper2. After the initial GameHelper2 port, the plugin was fully redesigned into a compact read-only XP overlay and progression tracker with session XP tracking, area/map XP tracking, death-aware map gain/loss display, configurable colors, and in-memory area history.

Read-only overlay — **build from source**.

## What changed from the original XPBar

This version is not only a direct framework port. The development went through two stages:

1. **GameHelper2 adaptation**

   * migrated from the original XPBar / ExileCore-style plugin structure to the GameHelper2 plugin model
   * updated project structure, references, build target, runtime copy behavior, and settings handling
   * switched to GameHelper2 player and area read models
   * preserved a minimal compact XP display as the first working baseline

2. **Full plugin redesign**

   * redesigned the overlay UX
   * added configurable layout, scaling, labels, colors, and background
   * added XP session tracking
   * added area/map XP tracking
   * added death-aware negative XP delta display
   * added current-session area history
   * separated tracking logic from overlay rendering
   * kept runtime state in-memory and read-only

## Features

* Compact XP overlay:

  * `94: 36.158%`
  * optional custom label/prefix
  * configurable decimal precision
  * configurable text scale and auto-scale
  * configurable position and centered-width rendering
  * optional background with configurable color and padding

* Session XP tracking:

  * opt-in, disabled by default
  * tracks XP gained since session start/reset
  * optional session gain suffix in overlay
  * reset button
  * in-memory only; session baseline is not saved into config

* Area / Map XP tracking:

  * opt-in, disabled by default
  * tracks XP gained or lost in the current map/area instance
  * uses GameHelper2 current area instance identity
  * preserves baseline when leaving and returning to the same map instance
  * ignores town, hideout, and loading states
  * positive map gain can appear to the right of the main XP text:

    * `94: 36.158%   +0.421%`
  * negative map delta after death can appear to the left:

    * `-0.137%   94: 35.921%`
  * suppresses empty zero deltas such as `+0.000%`
  * configurable positive and negative area delta colors

* Area history:

  * in-memory current-session history
  * configurable history limit
  * settings-only display
  * not persisted across plugin restart

* Defensive XP calculations:

  * percent gain is shown only when the required XP table segment is valid
  * falls back to raw XP gain/loss when percent would be misleading
  * supports signed raw XP loss for death/map-loss tracking

* Safety-focused behavior:

  * no game memory writes
  * no input automation
  * no clicking, key sending, or gameplay automation
  * no packet or network interaction
  * local overlay rendering and plugin-local runtime state only

## Requirements

* A working [GameHelper2](https://github.com/Gordin/GameHelper2) source tree
* **.NET 10 SDK** (`net10.0-windows`, x64)
* Windows x64
* Path of Exile 2 supported by your GameHelper2 build

## Build & install

```bash
git clone https://github.com/Gordin/GameHelper2.git
cd GameHelper2
git clone https://github.com/MykytaMusaiev/XPBar.git Plugins/XPBar
dotnet build GameHelper/GameHelper.csproj -c Release
dotnet build Plugins/XPBar/XPBar.csproj -c Release
```

Enable **XPBar** in GameHelper → Plugins.

The build copies the plugin output into the GameHelper2 runtime plugins folder:

```txt
GameHelper/bin/Release/net10.0-windows/win-x64/Plugins/XPBar
```

## Updating

Close GameHelper before rebuilding the plugin. Otherwise, Windows may keep `XPBar.dll` or related runtime files locked.

```bash
taskkill //IM GameHelper.exe //F
taskkill //IM Launcher.exe //F

cd /c/G/soft_links/tools/GameHelper2
dotnet build Plugins/XPBar/XPBar.csproj -c Release
```

Then restart GameHelper.

## Settings

XPBar settings are available inside GameHelper → Plugins → XPBar.

### Main overlay

* **Show XP overlay**
  Shows or hides the main XP overlay text.

* **Show background**
  Draws a background rectangle behind the overlay text.

* **Hide when game not foreground**
  Hides the overlay when Path of Exile 2 is not the foreground window.

* **Show when GameHelper foreground**
  Keeps the overlay visible while configuring GameHelper itself.

* **Position X / Position Y**
  Adjusts the overlay position.

* **Center by width**
  Centers the whole rendered text block by its measured width. Useful when labels, session gain, or area gain change the text length.

* **Decimal places**
  Controls XP percentage precision.

* **Custom label**
  Optional prefix for the main XP display.

* **Text scale / Auto scale**
  Controls text size.

* **Text color**
  Main XP text color.

* **Background color / Background padding**
  Background appearance controls.

### XP Tracking

* **Enable XP tracking**
  Enables session XP tracking.

* **Show session gain in overlay**
  Adds session gain to the overlay when area overlay is not taking priority.

* **Reset session**
  Resets the session baseline to the current valid XP value.

Session tracking is runtime-only. Restarting the plugin starts a fresh session.

### Area / Map XP Tracking

* **Enable area/map XP tracking**
  Enables current map/area XP tracking.

* **Show area gain in overlay**
  Shows current area gain/loss in the overlay.

* **Show area history**
  Shows recent area entries in settings.

* **Area history limit**
  Controls how many area entries are kept in memory.

* **Positive area gain color**
  Color used for positive map gain.

* **Negative area loss color**
  Color used for negative map delta after death/loss.

* **Reset current area**
  Resets the current area baseline when valid player and area data are available.

Area tracking uses the current area instance identity from GameHelper2. Town, hideout, and loading states are ignored so they do not reset the current map baseline.

## Config folder

| Path                                | Purpose                |
| ----------------------------------- | ---------------------- |
| `Plugins/XPBar/config/settings.txt` | Plugin settings (JSON) |

Runtime-only session and area baselines are not stored in the config file.

## Display examples

Main XP only:

```txt
94: 36.158%
```

Main XP with positive area gain:

```txt
94: 36.158%   +0.421%
```

Main XP with negative area delta after death:

```txt
-0.137%   94: 35.921%
```

Optional custom label:

```txt
XP 94: 36.158%
```

Raw XP fallback example:

```txt
94: 36.158%   +124,532 XP
```

## Known limitations

* XP percentage gain depends on the available XP table data.
* If a level segment is malformed or missing, XPBar avoids showing a misleading percentage and falls back to raw XP where possible.
* Area history is in-memory only and resets when the plugin restarts.
* Very old area entries can be pruned when the configured history limit is reached.
* Re-entering a pruned old area instance may start a fresh baseline.
* XPBar does not provide XP/hour, ETA, or persistent long-term history yet.

## Roadmap

Possible future improvements:

* XP/hour
* Percent/hour
* ETA to next level
* Rate smoothing
* Last N maps average
* Optional persistent history
* README screenshots

## Credits

* Original XPBar concept: [Curvu/XPBar](https://github.com/Curvu/XPBar)
* GameHelper2: [Gordin/GameHelper2](https://github.com/Gordin/GameHelper2)
* GameHelper2 adaptation, redesign, and current XP/session/area tracking version: **MykytaMusaiev**

## Disclaimer

Third-party plugin — use at your own risk.

XPBar is designed as a read-only overlay. It does not automate gameplay, send inputs, write to game memory, patch the game client, or interact with network packets.

Comply with Grinding Gear Games' Terms of Use and any rules that apply to third-party tools.

## Version history

| Version      | Notes                                                                                                                                                                                                                       |
| ------------ | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| **1.0.0**    | Initial GameHelper2 adaptation and redesigned community release with compact XP overlay, session XP tracking, area/map XP tracking, in-memory area history, positive/negative area delta colors, and zero-delta suppression |
| **Original** | ExileCore2 XPBar concept by Curvu                                                                                                                                                                                           |
