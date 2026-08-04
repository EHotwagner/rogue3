// The DECLARED public surface of this module (EHotwagner/rogue3#96, constitution principle III:
// "Public Surface Is Declared, Not Incidental"). A binding absent from this file is private to
// the implementation and is NOT product API -- adding one here is a contracted (Tier 1) change.
module Rogue3.Determinism

/// The canonical text encoding of a value. Equal encodings mean equal values for every shape the
/// simulation uses; the encoding never truncates.
val encode: value: obj -> string

/// UTF-8 bytes of the canonical encoding, with no byte-order mark.
val bytes: value: obj -> byte array

/// Lower-case sha256 of the canonical bytes — the compact form for evidence and receipts.
val digest: value: obj -> string
