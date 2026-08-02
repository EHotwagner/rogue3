module Rogue3.AudioCues

open System
open System.IO
open FS.GG.Audio.Core
open FS.GG.Audio.Host
open Rogue3.Geometry
open Rogue3.Model

// ─────────────────────────────────────────────────────────────────────────────────────────────
// Sound, as pure values (issue #245).
//
// `forTransition` is the ONLY place this rogue3 decides what to play. It is a pure function of the
// message and the before/after model — no device, no file handle, no `unit -> unit`. The host turns
// the returned values into real playback: `Program.fs` passes `Audio.play backend` to the launch
// entry point, and the viewer hands it each `ViewerEffect.PlayAudio` batch in dispatch order. Your
// `update` never changes.
//
// Why BOTH models are in the signature: most gameplay cues (a shot, a death, a pickup, a room seal)
// have NO `Msg` of their own — they are recovered by DIFFING `previous` against `next`, so you add
// them without touching `Msg` or `Model.fs`. The `scored`/`bounced` helpers below do exactly this.
// Its coverage boundary lives at the point of use — see the `Tick` cues.
//
// REPLACE ME, with Model.fs. This file names your `Msg` cases and reads your `Model`'s fields, so a
// model swap rewrites it. The seam that carries its output (`PlayAudio` + the `*WithAudio` launch)
// is durable and does not — see docs/scaffold-map.md.
//
// Test it the way you test `update` — by value, with no sound card:
//     AudioCues.forTransition SaveRequested before after = [ Audio.playSfx (SoundId "save") 0.7 ]
//
// ── Which host carries it (issue #436) ───────────────────────────────────────────────────────
//
// Both of them, through the same seam. The two starter profiles ship DIFFERENT `Msg` types, so the
// cue map below is written twice — but the SIGNATURE is identical, and both hosts in
// EvidenceCommands.fs call it from `Init` AND `Update`:
//
//     app                  -> `interactiveHost`, launched by `ControlsElmish.runInteractiveAppWithAudio`
//     game / sample-pack   -> `generatedHost`,   launched by `Viewer.runAppWithAudio`
//
// That the Controls family can request sound at all is #429; that a scaffolded rogue3 on the `app`
// profile can REACH it is #436 — before which `app` referenced none of the FS.GG.Audio packages and
// launched through a sinkless overload, so "every game with a menu" got a silent menu.
// ─────────────────────────────────────────────────────────────────────────────────────────────

/// Where `resolver` looks for PCM WAV files, relative to the running rogue3.
[<Literal>]
let assetRoot = "assets/audio"

/// M12 (DEC-009): the roots `assetRoot` is resolved against, in order.
///
/// The working directory alone is not enough, and that is not a theoretical complaint. A relative
/// path is resolved against whatever directory the *shell* was in, so the same built binary was
/// audible from one directory and silent from every other — including the directory the binary
/// itself lives in. `AppContext.BaseDirectory` is where the assets are copied by the build
/// (`Rogue3.fsproj` copies `assets/audio/*.wav` beside the assembly), so probing it second makes the
/// product audible wherever it is launched from, while leaving a working-directory override — a
/// mod folder, a test fixture — winning when one is present. A launch directory that happens to
/// contain `assets/audio/<id>.wav` therefore shadows the shipped asset; that is the point of the
/// ordering, and it is written down here because nothing else marks it.
///
/// Evaluated per call rather than captured in a module-level binding: a module `let` would freeze
/// the working directory at whatever it was when this module was first touched, an initialization
/// order a caller cannot see and a test cannot control. Resolution happens once per id per process
/// anyway — the OpenAL backend memoizes uploaded buffers behind `BufferCache` — so there is no cost
/// to reading it honestly.
let private assetSearchRoots () =
    [ Directory.GetCurrentDirectory(); System.AppContext.BaseDirectory ]

