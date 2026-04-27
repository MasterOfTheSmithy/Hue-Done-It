# Hue Done It - Beta Hardening Pass 2

This patch is intended to make the current build less unplayable while keeping the existing project architecture intact.

## Major changes

- Added a permanent IMGUI fallback HUD: `BetaGameplayEmergencyOverlay`.
  - Shows even if the generated UGUI HUD fails to bind.
  - F1 toggles it.
  - Shows phase, timer, objective, role/life state, inventory summary, and spectator controls.
- Strengthened gameplay HUD installation in `GameplayBetaSceneInstaller`.
  - Creates an EventSystem when missing.
  - Reactivates existing HUD/canvas instead of silently doing nothing.
  - Installs the emergency overlay.
- Expanded the generated beta map.
  - Added west/east bypass lanes.
  - Added north/south task lanes.
  - Added high-contrast map landmarks.
  - Added 10 spawn pads for 10-player tests.
  - Removed duplicate round event director install call.
- Added task endpoint protection in `NetworkRepairTask`.
  - In-progress tasks now time out and cancel server-side if they get stuck because a client disconnects, dies, leaves range, or the local participant state fails to send terminal RPCs.
- Hardened player and CPU interaction execution.
  - Player interaction RPC now catches interactable exceptions and logs a safe failure instead of letting NGO rethrow an unhandled RPC exception.
  - CPU direct interactions now use the same safe wrapper so one broken station does not spam-crash every frame.
- Added runtime fallback remains for kills.
  - `EliminationManager` now creates reportable primitive remains if the authored remains prefab reference is missing.
  - This preserves the body report -> meeting loop instead of failing with only a log error.
- Improved spectator target selection.
  - Dead-player spectator prefers living human players.
  - Falls back to CPUs only if no living human targets exist.

## Test checklist

1. Open `Gameplay_Undertint` through the normal host/lobby flow.
2. Confirm at least one HUD exists. If UGUI is missing, the F1 fallback overlay should appear.
3. Start a task, walk away, and confirm it cancels instead of staying stuck forever.
4. Kill a player with no remains prefab assigned and confirm a reportable fallback body appears.
5. Report the body and verify meeting/voting opens.
6. Die locally and verify spectator mode:
   - Tab / ] cycles next player.
   - [ cycles previous player.
   - F toggles free camera.
   - WASD / Space / Ctrl moves free camera.
7. Let CPU players use stations and confirm station exceptions do not spam unrecoverable RPC errors.
8. Check that the map has readable bypass lanes and 10 spawn pads.
