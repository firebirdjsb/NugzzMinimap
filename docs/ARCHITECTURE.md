# Architecture

## Runtime flow

1. `Core` installs every Harmony patch in the assembly.
2. When the `Main` scene loads, `MinimapManager` creates one persistent manager and waits until both
   `Player.Local` and the phone `MapApp` are usable.
3. `MinimapView` builds the overlay even when its initial enabled preference is false. It renders an
   independent copy of `MapApp.BackgroundImage` and creates dedicated marker layers.
4. Once per second, the view copies meaningful static place names from `MapApp.LabelGroup` and
   discovers live `POI`, `NPC`, and `PoliceOfficer` objects. Every frame it converts world positions
   with `MapPositionUtility`; dynamic location icons and NPC mugshots are read directly from game
   objects rather than from phone-controlled UI visibility.
5. `NPCButtonPatches` hides the HUD while `MapApp.SetOpen(true)` uses the untouched phone map and
   resumes the HUD after `SetOpen(false)`.
6. NPC and police markers are HUD-owned only. The mod does not create, reparent, enable, or recolor
   native phone-map POIs, so opening the phone map remains independent.
7. `Player.PlayerList` is read every frame to place named, directional markers for the local player
   and every connected lobby member.
8. On scene exit or mod unload, the manager stops every coroutine, unsubscribes every preference
   event, destroys generated textures/UI, and clears the static
   patch reference.

## Ownership

| Owner | Objects/resources | Cleanup responsibility |
| --- | --- | --- |
| `Core` | Harmony instance | Unpatch on mod deinitialization. |
| `MinimapManager` | Preferences, initialization coroutine, `MinimapView` | Stop initialization, unsubscribe events, destroy view. |
| `MinimapView` | Canvas, generated mask sprites, and live marker dictionaries | Destroy canvas/sprites and clear references. |
| `TimeDisplayView` | Clock GameObject and two generated panel sprites | Destroy UI and backing sprite textures. |
| `MapApp` (game-owned) | Content tree, background, and live POI container | Never reparented, transformed, disabled, or destroyed by the mod. |

## Update schedule

| Coroutine | Interval | Purpose |
| --- | ---: | --- |
| Manager readiness | one frame | Wait for the player and phone map without calling `MapApp.Awake()` manually. |
| Live marker discovery | 1 second | Rescan native place labels, locations, NPCs, and officers; add new markers and prune stale ones. |
| Marker positioning | every `LateUpdate` | Convert current world positions and update multiplayer directions. |

The manager readiness coroutine is retained and stopped during cleanup. Continuous location/NPC
tracking is handled by `LateUpdate`, so no infinite marker coroutine survives a scene change.

## Source map

### `NugzzMinimap/Core.cs`

MelonLoader entry point and scene lifecycle. Harmony scans the complete assembly because the actual
patch declarations are nested inside `NPCButtonPatches`; scanning only the outer type does not
install them.

### `NugzzMinimap/Main/MinimapManager.cs`

The singleton coordinator. It owns preference subscriptions and translates each setting change into
one view update. `DestroyInstance` deliberately avoids the creating `Instance` getter.

### `NugzzMinimap/Main/MinimapView.cs`

The main integration layer:

- creates the screen-space canvas and minimap mask;
- renders the native map sprite independently;
- copies every meaningful `Text` below `MapApp.LabelGroup` into a dedicated HUD label layer using
  its native text, centered map-relative position, size, wrapping, font, alignment, and color;
- compensates the native labels' `0.3` local scale against the HUD zoom so they render like the
  readable 1.5x phone-map label state rather than becoming microscopic;
- composes label position, rotation, and scale from local transforms below `LabelGroup`; it never
  reads `lossyScale` or world matrices that can collapse while the phone app is closed;
- resolves the native `LabelGroup` rectangle with `GetComponent<RectTransform>()`; IL2CPP component
  wrappers must not be converted with C# `as`, which returns null even for a native RectTransform;
- discovers every active non-NPC `POI` and renders its native icon plus `MainText`/`DefaultMainText`;
- identifies dead-drop POIs through their parent `DeadDrop` component and applies the independent
  `G_ShowDeadDropPOIs` preference during each location scan;
