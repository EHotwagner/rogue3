/// M12 (013-m12-audio-assets): the gate that makes a silent cue impossible to ship green.
///
/// WHAT WENT WRONG BEFORE. M8 shipped `shipReady` with 12/12 obligations while `assets/audio/` did
/// not exist in the tree at all, so every cue in the game was silent. Nothing was wrong with the M8
/// tests: they assert what the product *requests*, which was correct then and is correct now. What
/// no obligation ever did was cross from "requested" to "resolves", and that gap is what a player
/// heard as nothing.
///
/// WHY THIS FILE IS SHAPED THE WAY IT IS. Two shortcuts would each recreate the defect one layer
/// down, so neither is taken:
///
///   * A hardcoded list of ids in this file would drift from `AudioCues.fs` and then pass while a
///     newly added cue stayed silent. The obligation is enumerated from `Rogue3.AudioCueIds`, the
///     same declaration `AudioCues.fs` builds every `SoundId`/`TrackId` from — and `requested cue
///     ids resolve` below closes the loop the other way, collecting the ids the production host
///     ACTUALLY emits over a real run and requiring each of those to resolve too.
///   * Checking `File.Exists` under `assets/audio/` would pass against the repository tree while the
///     shipped binary could not find its own assets, and would accept a zero-byte or non-PCM file.
///     Resolution goes through `Rogue3.AudioCues.resolver` — the exact value `Program.fs` hands to
///     `OpenAlBackend.create` — and the bytes go through `FS.GG.Audio.Host.Wav.tryParse`, the same
///     structural parse the device backend applies before upload.
module M12AudioAssetTests

open System
open System.IO
open System.Text.RegularExpressions
open Expecto
open FS.GG.Audio.Core
open FS.GG.Audio.Host
open FS.GG.UI.SkiaViewer
open Rogue3
open Rogue3.Model

// ─────────────────────────────────────────────────────────────────────────────────────────────
// The resolution predicate — one definition, applied to declared ids and to the negative control
// alike, so the gate is shown to discriminate rather than assumed to.
// ─────────────────────────────────────────────────────────────────────────────────────────────

/// `Ok ()` when `bytes` is something the shipped device backend could actually upload and play:
/// present, parseable as RIFF/WAVE, PCM, mono, 16-bit, non-empty. `Error reason` otherwise, in the
/// same vocabulary `FS.GG.Audio.Host.AssetDiagnostics.Failure` uses.
let private playable (bytes: byte[] option) =
    match bytes with
    | None -> Error "unresolved"
    | Some raw ->
        match Wav.tryParse raw with
        | None -> Error(sprintf "not-wav (%d bytes)" raw.Length)
        | Some pcm when pcm.FormatTag <> Wav.FormatPcm -> Error(sprintf "unsupported-codec (formatTag %d)" pcm.FormatTag)
        | Some pcm when pcm.Channels <> 1 -> Error(sprintf "unsupported-format (%d channels; OpenAL spatializes mono only)" pcm.Channels)
        | Some pcm when pcm.BitsPerSample <> 16 -> Error(sprintf "unsupported-format (%d bits per sample)" pcm.BitsPerSample)
        | Some pcm when pcm.Data.Length = 0 -> Error "empty data chunk"
        | Some _ -> Ok()

let private resolveSound id = AudioCues.resolver.ResolveSound(SoundId id)
let private resolveTrack id = AudioCues.resolver.ResolveTrack(TrackId id)

/// Every declared id paired with how it resolves, sounds through `ResolveSound` and tracks through
/// `ResolveTrack` — the two halves are separate functions on `AssetResolver` and a guard that only
/// exercised one of them would miss a whole class of failure.
let private declaredResolutions () =
    [ for id in AudioCueIds.sounds -> "sound", id, playable (resolveSound id)
      for id in AudioCueIds.tracks -> "track", id, playable (resolveTrack id) ]

let private failureReport results =
    results
    |> List.choose (fun (kind, id, result) ->
        match result with
        | Ok() -> None
        | Error reason -> Some(sprintf "%s '%s': %s" kind id reason))

// ─────────────────────────────────────────────────────────────────────────────────────────────
// Locating the authored source, for the drift scan
// ─────────────────────────────────────────────────────────────────────────────────────────────

/// Walk up from the test assembly to the directory that holds `src/Rogue3/AudioCues.fs`. Returning
/// `None` is a FAILURE below, never a skip: a scan that silently found no file would pass over zero
/// literals, which is exactly the vacuous green this file exists to prevent.
let private repositoryRoot =
    let rec walk (directory: DirectoryInfo) =
        if isNull (box directory) then None
        elif File.Exists(Path.Combine(directory.FullName, "src", "Rogue3", "AudioCues.fs")) then Some directory.FullName
        else walk directory.Parent
    walk (DirectoryInfo AppContext.BaseDirectory)