/// `resolver` is public and takes an id from its caller, so the id reaches `Path.Combine`. A rooted
/// or `..`-bearing id would then escape `assetRoot` entirely and read an arbitrary file. Nothing in
/// this product can produce such an id — every one comes from `AudioCueIds` and the M12 guard pins
/// them to a lowercase slug — but "no current caller does that" is not a property of a public
/// function, so the containment is checked here rather than assumed.
let private isContainedId (name: string) =
    not (String.IsNullOrWhiteSpace name)
    && name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0
    && name <> "."
    && name <> ".."

let private tryReadAsset (name: string) =
    if not (isContainedId name) then
        None
    else
        let fileName = name + ".wav"
        assetSearchRoots ()
        |> List.tryPick (fun root ->
            let path = Path.Combine(root, assetRoot, fileName)
            if File.Exists path then Some(File.ReadAllBytes path) else None)

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
let resolver: AssetResolver =
    { ResolveSound = fun (SoundId id) -> tryReadAsset id
      ResolveTrack = fun (TrackId id) -> tryReadAsset id }

// ─────────────────────────────────────────────────────────────────────────────────────────────
// `Started`, and the trap it exists to close (issue #458)
//
// `forTransition` is a function of a TRANSITION. The initial model does not make one: it is produced
// by `initialModel`, not dispatched into. So without `Started`, ANY sound the initial state implies
// is never requested — and that is a hole in this pattern, not a bug in a function.
//
// It bites the moment you *load* state instead of *transitioning into* it:
//
//     Load the player's saved settings in `initialModel`, fold them into the model, and the model
//     is CORRECT. The settings ARE loaded. And the mixer is never told, because no transition ever
//     carried them to it. Nothing catches this — no type is wrong, no requirement unsatisfied, and
//     a test that asserts on the model passes, because a restored volume the mixer never heard is
//     indistinguishable, from inside the model, from one that was restored properly.
//
//     It surfaces later as: turn the music down, restart, and get full-volume music from a settings
//     screen that correctly reports it as quiet.
//
// The same applies to a save game, restored window geometry, a resumed session, a replayed
// checkpoint — anything that enters the model through a door a transition-shaped seam is not
// watching. `Started` is that door, and the host dispatches it as `forTransition Started m m`.
//
// So: **put anything the initial state implies under `Started`**, e.g.
//
//     | Started -> [ Audio.setMasterVolume next.Settings.Volume
//                    Audio.playMusic (TrackId "theme") true ]
//
// Assert it at the SINK, not at the model — the only test that catches this class asks *what the
// engine was told*, not *what the model holds*.
//
// Both starters carry `Started` for this reason. It was the game's alone until #436 gave the
// Controls starter a cue seam; a seam without `Started` would have reproduced #458 one profile over.
// ─────────────────────────────────────────────────────────────────────────────────────────────

// M12 (DEC-001): every id below is a value from `Rogue3.AudioCueIds`, never a literal. That is what
// lets the generator and the M12 guard enumerate exactly the set this file requests instead of
// re-typing it — and `M12AudioAssetTests` scans this file (comments stripped) for stray
// `SoundId "…"`, `TrackId "…"` and `sfx "…"` literals, so an inline id added here reds the suite
// rather than shipping as a silent cue. It rejects the INTERPOLATED form too — `TrackId $"floor-…"`
// — because a value that is not known until it runs cannot be checked against the declaration at
// all, and that is exactly the shape the floor themes used to have.
let private sfx id volume = Audio.playSfx (SoundId id) volume

let private forAudioEvent = function
    | AudioEvent.ShotFired -> sfx AudioCueIds.shotFire 0.55
    | AudioEvent.ShotHit -> sfx AudioCueIds.shotHit 0.7
    | AudioEvent.EnemyDied -> sfx AudioCueIds.enemyDeath 0.75
    | AudioEvent.PlayerHit -> sfx AudioCueIds.playerHit 0.9
    | AudioEvent.PlayerDied -> sfx AudioCueIds.playerDeath 1.0
    | AudioEvent.DodgeRolled -> sfx AudioCueIds.dodgeRoll 0.6
    | AudioEvent.BombExploded -> sfx AudioCueIds.bombExplosion 0.95
    | AudioEvent.ItemGranted -> sfx AudioCueIds.itemPickup 0.85

