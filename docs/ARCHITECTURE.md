# Architecture

## Runtime flow

1. `Core` installs every Harmony patch in the assembly.
2. When the `Main` scene loads, `MinimapManager` creates one persistent manager and waits until both
   `Player.Local` and the phone `MapApp` are usable.
3. `MinimapView` builds the overlay even when its initial enabled preference is false. It renders an
   independent copy of `MapApp.BackgroundImage`, an embedded calibrated sewer-map crop, and
   dedicated marker layers.
4. Static place names and POIs are discovered once, then updated through `POI` lifecycle/text
   patches. The view walks the game's `NPCManager.NPCRegistry` and `PoliceOfficer.Officers` lists
   one entry per frame, then waits two seconds before beginning the next pass. Every frame it
   converts cached live-actor world positions with `MapPositionUtility`.
5. `NPCButtonPatches` hides the HUD while `MapApp.SetOpen(true)` uses the untouched phone map and
   resumes the HUD after `SetOpen(false)`. `MinimapView` also reconciles its latch with the native
   `MapApp.opened` field every frame and on focus return, recovering if a focus transition bypasses
   the Harmony close callback.
6. NPC and police markers are HUD-owned only. The mod does not create, reparent, enable, or recolor
   native phone-map POIs, so opening the phone map remains independent.
7. `Player.PlayerList` is read every frame to place directional markers for the local player and
   every connected lobby member; player display names are refreshed once per second.
8. Every 0.25 seconds, the minimap reads the presence value already cached by the game-owned
   `SewerCameraPresense` controller. Its result selects the calibrated sewer overlay regardless of
   which manhole, entrance, or sewer door was used; leaving all sewer volumes restores the surface
   layer without repeating the game's sewer-volume query.
9. On scene exit or mod unload, the manager stops every coroutine, unsubscribes every preference
   event, destroys generated textures/UI, and clears the static
   patch reference.

## Ownership

| Owner | Objects/resources | Cleanup responsibility |
| --- | --- | --- |
| `Core` | Harmony instance | Unpatch on mod deinitialization. |
| `MinimapManager` | Preferences, initialization coroutine, `MinimapView` | Stop initialization, unsubscribe events, destroy view. |
| `MinimapView` | Canvas, generated mask sprites, embedded sewer texture, and live marker dictionaries | Destroy canvas/sprites and clear references. |
| `TimeDisplayView` | Clock GameObject and two generated panel sprites | Destroy UI and backing sprite textures. |
| `MapApp` (game-owned) | Content tree, background, and live POI container | Never reparented, transformed, disabled, or destroyed by the mod. |

## Update schedule

| Coroutine | Interval | Purpose |
| --- | ---: | --- |
| Manager readiness | one frame | Wait for the player and phone map without calling `MapApp.Awake()` manually. |
| Actor registry sync | one entry per frame, then 2-second idle | Walk the game's NPC/officer registries incrementally; add new markers and prune stale ones without a single-frame spike or Unity scene scan. |
| POI lifecycle patches | event-driven | Add, remove, or refresh location markers when their native POI changes. |
| Marker positioning | every `LateUpdate` | Convert current live-actor positions and update multiplayer directions; static POIs remain event-driven. |
| Sewer layer state | 0.25 seconds | Read the game controller's cached sewer-presence value without duplicating its volume query. |

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
- loads the supplied sewer graphic as an embedded resource and positions its crop using normalized
  coordinates calibrated against the native 4096x4096 `Map_Full` street/building geometry;
- places the sewer crop above surface-only labels/icons but below live player markers whenever the
  native sewer detector contains the local player;
- copies every meaningful `Text` below `MapApp.LabelGroup` into a dedicated HUD label layer using
  its native text, centered map-relative position, size, wrapping, font, alignment, and color;
- compensates the native labels' `0.3` local scale against the HUD zoom so they render like the
  readable 1.5x phone-map label state rather than becoming microscopic;
- composes label position, rotation, and scale from local transforms below `LabelGroup`; it never
  reads `lossyScale` or world matrices that can collapse while the phone app is closed;