- provides a clickable dead-drop HUD button whose managed callback updates the same preference and
  saves the toggle category immediately;
- filters the uninitialized `PoI Main Text` prefab placeholder from all label sources;
- discovers active NPCs directly and renders their native `MugshotSprite` at live world positions;
- keeps both the NPC portrait and its white backing disabled while `MugshotSprite` is null, avoiding
  stationary empty circles without suppressing an independent potential-customer area;
- uses `Customer.potentialCustomerPoI.enabled` to show the native potential-customer state as a
  translucent purple area behind the portrait;
- separates police into a dedicated layer with investigation/pursuit coloring;
- follows the local player or current vehicle;
- maintains directional markers for every entry in the multiplayer `Player.PlayerList`;
- filters every `Player.PoI` from the generic location layer to prevent duplicate player icons;
- maintains dictionaries keyed by Unity instance ID and prunes despawned objects;
- centers and scales only the HUD content, leaving the native phone map's pan/zoom state untouched.

### `NugzzMinimap/Main/TimeDisplayView.cs`

Builds the clock panel. Text is changed only when the game minute/day changes, avoiding a UI layout
rebuild every rendered frame. Sprite pixels remain opaque and image tint carries user opacity, so
live opacity changes are not multiplied by the startup alpha.

### `NugzzMinimap/Helpers/MapPreferences.cs`

Defines configuration schema and compatibility keys. Categories and entries are repaired
individually, then all three categories are loaded from the shared configuration file. Numeric
values are clamped at their use sites.

### `NugzzMinimap/Helpers/Utils.cs`

Generates circle/rounded-rectangle textures in one pixel buffer and uploads each texture once. It
also owns the guarded `SafeSetActive` helper used around native Unity wrappers.

### `NugzzMinimap/Helpers/MinimapState.cs`

Stores the shape and scale used when the HUD mirror is resized or restyled.

### `NugzzMinimap/Helpers/TimeDisplayColor.cs`

Stable enum serialized by MelonPreferences. Renaming members would break existing configuration
values.

### `NugzzMinimap/NPCSystem/NPCButtonPatches.cs`

Harmony bridge for `MapApp.SetOpen(bool)` and `ContactsDetailPanel.Open(NPC)`. The latter reconnects
the game's Show On Map control to the customer POI owned by the native map system.

### `NugzzMinimap/Properties/AssemblyInfo.cs`

MelonLoader game/mod metadata and repaired assembly version.

## Important invariants

- Exactly one `MinimapManager` and one `MinimapCanvas` may exist.
- The game-owned `ContentRect` and `PoIContainer` are never reparented or mutated by the minimap.
- HUD marker UI has no gameplay/input scripts and cannot receive raycasts.
- No infinite coroutine may be started without retaining its stop token.
- Generated sprites own their textures; game sprites do not.
- The map, markers, labels, and clock have raycasts disabled. The canvas raycaster exists solely for
  the explicit dead-drop toggle button.

## Confirmed repairs from the reconstructed source

- Replaced six uncompilable decompiler state-machine references with real iterator methods.
- Hid managed coroutine helpers from native class injection, eliminating unsupported
  `System.Collections.IEnumerator` registration warnings.
- Replaced the invalid legacy project file and mixed-game absolute references with a .NET 6 project.
- Fixed Harmony registration so nested patches are actually installed.
- Fixed the initial-disabled preference path, duplicate manager creation, and leaked scene coroutines.
- Restored the original phone-map anchored position/sibling order instead of forcing position zero.
- Removed the doubled background movement that desynchronized the map image and POIs.
- Replaced POI-container reparenting with a live, read-only full-map mirror so the HUD and phone map
  can update independently.
- Replaced phone-UI visual mirroring with source-driven `POI`/`NPC`/police markers after confirming
  that `UIMapPanel` owns native POI visibility and hover state.
- Hid mod-local enum methods from native class injection, eliminating the false
  `Assembly NugzzMinimap.dll is not registered in il2cpp` lookup.
- Moved police coloring from every rendered frame to a 0.3-second state update.
- Reduced clock text writes from every rendered frame to once per in-game minute/day change.
- Removed unused/decompiler-generated source and the inactive POI patch that would have duplicated
  `POI.UpdatePosition()` work if enabled.
