# M6 raster eye-check

- Inspected all four 1280×720 PNGs at original resolution on 2026-08-01.
- Production frame: all eight enemies are visible, separated, unclipped, and ordered behind the player/HUD. Enemy-red faction strokes, green health arcs, silhouette classes, and Ring/Bolt/Fang sigils remain readable on the dark floor.
- Token candidate: facing and class silhouette remain immediate across the complete eight-kind roster; exact physical radius differences are visible.
- Badge candidate: stable and readable, but its screen-aligned body weakens world-facing orientation.
- Ring candidate: stable and readable, but radial sameness makes Scout/Mobile/Heavy discrimination less immediate.
- Decision: accept Token. No blank frame, clipping, overlap, tofu, or illegible-color defect observed.
- Machine linter cross-check: one `Warning Size` only, zero errors; accepted because the rendered radius is the exact collision radius.