type private MusicContext =
    | Title
    | Floor of int
    | Shop
    | Boss
    | GameOver
    | Victory

let private roomType model =
    model.Floor.Rooms
    |> Map.tryFind model.Floor.CurrentRoom
    |> Option.map _.RoomType

let private musicContext model =
    match model.RunOutcome with
    | Some RunOutcome.GameOver -> GameOver
    | Some RunOutcome.Victory -> Victory
    | None when not model.RunActive -> Title
    | None ->
        match roomType model with
        | Some FloorGeneration.Shop -> Shop
        | Some FloorGeneration.Boss -> Boss
        | _ -> Floor model.FloorIndex

// M12 (DEC-002): the floor family goes through `AudioCueIds.floorTheme`, which CLAMPS into the
// declared range, rather than interpolating an unbounded index. This line used to read
// `$"floor-{max 1 index}-theme"`, which meant "six floors" was a fact about `Model.fs` that this
// file merely believed — a seventh floor would have requested a track nothing declares and played
// as silence. Clamping makes the family total, so no floor index can name an undeclared track.
let private track = function
    | Title -> TrackId AudioCueIds.titleTheme
    | Floor index -> TrackId(AudioCueIds.floorTheme index)
    | Shop -> TrackId AudioCueIds.shopTheme
    | Boss -> TrackId AudioCueIds.bossTheme
    | GameOver -> TrackId AudioCueIds.gameOverTheme
    | Victory -> TrackId AudioCueIds.victoryTheme

let private replaceMusic context =
    [ Audio.stopMusic; Audio.playMusic (track context) true ]

/// Shell transitions are outside the play-model Msg stream, but they replace the same single loop.
let replaceWithTitleMusic () = replaceMusic Title
let replaceWithCurrentMusic model = replaceMusic (musicContext model)

let private effectiveVolume model =
    if model.Profile.Settings.Muted then 0.0
    else Audio.clampVolume model.Profile.Settings.MasterVolume

let private musicForTransition msg previous next =
    let before = musicContext previous
    let after =
        match msg with
        | CompleteRunStats(won, _) -> if won then Victory else GameOver
        | _ -> musicContext next
    match msg with
    | Started -> [ Audio.playMusic (track Title) true ]
    | _ when before <> after -> replaceMusic after
    | _ -> []

let private cueForPickup = function
    | Rogue3.Entities.PickupKind.Coin1
    | Rogue3.Entities.PickupKind.Coin3 -> Some(sfx AudioCueIds.pickupCoin 0.7)
    | Rogue3.Entities.PickupKind.Key -> Some(sfx AudioCueIds.pickupKey 0.7)
    | Rogue3.Entities.PickupKind.Bomb -> Some(sfx AudioCueIds.pickupBomb 0.7)
    | Rogue3.Entities.PickupKind.HalfRedHeart
    | Rogue3.Entities.PickupKind.SoulHeart -> Some(sfx AudioCueIds.pickupHeart 0.7)
    | Rogue3.Entities.PickupKind.Nothing -> None

// A shop interaction is an explicit acquisition event. Do not infer pickups from arbitrary model
// increases: run resets and the difficulty-owned post-boss heal also increase those fields.
let private m5ShopPickupCues msg previous next =
    match msg with
    | InteractM5Shop slotId ->
        match previous.M5ShopSlots |> List.tryFind (fun slot -> slot.Id = slotId),
              next.M5ShopSlots |> List.tryFind (fun slot -> slot.Id = slotId) with
        | Some before, Some after when before.Offer <> after.Offer ->
            match before.Offer with
            | Rogue3.Entities.ShopOffer.Item _ -> [ sfx AudioCueIds.itemPickup 0.85 ]
            | Rogue3.Entities.ShopOffer.Consumable kind -> cueForPickup kind |> Option.toList
            | Rogue3.Entities.ShopOffer.Empty -> []
        | _ -> []
    | _ -> []

