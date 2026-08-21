# NugzzMinimap

NugzzMinimap is a live HUD minimap for the IL2CPP build of **Schedule I**. It uses the game's native
map, building labels, location data, and character portraits while leaving the phone map available
for normal use.

Current version: **1.6.8**

Authors: **XUnfairX / GrandmasAnkles**

## Features

- Live circular or rounded-square minimap with configurable size, zoom, and position.
- Native building and place labels matching the zoomed-in phone map.
- Live NPC portraits with empty portrait markers automatically suppressed.
- Purple potential-customer areas based on the game's current customer state.
- Police tracking with investigation and pursuit colors.
- Directional markers for the local player and every connected multiplayer player.
- House, business, and other native location icons.
- Optional dead-drop POIs with a clickable `DEAD DROPS: ON/OFF` HUD button.
- Configurable weekday and time display.
- Automatic vehicle tracking when the local player enters a vehicle.
- Non-interactive map graphics that do not block gameplay input.
- Phone-map compatibility: the HUD hides while the map app is open and resumes afterward.

## Requirements

- **Schedule I** using its IL2CPP build.
- [MelonLoader](https://github.com/LavaGang/MelonLoader) installed for Schedule I.
- The game must have been launched through MelonLoader at least once.

## Installation

1. Download `NugzzMinimap.dll` from the [latest GitHub release](../../releases/latest).
2. Close Schedule I.
3. Open the game's installation directory. The default Steam location is:

   ```text
   C:\Program Files (x86)\Steam\steamapps\common\Schedule I
   ```

4. Copy `NugzzMinimap.dll` into the `Mods` directory.
5. Remove any older `BetterMinimap` or `NugzzMinimap` DLLs so only one copy is installed.
6. Start the game through MelonLoader.

MelonLoader should report `NugzzMinimap v1.6.8` during startup.

## Configuration

Settings are stored in:

```text
Schedule I/UserData/NugzzMinimap.cfg
```

The file is created or repaired automatically when the mod starts. Most settings update while the
game is running.

| Setting | Default | Description |
| --- | ---: | --- |
| `A_MinimapEnabled` | `true` | Enables the HUD minimap. |
| `B_ShowGameTime` | `true` | Shows the weekday and time panel. |
| `C_IncreaseMapSize` | `false` | Switches from the normal 1.5x size to the larger 2.2x size. |
| `D_ShowSquareMinimap` | `false` | Uses a rounded-square map instead of a circle. |
| `E_TrackNPCs` | `true` | Shows live NPC portraits and potential-customer areas. |
| `F_TrackPolice` | `true` | Shows police locations and pursuit state. |
| `G_ShowDeadDropPOIs` | `true` | Shows dead-drop names and icons. Also controlled by the HUD button. |
| `H_MinimapZoom` | `7` | Sets map zoom from `1` through `10`. |
| `A_TimeDisplayTextColor` | `White` | Sets the clock text color. |
| `B_TimeDisplayBackgroundOpacity` | `65` | Sets clock background opacity from `0` through `100`. |
| `C_TimeDisplayTextSize` | `18` | Sets clock text size from `10` through `30`. |
| Position settings | varies | Sets minimap and clock X/Y offsets. |

The dead-drop button appears below the clock. Green means dead drops are enabled; red means they are
hidden. Clicking it updates and saves `G_ShowDeadDropPOIs`.

## Building from source

The project targets .NET 6 and references assemblies from the local Schedule I and MelonLoader
installation. Game assemblies are not included in this repository.

With the game installed in its default Steam location:

```powershell
dotnet build .\NugzzMinimap.sln --configuration Release
```

For a custom game location:

```powershell
dotnet build .\NugzzMinimap.sln --configuration Release `
  -p:ScheduleOnePath="D:\Games\Schedule I"
```

The compiled mod is written to:

```text
NugzzMinimap/bin/Release/net6.0/NugzzMinimap.dll
```

## Troubleshooting

### The mod does not appear

- Confirm `NugzzMinimap.dll` is directly inside `Schedule I/Mods`.
- Remove duplicate or older minimap DLLs.
- Launch the game through MelonLoader.
- Check the newest file in `Schedule I/MelonLoader/Logs` for `NugzzMinimap v1.6.8`.

### The project does not build

- Run Schedule I through MelonLoader once so `MelonLoader/Il2CppAssemblies` is generated.
- Install the .NET 6 SDK or newer.
- Supply `ScheduleOnePath` explicitly if the game is not in a default Steam directory.

### The configuration is outdated

The mod adds missing settings automatically. To restore every default, close the game, back up
`UserData/NugzzMinimap.cfg`, delete it, and launch the game again.

## Project structure

- `NugzzMinimap/Core.cs` — MelonLoader entry point and scene lifecycle.
- `NugzzMinimap/Main/MinimapManager.cs` — preferences and runtime coordination.
- `NugzzMinimap/Main/MinimapView.cs` — map, markers, labels, clock placement, and HUD controls.
- `NugzzMinimap/NPCSystem/NPCButtonPatches.cs` — phone-map Harmony integration.
- `NugzzMinimap/Helpers` — preferences, UI state, colors, and generated sprite helpers.
- `docs/ARCHITECTURE.md` — implementation details, ownership, and lifecycle invariants.

## Verification

Version 1.6.8 builds in Debug and Release with zero compiler warnings or errors against the current
Schedule I and MelonLoader IL2CPP assemblies. Runtime behavior must be verified in the game because
Schedule I does not provide a headless test environment.

## Disclaimer

NugzzMinimap is an unofficial community mod. It is not affiliated with or endorsed by TVGS,
Schedule I, MelonLoader, or their respective developers.
