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
/// This is not tidiness. `AudioCues.fs` carries cue id literals inside its own documentation —
/// `SoundId "save"`, `TrackId "theme"`, and the M12 note that quotes the very forms this scanner
/// looks for — none of which the product requests. A scan that did not strip comments would report
/// them as drift and red the suite for prose.
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

/// Matches `SoundId "x"`, `TrackId "x"` and `sfx "x"` — and, critically, their INTERPOLATED forms
/// `SoundId $"x"` etc. The `$` was the hole: this file's own floor-theme family used to read
/// `TrackId $"floor-{max 1 index}-theme"`, and a scan that required a plain literal would have
/// reported clean over exactly the construction that produced the milestone's most-missed ids.
let private cueIdLiteral = Regex("(?:SoundId|TrackId|sfx)\\s+(\\$?)\"([^\"]*)\"", RegexOptions.Compiled)

/// A cue id literal found in source: the text, and whether it was an interpolated string.
type private FoundLiteral = { Text: string; Interpolated: bool }

let private cueIdLiteralsIn (source: string seq) =
    source
    |> Seq.map stripLineComment
    |> Seq.collect (fun line ->
        cueIdLiteral.Matches line
        |> Seq.map (fun m -> { Text = m.Groups.[2].Value; Interpolated = m.Groups.[1].Value = "$" }))
    |> List.ofSeq

/// The declared-set check, over plain literals only.
let private plainLiteralsIn source =
    cueIdLiteralsIn source
    |> List.filter (fun found -> not found.Interpolated)
    |> List.map _.Text
    |> Set.ofList

/// A cue id assembled at run time by `sprintf` or `String.Format` rather than named. Like the
/// interpolated form, its value is unknown until it runs, so it cannot be checked against the
/// declaration and is rejected outright.
let private computedCueId =
    Regex("(?:SoundId|TrackId|sfx)\\s*\\(?\\s*(?:sprintf|String\\.Format)", RegexOptions.Compiled)

/// Every product source file that could construct a cue id. Scanning `AudioCues.fs` alone was a
/// narrower guarantee than FR-005 needs: nothing stops a future cue literal appearing in
/// `GameShell.fs` or `EvidenceCommands.fs`, and a scan that never looks there would report clean.
let private productSourceFiles root =
    Directory.GetFiles(Path.Combine(root, "src", "Rogue3"), "*.fs") |> Array.sort

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

