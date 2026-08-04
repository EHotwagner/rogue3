// The DECLARED public surface of this module (EHotwagner/rogue3#96, constitution principle III:
// "Public Surface Is Declared, Not Incidental"). A binding absent from this file is private to
// the implementation and is NOT product API -- adding one here is a contracted (Tier 1) change.
module Rogue3.Replay

/// One ordered host or simulation message in a replay. `Sequence` exists so a truncated, reordered
/// or duplicated log is rejected rather than silently replayed as a different run.
type InputLogEntry =
    {
      Sequence: int
      Message: Model.Msg
    }

/// Canonical, non-truncating byte encoding of a generated floor (§14.1).
val floorBytes: floor: FloorGeneration.Floor -> byte array

/// Canonical, non-truncating byte encoding of a whole simulation value (§13).
val modelBytes: model: Model.Model -> byte array

/// A log is canonical only when its sequence numbers are unique and strictly increasing.
val isCanonical: entries: InputLogEntry list -> bool

/// Re-run a seed plus its exact ordered message/timing log through the production update function.
/// Invalid ordering is rejected instead of silently reordering or dropping player actions.
val replay: seed: uint64 -> entries: InputLogEntry list -> Model.Model

/// Record a run from a seed by folding the same log the replay will consume, returning the final
/// model and its canonical bytes. Recording and replaying therefore cannot diverge by construction;
/// the test that matters is that a SECOND independent replay of the same log matches these bytes.
val record:
  seed: uint64 -> entries: InputLogEntry list -> Model.Model * byte array
