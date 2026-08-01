# M6 raster eye-check

- Inspected all four 1280×720 PNGs at original resolution on 2026-08-01.
- Production frame: all eight enemies plus the Maw boss are visible, separated, unclipped, and ordered behind the player/HUD. The frame also contains all five obstacle kinds, all six visible pickup kinds, three shop offers, open/locked/sealed doors, room drop, room reward, trapdoor, a placed bomb, both projectile sides, shadows, and dispersed particles. Enemy-red faction strokes, green health arcs, silhouette classes, Ring/Bolt/Fang sigils, and speed tiers remain readable on the dark floor.
- Token candidate: facing and class silhouette remain immediate across the complete eight-kind roster; exact physical radius differences are visible.
- Badge candidate: stable and readable, but its screen-aligned body weakens world-facing orientation.
- Ring candidate: stable and readable, but radial sameness makes Scout/Mobile/Heavy discrimination less immediate.
- Same-frame contact sheet: Token, Badge, and Ring columns retain identical positions and hard-content clutter, so differences are attributable to grammar rather than fixture drift.
- Decision: accept Token. No blank frame, clipping, overlap, tofu, or illegible-color defect observed.
- Machine linter cross-check: one `Warning Size` only, zero errors; accepted because the rendered radius is the exact collision radius.