/// Drive the production hosts through a scripted sequence that reaches every cue family the game
/// has — boot, start, a boss room, damage, a phase change, a boss death, a guarded descent, a shop
/// purchase on floor two, a fixed-step shot, and both run endings — and collect every id that
/// crossed the `PlayAudio` sink.
///
/// HONEST ABOUT WHAT THIS IS. It is a scripted HOST-ROUTE sequence, not a player's journey: it
/// dispatches production `Msg` values through the production host and stages a few model fields
/// between them (shop currency, the player standing on the trapdoor). What it is *not* is a
/// re-implementation of the cue map — every id below comes back out of `AudioCues.forTransition` by
/// way of `ViewerEffect.PlayAudio`, which is the property this scenario needs. Proving these cues
/// from a real walked route belongs to the scripted play-through agent (roadmap M14).
///
/// THE SHOP MUST BE ENTERED ON FLOOR TWO. `FloorGeneration` only adds `RoomKind.Shop` when
/// `floorIndex >= 2`, so a shop leg on the starting floor is unreachable code that silently does
/// nothing — which is what this function did on its first draft, costing ten of the twenty-nine ids
/// their coverage while a `length > 12` assertion stayed green. `shopVisited` below is returned so
/// the caller can assert the leg actually fired.
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
    let roomOfType (model: Model) kind =
        model.Floor.Rooms
        |> Map.toSeq
        |> Seq.tryFind (fun (_, room) -> room.RoomType = kind)
        |> Option.map fst

    let boss =
        match roomOfType started FloorGeneration.Boss with
        | Some bossId -> drive started (EnterM5Room bossId)
        | None -> failtest "seed 4242 floor 1 has no boss room; the boss cue family would be silently uncovered"

    let damaged = drive boss (DamageM5Boss 150.0)
    let phased = drive damaged (Tick fixedDt)
    let cleared = drive phased (DamageM5Boss 10000.0)
    let descended = drive { cleared with PlayerPosition = trapdoorCenter } DescendFloor

    // Floor two is the first floor that generates a shop, so this is where the acquisition cues —
    // `pickup-coin`, `pickup-key`, `pickup-bomb`, `pickup-heart`, `item-pickup` — become reachable.
    // Every slot is bought so the offer mix cannot leave a cue family untouched by luck of the seed.
    let mutable shopVisited = false
    let shopped =
        match roomOfType descended FloorGeneration.Shop with
        | Some shopId ->
            let entered = drive descended (EnterM5Room shopId)
            let stocked =
                { entered with PlayerCurrency = { entered.PlayerCurrency with Coins = 999; Keys = 99 } }
            entered.ShopSlots
            |> List.fold
                (fun model (slot: Entities.ShopSlot) ->
                    shopVisited <- true
                    drive model (InteractM5Shop slot.Id))
                stocked
        | None -> descended

    // A genuine fixed-step fire: holding the aim command makes the step raise `AudioEvent.ShotFired`
    // itself, so `shot-fire` arrives from the production route rather than from a staged field.
    let firing =
        { shopped with
            Input = { shopped.Input with Current = { shopped.Input.Current with Commands = Set [ "aim-right" ] } } }
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
                  AudioEvent.BombExploded
                  // Board item #47 — see the note in M8AudioTests. This list is hand-maintained and
                  // unguarded; M14ItemGrantTests asserts the AudioEvent case count so that adding a
                  // case fails there and names this list.
                  AudioEvent.ItemGranted ] }
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

    List.ofSeq collected, shopVisited

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
            let collected, shopVisited = idsRequestedByARealRun ()
            let requested = collected |> List.distinct |> List.sort

            // A count threshold is not coverage. It cannot notice a leg of the sequence that quietly
            // does nothing — which is exactly how the shop leg spent its first draft unreachable
            // while `length > 12` stayed green over the nineteen ids the rest of the run happened to
            // reach. Name the families instead, so a leg that stops firing reds and says which.
            Expect.isTrue shopVisited "the shop leg actually purchased something; a shop room only generates from floor two, so an unreachable leg silently drops the whole acquisition family"
            let missing =
                [ AudioCueIds.titleTheme; AudioCueIds.floorTheme 1; AudioCueIds.floorTheme 2
                  AudioCueIds.shopTheme; AudioCueIds.bossTheme; AudioCueIds.gameOverTheme; AudioCueIds.victoryTheme
                  AudioCueIds.doorLock; AudioCueIds.doorUnlock; AudioCueIds.bossIntro; AudioCueIds.bossPhase
                  AudioCueIds.bossDeath; AudioCueIds.floorDescend
                  AudioCueIds.shotFire; AudioCueIds.shotHit; AudioCueIds.enemyDeath; AudioCueIds.playerHit
                  AudioCueIds.playerDeath; AudioCueIds.dodgeRoll; AudioCueIds.bombExplosion ]
                |> List.filter (fun id -> not (List.contains id requested))
            Expect.equal missing [] $"every cue family the sequence claims to reach must actually cross the PlayAudio sink; got {requested}"

            let acquisition =
                [ AudioCueIds.pickupCoin; AudioCueIds.pickupKey; AudioCueIds.pickupBomb
                  AudioCueIds.pickupHeart; AudioCueIds.itemPickup ]
            Expect.isNonEmpty
                (acquisition |> List.filter (fun id -> List.contains id requested))
                $"buying every slot in the floor-two shop must request at least one acquisition cue; got {requested}"

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

        test "no cue id is written into product source outside the declaration" {
            // Prove the scanner works before trusting what it reports. Over the shipped tree the
            // expected result is the EMPTY set, so a regex that silently matched nothing would make
            // the scan below pass forever.
            let fixture =
                [ "let a = sfx \"live-literal\" 0.5"
                  "// let b = sfx \"commented-literal\" 0.5"
                  "let c = SoundId \"another-live\" // TrackId \"trailing-comment\""
                  "//     AudioCues.forTransition SaveRequested before after = [ Audio.playSfx (SoundId \"save\") 0.7 ]"
                  "let d = TrackId $\"floor-{max 1 index}-theme\"" ]
            Expect.equal
                (plainLiteralsIn fixture)
                (Set.ofList [ "live-literal"; "another-live" ])
                "the scanner finds live literals and ignores commented ones, including trailing comments"
            Expect.equal
                (cueIdLiteralsIn fixture |> List.filter _.Interpolated |> List.map _.Text)
                [ "floor-{max 1 index}-theme" ]
                "the scanner separately reports an INTERPOLATED cue id, which is the form the floor themes used to take"
            Expect.isTrue
                (computedCueId.IsMatch "let e = TrackId (sprintf \"floor-%d-theme\" n)")
                "the scanner also recognises a cue id assembled by sprintf"

            let root =
                match repositoryRoot with
                | Some root -> root
                | None -> failtestf "could not locate src/Rogue3/AudioCues.fs above %s — a drift scan that cannot find its source must fail, not pass vacuously" AppContext.BaseDirectory

            let sources = productSourceFiles root
            Expect.isGreaterThan sources.Length 10 "the scan found the product source directory, not an empty one"
            Expect.contains (sources |> Array.map Path.GetFileName) "AudioCues.fs" "the scan covers the cue map"

            let scanned =
                sources
                |> Array.map (fun path -> path, File.ReadLines path |> Seq.map stripLineComment |> List.ofSeq)

            // FR-005 says NO cue id literal may appear in product source outside the declaration —
            // not merely no *undeclared* one. Comparing against `AudioCueIds.all` would have let
            // `sfx "shot-fire"` through, which is drift by any other name: the id would then live in
            // two places and could diverge. The expected set is empty.
            let literals =
                scanned
                |> Array.collect (fun (path, lines) ->
                    cueIdLiteralsIn lines |> List.map (fun found -> sprintf "%s: %s" (Path.GetFileName path) found.Text) |> Array.ofList)
            Expect.equal
                (List.ofArray literals)
                []
                "every SoundId/TrackId/sfx id must come from Rogue3.AudioCueIds; a literal here would live in two places and could diverge"

            // A value assembled at run time cannot be checked against the declaration at all. This is
            // the exact hole the first draft of this scan had: `TrackId $"floor-{max 1 index}-theme"`
            // is not a plain literal, so a `SoundId|TrackId|sfx\s+"…"` scan reported clean over the
            // one construction that produced six of the twenty-nine ids — and reverting the cue map to
            // it left the whole suite green until this arm existed.
            let computed =
                scanned
                |> Array.collect (fun (path, lines) ->
                    lines
                    |> List.filter computedCueId.IsMatch
                    |> List.map (fun line -> sprintf "%s: %s" (Path.GetFileName path) (line.Trim()))
                    |> Array.ofList)
            Expect.equal (List.ofArray computed) [] "a cue id assembled by sprintf or String.Format must go through Rogue3.AudioCueIds instead"

            let audioCuesPath = Path.Combine(root, "src", "Rogue3", "AudioCues.fs")
            Expect.stringContains (File.ReadAllText audioCuesPath) "AudioCueIds." "AudioCues.fs still consumes the declaration rather than having been rewritten off it"
        }

        test "the shipped cue map itself, not just the declaration, keeps every floor index in range" {
            // The declaration being total is worth nothing if `AudioCues` stops calling it. That is
            // not hypothetical: reverting the one line `AudioCues.track` uses back to its pre-M12
            // interpolated form left all of the other scenarios green, because the drift scan of the
            // day did not match `$"…"` and the scripted run only ever reaches floors one and two.
            // This drives the PUBLIC cue map — `replaceWithCurrentMusic` is the same function the
            // shell calls on a run transition — across every floor index and past the last floor.
            let trackIdsFor index =
                let model = { Program.initialModel with RunActive = true; FloorIndex = index }
                AudioCues.replaceWithCurrentMusic model
                |> Audio.interpret
                |> _.Requested
                |> List.choose (function
                    | PlayMusic(TrackId id, _) -> Some id
                    | _ -> None)

            let offRange =
                [ -2 .. 9 ]
                |> List.collect (fun index -> trackIdsFor index |> List.map (fun id -> index, id))
                |> List.filter (fun (_, id) -> not (List.contains id AudioCueIds.tracks))
            Expect.equal offRange [] "the shipped cue map must not request a track Rogue3.AudioCueIds does not declare, at any floor index"

            Expect.equal (trackIdsFor 1) [ AudioCueIds.floorTheme 1 ] "floor one requests its own theme through the cue map"
            Expect.equal (trackIdsFor 6) [ AudioCueIds.floorTheme 6 ] "the deepest floor requests its own theme through the cue map"
            Expect.equal (trackIdsFor 9) [ AudioCueIds.floorTheme 6 ] "an index past the last floor reuses the deepest floor's loop rather than naming an undeclared track"
        }

        test "assets/audio holds exactly the declared set and nothing else" {
            // AC-007 says the generator writes one file per declared id "and no others". Nothing
            // enforced the second half: an orphaned asset left behind by a renamed cue would sit in
            // the tree, ship in every build output, and never be noticed.
            let root =
                match repositoryRoot with
                | Some root -> root
                | None -> failtest "could not locate the repository root"

            let directory = Path.Combine(root, "assets", "audio")
            Expect.isTrue (Directory.Exists directory) $"the committed asset directory exists at {directory}"

            let onDisk = Directory.GetFiles(directory, "*.wav") |> Array.map Path.GetFileName |> Set.ofArray
            let declared = AudioCueIds.all |> List.map AudioSynthesis.fileName |> Set.ofList
            Expect.equal (Set.toList (onDisk - declared)) [] "no asset in assets/audio is undeclared; a renamed cue must not leave its old file behind"
            Expect.equal (Set.toList (declared - onDisk)) [] "every declared id has a committed asset"

            let others = Directory.GetFiles(directory) |> Array.map Path.GetFileName |> Array.filter (fun name -> not (name.EndsWith ".wav"))
            Expect.equal (List.ofArray others) [] "assets/audio holds audio and nothing else"
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
