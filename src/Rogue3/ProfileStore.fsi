// The DECLARED public surface of this module (EHotwagner/rogue3#96, constitution principle III:
// "Public Surface Is Declared, Not Incidental"). A binding absent from this file is private to
// the implementation and is NOT product API -- adding one here is a contracted (Tier 1) change.
module Rogue3.ProfileStore

type LoadResult =
    | Loaded of Model.MetaProfile
    | Absent
    | Unreadable of string

val profileFileName: string

val platformProfilePath: unit -> string

val load: path: string -> LoadResult

type AtomicWriteEvidence =
    {
      DestinationPath: string
      TempPath: string
      TempCreated: bool
      Renamed: bool
      TempExistsAfter: bool
    }

val writeAtomic: path: string -> profile: Model.MetaProfile -> unit

/// Host-owned debounce coordinator. Requests replace the pending profile; the callback performs
/// one atomic write after the quiet window. Tests can call Flush to make the boundary deterministic.
type Store =
    interface System.IDisposable
    new: path: string * debounce: System.TimeSpan -> Store
    member Flush: unit -> unit
    member Load: unit -> LoadResult
    member Request: profile: Model.MetaProfile -> unit
    member LastWrite: AtomicWriteEvidence option
    member Path: string
    member TempPath: string
    member WriteCount: int
