// The DECLARED public surface of this module (EHotwagner/rogue3#96, constitution principle III:
// "Public Surface Is Declared, Not Incidental"). A binding absent from this file is private to
// the implementation and is NOT product API -- adding one here is a contracted (Tier 1) change.
module Rogue3.AudioSynthesis

/// The complete WAV bytes for one declared cue id.
///
/// TOTAL OVER THE DECLARATION, AND ONLY OVER IT. An id `AudioCueIds` does not export raises rather
/// than returning an empty buffer: a typo must be loud at generation time, not a silent zero-length
/// asset that satisfies a naive existence check.
val render: id: string -> byte array

/// `<id>.wav` — the file name `AudioCues.resolver` looks for under `assets/audio`.
val fileName: id: string -> string
