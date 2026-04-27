# Hue Done It Beta Loop / Map / UI / Spectate Patch

This pass turns several silent-failure beta systems into explicit runtime endpoints.

## Included runtime changes

- Gameplay HUD is now guaranteed by `GameplayBetaSceneInstaller` and still renders a binding fallback when the local avatar or round state is missing.
- Dead local players release their owner first-person camera and enter spectator mode.
- Spectator mode supports following living players and a free-floating camera.
- Generated Undertint map has wider main route slabs, meeting/escape/pump landmarks, smaller central flood volume, and slower flood pacing.
- Coolant reroute task steps were moved away from the flood valve steps to reduce interactable overlap.
- Round state now fails safely if a broken setup produces no active objectives.
- Critical task lock counting no longer double-counts locked tasks.
- Netcode fixed-string assignments are routed through a safe UTF-8 clamp utility to avoid runtime truncation exceptions from long system text.

## Spectator controls

- Tab / Right Bracket: next living player
- Left Bracket: previous living player
- F: toggle free camera
- WASD: free camera planar movement
- Space / Ctrl: free camera up/down
- Shift: faster free camera
- Esc: unlock cursor
- Left click: re-lock cursor

## Manual QA

1. Start Gameplay_Undertint as host.
2. Verify HUD appears immediately, even before all network bindings are ready.
3. Verify spawn/floor/jump still work.
4. Kill or diffuse the local player.
5. Verify the local camera switches to spectator.
6. Cycle between living players.
7. Toggle free camera and fly around.
8. Let the round continue until task win, bleach win, no-objective safe failure, or flood timer resolution.