/// Drop everything from the first `//` that is not inside a string literal.
///
/// This is not tidiness. `AudioCues.fs` carries two cue id literals inside its own documentation —
/// `SoundId "save"` and `TrackId "theme"` — that the product never requests. A scan that did not
/// strip comments would report them as undeclared drift and red the suite for prose.
let private stripLineComment (line: string) =
    let mutable inString = false
    let mutable index = 0
    let mutable cut = -1
    while cut < 0 && index < line.Length do
        let current = line.[index]
        if inString && current = '\\' then index <- index + 1
        elif current = '"' then inString <- not inString
        elif not inString && current = '/' && index + 1 < line.Length && line.[index + 1] = '/' then cut <- index
        index <- index + 1
    if cut >= 0 then line.Substring(0, cut) else line

let private cueIdLiteral = Regex("(?:SoundId|TrackId|sfx)\\s+\"([^\"]*)\"", RegexOptions.Compiled)

let private cueIdLiteralsIn (source: string seq) =
    source
    |> Seq.map stripLineComment
    |> Seq.collect (fun line -> cueIdLiteral.Matches line |> Seq.map (fun m -> m.Groups.[1].Value))
    |> Set.ofSeq

// ─────────────────────────────────────────────────────────────────────────────────────────────
// The ids the PRODUCTION host actually emits, collected from a real run
// ─────────────────────────────────────────────────────────────────────────────────────────────

let private requestedIdsFrom effects =
    effects
    |> List.collect (function
        | ViewerEffect.PlayAudio batch -> (Audio.interpret batch).Requested
        | _ -> [])
    |> List.choose (function
        | PlaySfx(SoundId id, _) -> Some id
        | PlaySfx3D(SoundId id, _, _, _, _) -> Some id
        | PlayMusic(TrackId id, _) -> Some id
        | _ -> None)

/// Drive the production hosts through a run that touches every cue family the game has — boot,
/// start, a floor, a shop room, a boss room, damage, a phase change, a boss death, a guarded
/// descent, a shop purchase, and both run endings — and collect every id that crossed the
/// `PlayAudio` sink. These are the ids a player's session actually asks for.
let private idsRequestedByARealRun () =
    let host = Program.generatedHost
    let shell = Program.interactiveHost
    let collected = ResizeArray<string>()
    let drive model msg =
        let next, effects = host.Update msg model
        collected.AddRange(requestedIdsFrom effects)
        next

    let shell0, shellInit = shell.Init()
    collected.AddRange(requestedIdsFrom shellInit)

    let started = drive Program.initialModel (StartRun 4242UL)
    let roomOfType kind =
        started.Floor.Rooms
        |> Map.toSeq
        |> Seq.tryFind (fun (_, room) -> room.RoomType = kind)
        |> Option.map fst

    let shopped =
        match roomOfType FloorGeneration.Shop with
        | Some shopId ->
            let entered = drive started (EnterM5Room shopId)
            match entered.M5ShopSlots with
            | slot :: _ -> drive { entered with PlayerCurrency = { entered.PlayerCurrency with Coins = 99; Keys = 9 } } (InteractM5Shop slot.Id)
            | [] -> entered
        | None -> started

    let boss =
        match roomOfType FloorGeneration.Boss with
        | Some bossId -> drive shopped (EnterM5Room bossId)
        | None -> shopped

    let damaged = drive boss (DamageM5Boss 150.0)
    let phased = drive damaged (Tick fixedDt)
    let cleared = drive phased (DamageM5Boss 10000.0)
    let descended = drive { cleared with PlayerPosition = trapdoorCenter } DescendFloor

    // A genuine fixed-step fire: holding the aim command makes the step raise `AudioEvent.ShotFired`
    // itself, so `shot-fire` arrives from the production route rather than from a staged field.
    let firing =
        { descended with
            Input = { descended.Input with Current = { descended.Input.Current with Commands = Set [ "aim-right" ] } } }
    drive firing (Tick fixedDt) |> ignore

    // The remaining six `AudioEvent` cues need six different fixed-step situations to arise
    // naturally, which `M8AudioTests` already stages one at a time. Here the whole family goes
    // through the SAME production cue map — `AudioCues.forTransition` is the function the hosts above
    // call — so this scenario's claim (nothing the product can request is undeclared or unresolvable)
    // covers the fixed-step family too, rather than covering only the two events a short run reaches.
    let allEvents =
        { descended with
            AudioEvents =
                [ AudioEvent.ShotFired; AudioEvent.ShotHit; AudioEvent.EnemyDied
                  AudioEvent.PlayerHit; AudioEvent.PlayerDied; AudioEvent.DodgeRolled
                  AudioEvent.BombExploded ] }
    collected.AddRange(
        AudioCues.forTransition (Tick fixedDt) descended allEvents
        |> Audio.interpret
        |> _.Requested
        |> List.choose (function
            | PlaySfx(SoundId id, _) -> Some id
            | PlayMusic(TrackId id, _) -> Some id
            | _ -> None))

    drive { descended with RunActive = true } (CompleteRunStats(false, None)) |> ignore
    drive { descended with RunActive = true } (CompleteRunStats(true, None)) |> ignore

    // The shell route restores the title loop, which the play-model route never requests.
    let shellRun, shellStart = shell.Update (EvidenceCommands.StartFreshRun 4242UL) shell0
    collected.AddRange(requestedIdsFrom shellStart)
    let _, abandon = shell.Update EvidenceCommands.AbandonRun shellRun
    collected.AddRange(requestedIdsFrom abandon)

    List.ofSeq collected