- resolves the native `LabelGroup` rectangle with `GetComponent<RectTransform>()`; IL2CPP component
  wrappers must not be converted with C# `as`, which returns null even for a native RectTransform;
- discovers every active non-NPC `POI` once, then tracks lifecycle/text changes through Harmony;
- identifies dead-drop POIs through their parent `DeadDrop` component and applies the independent
  `G_ShowDeadDropPOIs` preference during each location scan;
- applies `I_ShowMapText` live to player-name, native-building, mirrored-map, and location-label text
  without disabling the associated player arrows, portraits, POI icons, or potential-customer areas;
- applies `J_ShowLocationIcons` to house, business, and other generic location icons without changing
  actor markers or the independently controlled dead-drop layer;
- provides compact `DROPS`, `ICONS`, and `TEXT` HUD buttons, lower-corner zoom `-`/`+` controls, and
  centered `ROTATE ON/OFF`; their managed callbacks update matching preferences and save the toggle
  category immediately;
- polls one configurable legacy Unity `KeyCode` while the HUD is visible and toggles only the six
  clickable controls, preserving all map, marker, clock, zoom, and layer state;
- filters the uninitialized `PoI Main Text` prefab placeholder from all label sources;
- reads active NPCs and police from the game's registries and renders their native `MugshotSprite`;
- keeps both the NPC portrait and its white backing disabled while `MugshotSprite` is null, avoiding
  stationary empty circles without suppressing an independent potential-customer area;
- uses `Customer.potentialCustomerPoI.enabled` to show the native potential-customer state as a
  translucent purple area behind the portrait;
- separates police into a dedicated layer with investigation/pursuit coloring;
- follows the local player or current vehicle;
- optionally rotates the common map content around the tracked player, uses the rotated offset to
  keep that player centered, and counter-rotates labels and portrait markers so they remain upright;
- maintains directional markers for every entry in the multiplayer `Player.PlayerList`;
- filters every `Player.PoI` from the generic location layer to prevent duplicate player icons;
- acquires the active `HintDisplay` singleton independently of notification events, captures its
  native anchors, pivot, position, and `Offset`, then forces the whole panel onto the screen edge
  opposite the minimap; writing both the native offset and container layout prevents the game's
  animation/layout pass from returning it over the minimap, while the original values are restored
  when the minimap hides;
- maintains dictionaries keyed by Unity instance ID and prunes despawned objects;
- writes UI transforms, rotations, image properties, and hint-panel layout only when their values
  actually change, avoiding needless Canvas rebuild work;
- centers, scales, and optionally rotates only the HUD content, leaving the native phone map's
  pan/zoom/orientation state untouched.

### `NugzzMinimap/Main/TimeDisplayView.cs`

Builds the clock panel. Text is changed only when the game minute/day changes, avoiding a UI layout
rebuild every rendered frame. Sprite pixels remain opaque and image tint carries user opacity, so
live opacity changes are not multiplied by the startup alpha.

### `NugzzMinimap/Helpers/MapPreferences.cs`

Defines configuration schema and compatibility keys. Categories and entries are repaired
individually, then all three categories are loaded from `Mods/NugzzMinimap Mod Settings.cfg` beside
the DLL. A legacy `UserData/NugzzMinimap.cfg` is copied once without deleting the rollback source;
its old default F8 control binding is migrated to Home. The file is prepared during MelonLoader
startup, then the shared preference entries are loaded again when the main-scene HUD is built.
Numeric values are clamped at their use sites.

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
  the explicit compact HUD controls.

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
- Spread NPC/police registry discovery over frames instead of processing the complete registry in
  one recurring gameplay frame.
- Reused the game controller's cached sewer-presence result instead of repeating its volume query.
- Removed unchanged background, marker, and hint-layout writes from the hot update path.
- Reduced clock text writes from every rendered frame to once per in-game minute/day change.
- Removed unused/decompiler-generated source and the inactive POI patch that would have duplicated
  `POI.UpdatePosition()` work if enabled.
