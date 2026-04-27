# Hue Done It - Movement / Flood / Task Beta Production Pass

This patch was built against the uploaded `Assets.zip`.

Primary runtime blockers fixed:
- `GameplayInvestorHud` no longer requests deprecated `Arial.ttf`; it uses `LegacyRuntime.ttf`.
- Spawn selection no longer fails the full round when generated floor planes are on an unexpected layer.
- Global paint splats now discard destroyed pooled decals before reusing them, preventing RPC rethrow spam from `StainReceiver.SpawnGlobalSplat`.
- Task and round fixed-string assignments are clamped through `FixedStringUtility`.

Gameplay refinements added:
- Strict interaction distance for prompts; task prompts stop appearing from outside the task's own use radius.
- Floor/collision repair helper for generated walkable surfaces.
- Player stuck/fall recovery and server-side depenetration guard.
- Water color diffusion/staining from player color while submerged; death in water stains the water more strongly until drain/dry decay.
- Physical task props that animate/color-shift as tasks progress or complete.
- A basic point-click task overlay for active NetworkRepairTask work, with cancel/complete hooks through `PlayerRepairTaskParticipant`.

Test order:
1. Let Unity compile.
2. Host gameplay.
3. Confirm HUD appears with no Arial/LegacyRuntime error.
4. Confirm players spawn even when authored spawn floor masks are imperfect.
5. Slam/paint during crash; confirm no `MissingReferenceException` from global paint decal reuse.
6. Walk toward a task; prompt should appear only in task range.
7. Start a task; point-click overlay should appear and task prop should animate.
8. Enter flood water; water should slowly tint toward the player color.
9. Die/diffuse in flood water; water tint should become stronger and remain until the zone drains/drys.
10. Try to wedge against clutter/floor edges; stuck guard should depenetrate or recover if out of bounds.

Known limitation:
This is still a beta-friendly event/splat/tint approximation, not a full fluid simulation. True fluid emission/diffusion should be a dedicated GPU/render-texture project.
