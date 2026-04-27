# Hue Done It Dynamic Loop/HUD/Map/Flood Patch

Included:
- Gameplay HUD explicitly displays Stability, Diffusion, Objectives, ship explosion countdown, and inventory.
- Emergency HUD mirrors those values when the UGUI HUD binding fails.
- Task interaction is radius-bound: leaving the task radius cancels/resets most task progress, and pressing E again closes/cancels the active task.
- Task UI hides when the player leaves range.
- Flood warning light presenter added. Flood telegraphs now pulse warning beacons before surge/flood danger.
- Gameplay map receives more intentional route language: central plaza, task pockets, route arrows, fewer random generated clutter objects.
- Flood pacing is less immediate and more readable.
- Generated splat masks are more organic/noisy and global splats get non-square variation.

Deferred:
True runtime fluid simulation emitted from player bodies is not implemented here. Current behavior remains event-driven splats/drips/stains. A real fluid solver needs its own networking/performance pass.
