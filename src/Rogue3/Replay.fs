module Rogue3.Replay

// Seed + input log replay (source specification §13): "Same `runSeed` ⇒ identical floors and
// identical drop sequence GIVEN IDENTICAL PLAYER ACTIONS/TIMING."
//
// Timing is not a separate channel here. A `Tick dt` message carries its own elapsed seconds, so an
// ordered list of production messages IS the actions-plus-timing record, and replay never reads a
// wall clock. The fold is the production `Model.update`; there is no replay-only transition to
// drift away from the shipped one.

open Rogue3.FloorGeneration
open Rogue3.Model

/// One ordered host or simulation message in a replay. `Sequence` exists so a truncated, reordered
/// or duplicated log is rejected rather than silently replayed as a different run.
type InputLogEntry = { Sequence: int; Message: Msg }

/// Canonical, non-truncating byte encoding of a generated floor (§14.1).
let floorBytes (floor: Floor) = Determinism.bytes floor

/// Canonical, non-truncating byte encoding of a whole simulation value (§13).
let modelBytes (model: Model) = Determinism.bytes model

/// Canonical byte encoding of the log itself, so a shared seed and log can be compared directly.
let inputLogBytes (entries: InputLogEntry list) = Determinism.bytes entries

let floorDigest (floor: Floor) = Determinism.digest floor
let modelDigest (model: Model) = Determinism.digest model

/// A log is canonical only when its sequence numbers are unique and strictly increasing.
let isCanonical (entries: InputLogEntry list) =
    entries
    |> List.map _.Sequence
    |> List.pairwise
    |> List.forall (fun (previous, next) -> next > previous)

/// Re-run a seed plus its exact ordered message/timing log through the production update function.
/// Invalid ordering is rejected instead of silently reordering or dropping player actions.
let replay seed (entries: InputLogEntry list) =
    if not (isCanonical entries) then
        invalidArg (nameof entries) "Replay sequence numbers must be unique and strictly increasing."

    entries
    |> List.fold (fun model entry -> update entry.Message model |> fst) (initialModelForSeed seed)

/// Record a run from a seed by folding the same log the replay will consume, returning the final
/// model and its canonical bytes. Recording and replaying therefore cannot diverge by construction;
/// the test that matters is that a SECOND independent replay of the same log matches these bytes.
let record seed entries =
    let model = replay seed entries
    model, modelBytes model
