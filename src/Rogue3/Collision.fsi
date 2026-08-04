// The DECLARED public surface of this module (EHotwagner/rogue3#96, constitution principle III:
// "Public Surface Is Declared, Not Incidental"). A binding absent from this file is private to
// the implementation and is NOT product API -- adding one here is a contracted (Tier 1) change.
namespace Rogue3

    /// Rogue3-owned collision helper — THIS FILE IS YOURS TO ADAPT.
    ///
    /// Detection reuses the framework primitives (no hand-rolled AABB, no look-alike geometry type):
    ///   * narrow-phase overlap uses `FS.GG.UI.Scene.Geometry` on the shared `Rect`/`Point`;
    ///   * broad-phase pruning uses `FS.GG.UI.Canvas.SpatialGrid`.
    /// The *response* rule (`resolve`, below) is the game-opinionated part — edit it, add collision
    /// layers, or delete this whole file (the build stays green: its compile item is `Exists`-guarded).
    ///
    /// Everything here is pure, total, and deterministic: pairs are formed in ascending body-index order
    /// and the response math is sqrt-free (the swept narrow-phase uses a single deterministic IEEE `sqrt`
    /// for the impact distance), so identical inputs yield byte-identical output across runs and platforms
    /// — safe to call from a replayed `update`.
    module Collision =
        /// A collidable thing: its axis-aligned bounds at the START of the step, the per-step displacement
        /// (`velocity × dt`) it travels this step, plus a caller-supplied identity/layer payload. A wall (or
        /// any body at rest) has `Velocity = { X = 0.0; Y = 0.0 }` and behaves exactly as a static body; give
        /// a fast mover its real per-step displacement so the swept pass (`collide`/`step`) tests the whole
        /// path `Bounds → Bounds + Velocity` and cannot tunnel it clean through a thin target in one step.
        /// `Tag` is generic (like `SpatialGrid<'T>`) so you never define a look-alike record just to
        /// carry an id — avoiding the consumer-vs-consumer `.Pos`/`.Id` inference footgun.
        type Body<'T> =
            {
              Bounds: FS.GG.Game.Core.Rect
              Velocity: FS.GG.Game.Core.Point
              Tag: 'T
            }
        /// A detected overlap between two bodies and how to separate them (pure detection result).
        /// `A` is always the lower-index body of the pair, `B` the higher — a stable, total order.
        /// `Penetration` is the minimum-translation vector that pushes `A` off `B` (push `B` off `A`
        /// with its negation). `Depth` is the overlap along the MTV axis (>= 0).
        type Contact<'T> =
            {
              A: Body<'T>
              B: Body<'T>
              Penetration: FS.GG.Game.Core.Point
              Depth: float
            }
        /// Post-response state for a contact. `Applied` is the displacement given to `A` (for the
        /// consumer's own velocity/response bookkeeping). `Restitution` is a normalized bounce factor
        /// (0.0..1.0) the consumer can fold into its velocity step — the helper itself only separates.
        type Resolution<'T> =
            {
              A: Body<'T>
              B: Body<'T>
              Applied: FS.GG.Game.Core.Point
              Restitution: float
            }
        /// How overlapping bodies separate. THIS is the policy to edit per game.
        type ResponseRule =
            /// Split the minimum-translation 50/50 — both bodies move (default).
            | SeparateEqually
            /// The FIRST body takes the full push; the second is immovable (a wall).
            | PushFirst
            /// The SECOND body takes the full push; the first is immovable (a wall).
            | PushSecond
            /// 50/50 separation with no recorded restitution (slide along the surface).
            | Slide
            /// 50/50 separation plus a recorded restitution (`restitutionPercent`, clamped to 0..100)
            /// for the consumer's velocity reflection — kept as an integer percent so two equal-strength
            /// bounces can never tie-break through floating-point equality.
            | Bounce of restitutionPercent: int
        /// Narrow-phase: the minimum-translation contact between two bodies, or `None` when they do not
        /// overlap on positive area. Edge-/corner-touching is NOT a contact (strict edges — this defers
        /// to `Geometry.intersects`). Total: non-finite bounds never overlap and never throw.
        val contact: a: Body<'T> -> b: Body<'T> -> Contact<'T> option
        /// The moving narrow-phase: a `Contact` when `a` and `b` overlap at the start of the step OR when
        /// `a`'s swept path — `Bounds → Bounds + Velocity`, taken relative to `b`'s own motion — crosses `b`
        /// during the step. The second case is exactly the tunnelling a static overlap test misses: a body
        /// moving faster than the target is thick is in front of it before the step and behind it after, so a
        /// point test at either end reports a clean miss on a pair that did collide. Defers to `contact` for
        /// an existing overlap (identical MTV); for a pure crossing it advances `a` to first contact along its
        /// path (the minimum translation that stops it AT `b`'s surface rather than past it). Total (NaN-safe)
        /// and deterministic. `collide`/`step` use THIS, not the bare `contact`.
        val sweptContact: a: Body<'T> -> b: Body<'T> -> Contact<'T> option
        /// Broad-phase (SpatialGrid) + swept narrow-phase over every body pair, returned in ascending
        /// (i, j) index order so the result is fully deterministic. `cellSize` tunes the grid; the query
        /// region is expanded by the largest body half-extent AND the largest per-step displacement so no
        /// overlap — including one a fast body only touches mid-sweep — is missed (exact, no false
        /// negatives). Total on empty/singleton input (returns `[]`).
        val collide:
          cellSize: float -> bodies: Body<'T> list -> Contact<'T> list
        /// Apply the response rule to a contact, returning the separated bodies. Pure and deterministic.
        /// EDIT THIS to change how your game resolves overlaps.
        val resolve: rule: ResponseRule -> c: Contact<'T> -> Resolution<'T>
        /// One per-frame pass: detect every collision over each body's swept step (so a fast mover cannot
        /// tunnel a thin target) and resolve it under `rule`, in deterministic pair order. This is the
        /// function most games call from `update`. A single swept pass per frame; for dense stacking, call it
        /// again on the resolved bodies or add your own iteration.
        val step:
          rule: ResponseRule ->
            cellSize: float -> bodies: Body<'T> list -> Resolution<'T> list
        /// Clamp a circle's CENTRE so the whole disc stays inside `bounds`, each axis inset by the radius —
        /// the playfield bound a moving hitbox needs so it cannot leave the map. Total: a `bounds` narrower
        /// than the disc on an axis pins the centre to that axis' high inset (`min hi (max lo v)`), and a
        /// non-finite centre or radius clamps to a finite bound rather than throwing. Pure, deterministic.
        val clampCircleInside:
          bounds: FS.GG.Game.Core.Rect ->
            c: FS.GG.Game.Core.Circle -> FS.GG.Game.Core.Circle
        /// Move circle `c` by `displacement` (velocity × dt) against STATIC `walls`, resolving the X move
        /// and the Y move INDEPENDENTLY so a wall that stops one axis does not cancel motion on the other —
        /// the "slide along the wall" feel a player hitbox wants (dead against a wall on X, still free on Y).
        /// Each axis pass advances that axis, then folds the walls in LIST ORDER, pushing the circle out of
        /// any wall it penetrates by the AXIS COMPONENT of the minimum-translation vector
        /// `Geometry.circleAabbContact` reports (the circle's separation is `−Normal × Depth`; a flat face
        /// gives an axis-aligned MTV so the off-axis component is zero and the other axis is untouched — that
        /// is the slide). When `bounds` is `Some`, the final centre is clamped inside it (inset by the
        /// radius) via `clampCircleInside`. Walls are IMMOVABLE; pass them in a stable order for a
        /// byte-identical result. Pure, total (NaN-safe: a non-finite displacement axis contributes nothing,
        /// and `circleAabbContact` treats a NaN/non-positive radius as no contact), and deterministic.
        ///
        /// This is a single MOVE-AND-RESOLVE step, NOT a swept cast: it advances the centre then separates
        /// the resulting overlap, which lands the circle on a wall's near face only while the moved centre
        /// stays in that wall's near half. A player hitbox against tile-sized walls never leaves that
        /// regime (its per-step displacement is well under the wall thickness), which is what this helper is
        /// for. A mover fast enough to overshoot a wall's midline in one step — a PROJECTILE — would tunnel;
        /// that is the swept `collide`/`step` pass above's job (`Body.Velocity`, #290), or call this in
        /// sub-steps each no longer than the radius so consecutive discs overlap. One pass also assumes
        /// SPARSE walls; for a very dense cluster call again on the result, exactly as `step` documents.
        val slideCircle:
          bounds: FS.GG.Game.Core.Rect option ->
            walls: FS.GG.Game.Core.Rect list ->
            c: FS.GG.Game.Core.Circle ->
            displacement: FS.GG.Game.Core.Point -> FS.GG.Game.Core.Circle
        /// Axis-separated swept movement for a fast-enough circular player. Each axis casts the centre
        /// against every wall expanded by the circle radius and stops at the nearest stable-order hit,
        /// then the ordinary circle contact fold removes any starting overlap. This keeps the responsive
        /// X-then-Y slide policy while preventing even a thin obstacle from being crossed in one step.
        val sweepCircle:
          bounds: FS.GG.Game.Core.Rect option ->
            walls: FS.GG.Game.Core.Rect list ->
            c: FS.GG.Game.Core.Circle ->
            displacement: FS.GG.Game.Core.Point -> FS.GG.Game.Core.Circle