let private doorAndBossCues previous next =
    let locked = function
        | Rogue3.Entities.DoorState.LockedClear
        | Rogue3.Entities.DoorState.BossSealed -> true
        | Rogue3.Entities.DoorState.Open -> false
    let beforeLocked = previous.M5Room.Doors |> List.exists locked
    let afterLocked = next.M5Room.Doors |> List.exists locked
    [ if not beforeLocked && afterLocked then yield sfx AudioCueIds.doorLock 0.7
      if beforeLocked && not afterLocked then yield sfx AudioCueIds.doorUnlock 0.7
      // M11: derived from the TRANSITION, not from a message. A player descends by pressing interact
      // on a trapdoor, which resolves inside a fixed step, so `msg` is `Tick` and never `DescendFloor`.
      // A refused descent moves no floor index and is therefore silent for free.
      if next.FloorIndex > previous.FloorIndex then yield sfx AudioCueIds.floorDescend 0.8
      if previous.M5Boss.IsNone && next.M5Boss.IsSome then yield sfx AudioCueIds.bossIntro 1.0
      match previous.M5Boss, next.M5Boss with
      | Some before, Some after when after.Phase > before.Phase -> yield sfx AudioCueIds.bossPhase 0.9
      | Some _, None -> yield sfx AudioCueIds.bossDeath 1.0
      | _ -> () ]

let private directEventCues msg previous next =
    [ match msg with
      | DamageM5Enemy(enemyId, _) when next.M5Enemies <> previous.M5Enemies ->
          yield sfx AudioCueIds.shotHit 0.7
          if previous.M5Enemies |> List.exists (fun enemy -> enemy.Id = enemyId)
             && next.M5Enemies |> List.forall (fun enemy -> enemy.Id <> enemyId) then
              yield sfx AudioCueIds.enemyDeath 0.75
      | DamageM5Boss _ when next.M5Boss <> previous.M5Boss -> yield sfx AudioCueIds.shotHit 0.7
      | RecordCoinsCollected count when count > 0 -> yield sfx AudioCueIds.pickupCoin 0.7
      // Board item #47 removed the `RecordItemFound` cue here. It was keyed to a message with zero
      // production dispatch sites — the same defect the note below describes for `floor-descend`, so
      // `item-pickup` was audible only to a test that dispatched it. The pedestal and boss reward
      // resolve inside a `Tick`, and are now cued STATE-DERIVED through `AudioEvent.ItemGranted`;
      // the shop keeps its message-keyed cue in `m5ShopPickupCues` because a purchase IS a message.
      // NOTE: `floor-descend` is NOT cued here. It is STATE-DERIVED in `doorAndBossCues`, because
      // M11's production route reaches a descent from inside a `Tick` — the audio seam never sees a
      // `DescendFloor` message on a real descent, so a message-keyed cue would be audible only to a
      // test that dispatched it. That is the exact defect this milestone exists to close.
      | _ -> () ]

/// What this rogue3 asks to hear when `msg` takes it from `previous` to `next`.
/// Return `[]` for a silent transition. Effects play in list order.
///
/// Drop a WAV at `assets/audio/<id>.wav` and you hear it; leave it out and the request is recorded
/// but silent. Add your own cases — this is your file.
let forTransition (msg: Msg) (previous: Model) (next: Model) : AudioEffect list =
    let volume =
        match msg with
        | Started
        | SetMasterVolume _
        | SetMuted _ -> [ Audio.setMasterVolume (effectiveVolume next) ]
        | _ -> []
    let fixedStepEvents =
        match msg with
        | Tick _ -> next.AudioEvents |> List.map forAudioEvent
        | _ -> []
    volume
    @ fixedStepEvents
    @ directEventCues msg previous next
    @ m5ShopPickupCues msg previous next
    @ doorAndBossCues previous next
    @ musicForTransition msg previous next
