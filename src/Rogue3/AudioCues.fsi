// The DECLARED public surface of this module (EHotwagner/rogue3#96, constitution principle III:
// "Public Surface Is Declared, Not Incidental"). A binding absent from this file is private to
// the implementation and is NOT product API -- adding one here is a contracted (Tier 1) change.
module Rogue3.AudioCues

/// Where `resolver` looks for PCM WAV files, relative to the running rogue3.
[<Literal>]
val assetRoot: string = "assets/audio"

/// The rogue3 owns the id -> asset mapping; the framework never does (FS.GG.Audio FR-005).
/// An id with no file on disk resolves to `None`, which the backend treats as a recorded no-op —
/// so a rogue3 with no assets yet still runs, and still requests the right sounds.
///
/// M12: "still runs" is the *degradation*, not the target. Every id `AudioCueIds` declares has a
/// committed asset under `assets/audio/`, and `M12AudioAssetTests` reds if any one of them stops
/// resolving through THIS value — the same resolver `Program.fs` hands to `OpenAlBackend.create`.
/// Before M12 the directory did not exist at all and the whole cue set was silent, while the suite
/// stayed green because every audio obligation asked what the product *requested*.
///
/// Model-agnostic on purpose: this half survives a model swap even though `forTransition` does not,
/// which is why it sits above the per-starter split below.
val resolver: FS.GG.Audio.Host.AssetResolver

/// Shell transitions are outside the play-model Msg stream, but they replace the same single loop.
val replaceWithTitleMusic: unit -> FS.GG.Audio.Core.AudioEffect list

val replaceWithCurrentMusic:
  model: Model.Model -> FS.GG.Audio.Core.AudioEffect list

/// What this rogue3 asks to hear when `msg` takes it from `previous` to `next`.
/// Return `[]` for a silent transition. Effects play in list order.
///
/// Drop a WAV at `assets/audio/<id>.wav` and you hear it; leave it out and the request is recorded
/// but silent. Add your own cases — this is your file.
val forTransition:
  msg: Model.Msg ->
    previous: Model.Model ->
    next: Model.Model -> FS.GG.Audio.Core.AudioEffect list
