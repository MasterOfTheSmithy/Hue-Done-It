# Hue Done It - Even More Production Refinement Pass

This pass is additive and should be applied after the previous production refinement patches.

## Added

- `BetaPlayerSafetyNet`
  - Server-side recovery for players who fall below the generated map or leave playable bounds.
  - Uses existing `NetworkPlayerAuthoritativeMover.ServerTeleportTo(...)` when available.

- `BetaObjectiveRouteCompass`
  - F4 toggled fallback route/objective compass.
  - Points testers toward the nearest safe, unfinished task.
  - Displays flood pressure hint and task interaction guidance.

- `BetaRouteLandmarkPolisher`
  - Adds large temporary route signage and colored route trim to the generated map.
  - Intended to reduce confusion while the final authored map is still being built.

- `BetaPlaytestDiagnostics`
  - F8 dumps a snapshot to Console and clipboard.
  - Includes network, round, flood zone, and task state data.

- `BetaMatchFlowSanityMonitor`
  - Server-side softlock monitor for task availability.
  - Auto-resets cancelled/failed tasks back to idle.
  - Warns if the match has no usable tasks.

## Controls

- F4: Toggle objective compass.
- F8: Copy playtest diagnostic snapshot.

## Test Checklist

1. Apply patch and let Unity compile.
2. Start Host.
3. Confirm F4 compass appears and points toward an unfinished safe task.
4. Walk/fall below the map deliberately in editor and verify server recovery.
5. Complete/cancel several tasks and confirm failed/cancelled tasks do not stay broken.
6. Trigger flood and confirm compass/flood hint updates.
7. Press F8 and confirm snapshot is copied/logged.

## Known Limits

This is still a beta reliability layer. It does not replace the need for a final authored map, final fluid simulation, final UI prefabs, or final networked task minigames.
