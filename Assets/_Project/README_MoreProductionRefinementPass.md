# Hue Done It - More Production Refinement Pass

This patch is a continuation/refinement pass layered on top of the previous beta hardening and flood-light fixes.

## What this pass adds

### Self-installing beta runtime systems

Added:

- `Assets/_Project/Gameplay/Beta/BetaProductionRuntimeInstaller.cs`
- `Assets/_Project/Gameplay/Beta/BetaAlwaysOnHudOverlay.cs`
- `Assets/_Project/Gameplay/Beta/BetaTaskEndpointGuard.cs`
- `Assets/_Project/Gameplay/Beta/BetaTaskWorldAffordancePresenter.cs`
- `Assets/_Project/Gameplay/Beta/BetaProductionMapPolisher.cs`
- `Assets/_Project/Gameplay/Beta/BetaFloodWarningBeaconInstaller.cs`
- `Assets/_Project/Gameplay/Beta/BetaRuntimePaintBudgetTuner.cs`

These systems self-install in gameplay-like scenes. They are additive and intentionally avoid depending on fragile scene references.

## Gameplay improvements

- Always-on beta HUD with Stability, Diffusion, Objective, ship explosion countdown, inventory, task state, and life/spectator state.
- F2 toggles the beta HUD.
- Server task watchdog cancels/reset-stabilizes tasks when the participant disconnects, dies, leaves range, or the task becomes unsafe.
- World-space task labels and interaction-radius rings make task UI behavior readable.
- Runtime route slabs and landmarks make the current beta map less cluttered and more intentional.
- Runtime flood warning beacons are created if the scene lacks warning-light wiring.
- Runtime paint budget tuning reduces replicated paint spam during heavy movement/splatting.

## Manual QA

1. Start host and enter Gameplay_Undertint.
2. Confirm F2 HUD shows Stability, Diffusion, Objectives, Countdown, Inventory.
3. Start a repair task, walk out of the visible ring, confirm progress cancels/resets.
4. Press E again while working a task and confirm task UI closes/cancels.
5. Trigger flood or wait for flood pressure, confirm flashing warning beacons.
6. Run around with CPU players enabled and verify paint no longer tanks the session as quickly.
7. Confirm map has clearer lanes/landmarks without blocking critical task access.

## Known limitations

- This is still not a true fluid simulation. It constrains/improves the current splat system while leaving the proper fluid/stain RT project for a dedicated pass.
- The runtime map layer is beta readability geometry, not final art.
- The HUD overlay is deliberately robust and visible; once the authored UGUI HUD is stable, this can be turned into a debug fallback.
