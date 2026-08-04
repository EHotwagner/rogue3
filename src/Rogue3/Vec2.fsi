// The DECLARED public surface of this module (EHotwagner/rogue3#96, constitution principle III:
// "Public Surface Is Declared, Not Incidental"). A binding absent from this file is private to
// the implementation and is NOT product API -- adding one here is a contracted (Tier 1) change.
namespace Rogue3

    /// Rogue3-owned collision-safe 2D vector — THIS FILE IS YOURS TO ADAPT.
    ///
    /// A game model that stores entity positions/velocities collides with the shared scene vocabulary if
    /// it reuses the record labels `FS.GG.UI.Scene.Point` (`X`,`Y`) and `Rect` (`X`,`Y`,`Width`,`Height`)
    /// use. Because the durable `LayoutEvidence.fs` opens BOTH `FS.GG.UI.Scene` and your model, F#'s
    /// record-label inference can then resolve the bare `{ X = …; Y = …; Width = …; Height = … }` literals
    /// in that durable file to YOUR record instead of `Rect` — a wall of `FS3566`/`FS0039` in a file you
    /// were told not to touch, surfacing only after a whole model is written (fs-gg-scene pitfall).
    ///
    /// `Vec2` avoids the trap structurally: its labels `Vx`/`Vy` ("vector component x/y") share NO name
    /// with `Point`/`Rect`, so a model built on `Vec2` can never trip the mis-inference. Use it for
    /// position, velocity, and displacement; cross into the scene vocabulary with `toPoint`/`toRect` —
    /// the ONE place bare `Scene` record literals appear in your rogue3 tree, where only `Scene` types
    /// are in scope and the resolution is unambiguous. Express an entity's SIZE through `toRect` (a
    /// centered AABB) rather than `Width`/`Height` labels on the record, so the size case stays safe too.
    ///
    /// TWO framework vocabularies exist and they are LABEL-IDENTICAL: the render one
    /// (`FS.GG.UI.Scene.Point`/`Rect`, opened above) and the simulation one
    /// (`FS.GG.Game.Core.Point`/`Rect`), which is what the shipped `Collision.fs` helper and
    /// `SpatialGrid` actually speak. Cross into render space with `toPoint`/`toRect`, and
    /// into sim space with `toSimPoint`/`ofSimPoint`/`toSimRect`/`ofSimRectCenter`. This file deliberately does NOT
    /// `open FS.GG.Game.Core`: that would put two identically-labelled `Point`s (and two `Rect`s) in one
    /// scope and re-create the very ambiguity `Vx`/`Vy` exists to prevent. The sim crossings go through
    /// the `SimPoint`/`SimRect` abbreviations plus a return-type annotation instead, which names the
    /// target record unambiguously — the pattern to copy whenever you must build a `Game.Core` value in
    /// a file that already sees `Scene` (the durable `Model.fs` is exactly such a file).
    ///
    /// Everything here is pure, total, and deterministic: straight-line float arithmetic that never throws,
    /// so identical inputs yield byte-identical output across runs and platforms — safe to call from a
    /// replayed `update`.
    ///
    /// TOTAL MEANS "NEVER THROWS" — IT DOES NOT MEAN "NEVER NaN". With ONE exception (`clamp`, below), the
    /// helpers here do not sanitise non-finite input; they PROPAGATE it. `add`/`sub`/`scale` on a NaN
    /// component yield NaN, `toPoint`/`toSimPoint` carry it into the scene/sim vocabularies, and
    /// `toRect`/`toSimRect` carry it into a `Rect` — into the origin (derived from the centre) and into the
    /// size as well, because that guard is `abs`, and `abs nan = nan`. Sanitising is YOUR job — see
    /// `isFinite`. It is deliberately not done for you: rewriting a NaN to `0.0` inside these helpers would
    /// convert a loud modelling bug into an entity that silently teleports to the origin, which is harder to
    /// find, not easier.
    ///
    /// `clamp` IS THE EXCEPTION, AND IT IS A TRAP RATHER THAN A GUARD. It does not propagate a NaN, it
    /// SWALLOWS one: the comparison runs through `max`/`min`, every comparison against a NaN is false, so a
    /// NaN component falls out as the LOW bound `lo` — not as NaN. An entity whose position has gone bad
    /// therefore does not LOOK bad: it silently snaps to the corner of the clamp box and keeps playing. That
    /// is emergent from IEEE-754, not a designed sanitiser, so do not lean on it — it launders only one of
    /// the two failure modes (an infinity clamps sensibly, to `hi`/`lo`), and it is the reason to call
    /// `isFinite` where the float ENTERS the model rather than trust a downstream `clamp` to absorb it.
    ///
    /// Know what a NaN costs you downstream, because it will not announce itself. The shipped `Collision`
    /// helpers document non-finite bounds as NEVER OVERLAPPING (every comparison against a NaN is false), so
    /// a single NaN component does not crash the step — it silently turns collision OFF for that entity, and
    /// it walks through walls. Guard where floats ENTER your model (a division you did not prove non-zero, a
    /// parsed save file, an impulse off a bad delta), not on every helper call.
    ///
    /// You own this file: rename `Vx`/`Vy`, add a `Z`, add rotation/normalization, or delete it after you
    /// swap `Model.fs` off it (its compile item is `Exists`-guarded, so deletion keeps the build green).
    /// See the `fs-gg-model-swap` / `fs-gg-game-core` skills. Guidance-only; no backing package.
    module Geometry =
        /// A 2D vector — position, velocity, or displacement. `Vx`/`Vy` deliberately avoid `X`/`Y`
        /// (`Scene.Point`) and `Width`/`Height` (`Scene.Rect`) so a model built on this never collides.
        type Vec2 =
            {
              Vx: float
              Vy: float
            }
        /// The simulation-space point, under a local name. `FS.GG.Game.Core.Point` carries the SAME
        /// labels as `Scene.Point` (`X`/`Y`), so it is reached through this abbreviation rather than an
        /// `open`: with both vocabularies in one scope a bare record literal is ambiguous. The
        /// abbreviation plus the return-type annotation on each `*Sim*` crossing below is what resolves it.
        type SimPoint = FS.GG.Game.Core.Point
        /// The simulation-space AABB, under a local name (see `SimPoint` for why it is not an `open`).
        /// This is what `Collision.Body.Bounds` and the `SpatialGrid` range queries speak.
        type SimRect = FS.GG.Game.Core.Rect
        /// The zero vector.
        val zero: Vec2
        /// Construct a vector from its components.
        val vec2: x: float -> y: float -> Vec2
        /// Is every component finite — neither NaN nor an infinity? This is the guard the header sends you
        /// to, and NOTHING in this module calls it for you: what a bad float should BECOME (drop the entity,
        /// clamp it, keep the last good value, fail the load) is a policy only the rogue3 can pick, and a
        /// helper that quietly picked `0.0` for you would hide the bug it was handed. Call it where floats
        /// enter the model, and note the payoff is not a crash you avoid but a crash you GET: an unguarded
        /// NaN position does not throw, it just makes `Collision` stop seeing the entity.
        val isFinite: v: Vec2 -> bool
        /// Component-wise addition (e.g. advance a position by a displacement).
        val add: a: Vec2 -> b: Vec2 -> Vec2
        /// Component-wise subtraction.
        val sub: a: Vec2 -> b: Vec2 -> Vec2
        /// Scalar multiply (e.g. `add pos (scale dt vel)` integrates one step).
        val scale: k: float -> v: Vec2 -> Vec2
        /// Per-component clamp into `[lo, hi]` — keep an entity inside a bound. Total: if a `lo` axis
        /// exceeds its `hi` axis the low bound wins (no throw), so a degenerate bound can never crash a step.
        ///
        /// WARNING — this is the one helper here that does NOT propagate a NaN, and that is a trap, not a
        /// feature. Every comparison against a NaN is false, so `max`/`min` fall through and a NaN component
        /// comes out as `lo`: a position that has silently gone bad silently snaps to the corner of your
        /// bound and keeps playing, instead of staying visibly NaN. Do not use `clamp` as your finite guard —
        /// it hides NaN and only NaN (an infinity clamps sensibly to `hi`/`lo`). Use `isFinite` at the
        /// boundary where the float enters the model.
        val clamp: lo: Vec2 -> hi: Vec2 -> v: Vec2 -> Vec2
        /// Cross into the shared scene vocabulary: a `Vec2` position becomes a `Scene.Point`.
        val toPoint: v: Vec2 -> FS.GG.UI.Scene.Point
        /// A centered axis-aligned rectangle of size `w` x `h` about `center` — the size-bearing case,
        /// expressed WITHOUT ever putting `Width`/`Height` labels on your own record. Negative sizes are
        /// treated as their magnitude (total), so a stray sign can never invert the rect.
        val toRect: center: Vec2 -> w: float -> h: float -> FS.GG.UI.Scene.Rect
        /// Cross into the SIMULATION vocabulary: a `Vec2` position becomes a `Game.Core.Point` — the type
        /// `SpatialGrid.build` keys on, and the one `Collision.Body.Velocity` carries. Without this you
        /// cannot call the collision helper this scaffold ships.
        val toSimPoint: v: Vec2 -> SimPoint
        /// Cross BACK out of the simulation vocabulary: a `Game.Core.Point` handed to you by a helper —
        /// a `Collision.Contact.Penetration`, say — becomes a `Vec2` your model can store without ever
        /// declaring an `X`/`Y` label of its own.
        val ofSimPoint: p: SimPoint -> Vec2
        /// The sim-space twin of `toRect`: a centered axis-aligned rectangle of size `w` x `h` about
        /// `center`, shaped as `Collision.Body.Bounds` wants it. Negative sizes are treated as their
        /// magnitude (total), so a stray sign can never invert the rect.
        val toSimRect: center: Vec2 -> w: float -> h: float -> SimRect
        /// Cross BACK out of the simulation vocabulary at the RECT: the CENTRE of a `Game.Core.Rect`,
        /// as a `Vec2`. This is the return leg of `toSimRect`, and you need it because `Collision.resolve`
        /// applies a separation by MOVING the body's rect (`Resolution.A.Bounds`) — so the post-separation
        /// position a model must store comes back as a `Rect`, not a `Point`. Without this the consumer is
        /// back to hand-writing the bridge. Size is deliberately not returned: a `Vec2` model expresses
        /// size through `toSimRect`/`toRect`, never as labels on its own record.
        val ofSimRectCenter: r: SimRect -> Vec2