[<Tests>]
let audioAssetTests =
    testList "M12 audio assets" [
        test "every declared cue id resolves through the production resolver to playable mono 16-bit PCM" {
            let results = declaredResolutions ()
            let failures = failureReport results
            Expect.equal
                failures
                []
                (sprintf
                    "every id Rogue3.AudioCueIds declares must resolve through Rogue3.AudioCues.resolver — the same resolver Program.fs hands to OpenAlBackend.create. %d of %d failed; regenerate with `dotnet fsi scripts/generate-audio-assets.fsx`"
                    failures.Length
                    results.Length)
            Expect.equal results.Length (AudioCueIds.sounds.Length + AudioCueIds.tracks.Length) "the guard covers the whole declaration"
            Expect.isGreaterThan results.Length 20 "the declaration is the real cue set, not a stub the guard shrank to"
        }

        test "the gate discriminates: an id with no asset fails the same predicate the declared ids pass" {
            // Without this, "every declared id resolves" could be satisfied by a predicate that
            // accepts anything — including the state M12 exists to close, where nothing resolved at
            // all. The negative control runs through the same resolver and the same predicate.
            let unknown = "m12-id-that-has-no-asset"
            Expect.isFalse (List.contains unknown AudioCueIds.all) "the negative control is genuinely undeclared"
            Expect.equal (playable (resolveSound unknown)) (Error "unresolved") "an undeclared sound id resolves to nothing and fails the gate"
            Expect.equal (playable (resolveTrack unknown)) (Error "unresolved") "an undeclared track id resolves to nothing and fails the gate"

            // The predicate rejects a present-but-unplayable asset too, which is what a naive
            // File.Exists gate would wave through.
            Expect.equal (playable (Some [||])) (Error "not-wav (0 bytes)") "a zero-byte asset is not playable"
            Expect.equal (playable (Some(Text.Encoding.ASCII.GetBytes "not a wav file at all"))) (Error "not-wav (21 bytes)") "arbitrary bytes are not playable"
        }

        test "every cue id the production host requests during a real run is declared and resolves" {
            // The other direction of the same obligation. `declaredResolutions` proves the
            // declaration is backed by assets; this proves the running product asks for nothing
            // outside the declaration — so a cue wired up with an id that was never declared cannot
            // hide behind a green declaration.
            let requested = idsRequestedByARealRun () |> List.distinct |> List.sort
            Expect.isGreaterThan requested.Length 12 $"a run that boots, starts, shops, fights a boss, descends and ends should request many cues; got {requested}"
            let undeclared = requested |> List.filter (fun id -> not (List.contains id AudioCueIds.all))
            Expect.equal undeclared [] "every id the production PlayAudio sink carried is declared in Rogue3.AudioCueIds"
            let unresolvable =
                requested
                |> List.filter (fun id ->
                    match playable (resolveSound id), playable (resolveTrack id) with
                    | Ok(), _
                    | _, Ok() -> false
                    | _ -> true)
            Expect.equal unresolvable [] "every id the production PlayAudio sink carried resolves to a playable asset"
        }

        test "no cue id literal escapes the declaration in AudioCues.fs" {
            // Prove the scanner works before trusting what it reports. A regex that silently matched
            // nothing would make the scan below pass over an empty set forever.
            let fixture =
                [ "let a = sfx \"live-literal\" 0.5"
                  "// let b = sfx \"commented-literal\" 0.5"
                  "let c = SoundId \"another-live\" // TrackId \"trailing-comment\""
                  "//     AudioCues.forTransition Save before after = [ Audio.playSfx (SoundId \"save\") 0.7 ]" ]
            Expect.equal
                (cueIdLiteralsIn fixture)
                (Set.ofList [ "live-literal"; "another-live" ])
                "the scanner finds live literals and ignores commented ones, including trailing comments"

            let root =
                match repositoryRoot with
                | Some root -> root
                | None -> failtestf "could not locate src/Rogue3/AudioCues.fs above %s — a drift scan that cannot find its source must fail, not pass vacuously" AppContext.BaseDirectory

            let path = Path.Combine(root, "src", "Rogue3", "AudioCues.fs")
            let literals = cueIdLiteralsIn (File.ReadLines path)
            let undeclared = literals - Set.ofList AudioCueIds.all
            Expect.isEmpty
                undeclared
                (sprintf
                    "AudioCues.fs must build every SoundId/TrackId from Rogue3.AudioCueIds; these inline literals are not declared and would ship as silent cues: %A"
                    (Set.toList undeclared))
            Expect.stringContains (File.ReadAllText path) "AudioCueIds." "AudioCues.fs still consumes the declaration rather than having been rewritten off it"
        }

        test "the committed assets are exactly what the compiled synthesis produces" {
            // Committed binaries a reviewer cannot check are opaque. This makes them reviewable by
            // re-derivation: read src/Rogue3/AudioSynthesis.fs, then trust that every byte on disk
            // came from it. It also means a hand-edited or truncated asset reds the suite.
            let mismatches =
                AudioCueIds.all
                |> List.choose (fun id ->
                    let expected = AudioSynthesis.render id
                    let actual =
                        match resolveSound id with
                        | Some bytes -> Some bytes
                        | None -> resolveTrack id
                    match actual with
                    | None -> Some(sprintf "%s: no asset resolved" id)
                    | Some bytes when bytes <> expected ->
                        Some(sprintf "%s: %d bytes on disk, %d bytes from AudioSynthesis.render" id bytes.Length expected.Length)
                    | Some _ -> None)
            Expect.equal
                mismatches
                []
                "every committed asset must equal AudioSynthesis.render of its id — re-run `dotnet fsi scripts/generate-audio-assets.fsx`"

            Expect.throws
                (fun () -> AudioSynthesis.render "m12-id-that-has-no-asset" |> ignore)
                "synthesis of an undeclared id raises rather than returning an empty buffer a naive existence gate would accept"
        }

        test "the declaration is well formed" {
            let all = AudioCueIds.all
            Expect.equal (List.distinct all).Length all.Length $"cue ids must be distinct across sounds and tracks; got {all}"
            Expect.isEmpty
                (List.filter (fun (id: string) -> not (Regex.IsMatch(id, "^[a-z0-9]+(-[a-z0-9]+)*$"))) all)
                "every cue id is a lowercase slug, so it is a safe file name on every platform"
            Expect.equal AudioCueIds.sounds.Length 18 "the sound set is the eighteen ids AudioCues.fs requests"
            Expect.equal AudioCueIds.tracks.Length (5 + AudioCueIds.floorThemeCount) "the track set is title, shop, boss, game-over, victory and one theme per floor"
        }

        test "the floor-theme family is total over every integer a floor index can take" {
            // `AudioCues.track` used to interpolate `floor-{max 1 index}-theme` from an unbounded int,
            // so a seventh floor would have requested an asset nothing declares. Clamping makes the
            // family total; this proves it over a range well past anything a run can reach.
            let outOfRange =
                [ -3 .. 12 ]
                |> List.map AudioCueIds.floorTheme
                |> List.filter (fun id -> not (List.contains id AudioCueIds.tracks))
            Expect.equal outOfRange [] "every integer floor index names a declared track"
            Expect.equal (AudioCueIds.floorTheme 1) "floor-1-theme" "floor one keeps its authored id"
            Expect.equal (AudioCueIds.floorTheme AudioCueIds.floorThemeCount) "floor-6-theme" "the deepest floor keeps its authored id"
            Expect.equal (AudioCueIds.floorTheme 0) (AudioCueIds.floorTheme 1) "an index below the range reuses the first floor's loop rather than falling silent"
            Expect.equal (AudioCueIds.floorTheme 99) (AudioCueIds.floorTheme AudioCueIds.floorThemeCount) "an index above the range reuses the deepest floor's loop rather than falling silent"
        }
    ]
