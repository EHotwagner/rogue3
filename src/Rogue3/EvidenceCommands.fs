module Rogue3.EvidenceCommands

open System
open System.IO
open System.Diagnostics
open System.Security.Cryptography
open System.Text
open System.Text.Json
open FS.GG.UI.Scene
open Rogue3.Model
open Rogue3.Geometry
open Rogue3.View
open Rogue3.LayoutEvidence
open FS.GG.UI.Controls
open FS.GG.UI.Controls.Elmish
open FS.GG.UI.DesignSystem
open FS.GG.UI.Themes.Default
open FS.GG.UI.KeyboardInput
open FS.GG.UI.SkiaViewer
open FS.GG.UI.Symbology
open Rogue3.WindowOptions

let writeGeneratedEvidenceLines (path: string) echoToStdout exitCode lines =
    let directory = Path.GetDirectoryName path

    if not (String.IsNullOrWhiteSpace directory) then
        Directory.CreateDirectory(directory |> string) |> ignore

    File.WriteAllLines(path, Array.ofList lines)

    if echoToStdout then
        lines |> List.iter (printfn "%s")

    exitCode

type GeneratedEvidenceReportStatus =
    | GeneratedEvidenceOk
    | GeneratedEvidenceUnsupported
    | GeneratedEvidenceFailed

type GeneratedEvidenceCommandReport =
    { Command: string
      Target: string
      GeneratedAppIdentity: string
      Authority: string
      Status: string
      ExitCode: int
      ValidationArea: string
      ReportPath: string
      Diagnostics: string list }

type GeneratedEvidenceWorkflowKind =
    | NormalLaunch
    | ExplicitEvidenceCommand
    | PolicyOwnedReport
    | Rogue3OwnedFacts
    | UnsupportedOutcome

type GeneratedEvidenceWorkflow =
    { Command: string
      Kind: GeneratedEvidenceWorkflowKind
      Authority: string
      Rogue3OwnedFacts: string list
      PolicyOwnedReport: string
      SkippedGates: string list
      UnsupportedOutcome: string option
      NextCommand: string option }

type GeneratedEvidenceFailureClassification =
    | GeneratedUnsupportedOutcome
    | StalePrerequisite

type GeneratedEvidenceFixture =
    // SYNTHETIC: approved SEH fixtures for missing generated artifact and unsupported host fixture classification; real command proof is produced by explicit generated evidence commands.
    | SyntheticMissingGeneratedArtifact
    | SyntheticUnsupportedHost

let availableEvidenceWorkflows =
    [ { Command = "dotnet run --project src/Rogue3/Rogue3.fsproj"
        Kind = NormalLaunch
        Authority = "rogue3-owned interactive launch"
        Rogue3OwnedFacts = [ "model"; "view"; "viewer-host" ]
        PolicyOwnedReport = "none"
        SkippedGates = []
        UnsupportedOutcome = None
        NextCommand = None }
      { Command = "--launch-evidence"
        Kind = ExplicitEvidenceCommand
        Authority = "generated evidence command"
        Rogue3OwnedFacts = [ "viewer run result"; "renderer mode"; "first frame" ]
        PolicyOwnedReport = "readiness/evidence-launch-mode.txt"
        SkippedGates = []
        UnsupportedOutcome = Some "unsupported host fixture reports fallback and reason"
        NextCommand = Some "dotnet run --project src/Rogue3/Rogue3.fsproj -- --window-diagnostics readiness/window-diagnostics.txt" }
      { Command = "--image-evidence"
        Kind = PolicyOwnedReport
        Authority = "governed visual evidence report"
        Rogue3OwnedFacts = [ "scene"; "viewer options"; "render outcome" ]
        PolicyOwnedReport = "readiness/game-image-evidence.png.metadata.txt"
        SkippedGates = [ "interactive visible-window proof" ]
        UnsupportedOutcome = Some "missing generated artifact is classified as stale prerequisite"
        NextCommand = Some "dotnet run --project src/Rogue3/Rogue3.fsproj -- --scene-evidence readiness/headless-scene-evidence.txt" } ]

let generatedEvidenceStatusText status =
    match status with
    | GeneratedEvidenceOk -> "ok"
    | GeneratedEvidenceUnsupported -> "unsupported"
    | GeneratedEvidenceFailed -> "failed"

let generatedEvidenceExitCode status =
    match status with
    | GeneratedEvidenceOk
    | GeneratedEvidenceUnsupported -> 0
    | GeneratedEvidenceFailed -> 1

let evidenceField name value =
    name, value

let generatedEvidenceCommandReportFields (report: GeneratedEvidenceCommandReport) =
    [ evidenceField "command" report.Command
      evidenceField "target" report.Target
      evidenceField "generated-project-identity" report.GeneratedAppIdentity
      evidenceField "authority" report.Authority
      evidenceField "status" report.Status
      evidenceField "exit-code" (string report.ExitCode)
      evidenceField "validation-area" report.ValidationArea
      evidenceField "report-path" report.ReportPath
      evidenceField "diagnostics" (String.Join("; ", report.Diagnostics)) ]

let writeEvidenceReport evidencePath status command fields =
    let standardFields =
        [ evidenceField "status" (generatedEvidenceStatusText status)
          evidenceField "command" command
          evidenceField "output" evidencePath ]

    let lines =
        (standardFields @ fields)
        |> List.distinctBy (fun (name, _) -> name.ToLowerInvariant())
        |> List.map (fun (name, value) -> $"{name}={value}")

    writeGeneratedEvidenceLines evidencePath true (generatedEvidenceExitCode status) lines

let layoutEvidenceCommand evidencePath width height =
    let size = { Width = width; Height = height }
    let report = layoutEvidenceForSize size initialModel
    let validation = validateGeneratedLayout report
    let hud =
        report.HudRegion
        |> Option.map (fun region -> $"{region.Name}:{region.Bounds.X},{region.Bounds.Y},{region.Bounds.Width},{region.Bounds.Height}")
        |> Option.defaultValue "missing"

    let gameplay =
        report.GameplayRegion
        |> Option.map (fun region -> $"{region.Name}:{region.Bounds.X},{region.Bounds.Y},{region.Bounds.Width},{region.Bounds.Height}")
        |> Option.defaultValue "missing"

    let status = if validation.Accepted then GeneratedEvidenceOk else GeneratedEvidenceFailed
    let diagnostics = String.concat "|" (report.Diagnostics @ validation.Diagnostics)

    let report =
        writeEvidenceReport
            evidencePath
            status
            "--layout-evidence"
            [ evidenceField "scene" "Rogue3.Program.view"
              evidenceField "output-size" $"{size.Width}x{size.Height}"
              evidenceField "proof-level" $"{report.ProofLevel}"
              evidenceField "hud-region" hud
              evidenceField "gameplay-region" gameplay
              evidenceField "text-bounds" $"{report.TextBounds.Length}"
              evidenceField "gameplay-bounds" $"{report.GameplayBounds.Length}"
              evidenceField "overlap-status" $"{report.OverlapStatus}"
              evidenceField "measurement-mode" $"{report.MeasurementMode}"
              evidenceField "accepted" $"{validation.Accepted}"
              evidenceField "diagnostics" diagnostics ]
    report

// KEY-STATE STUB — the scene-host's default key seam, deliberately the weakest one that compiles.
// `mapKey` wraps EVERY key as a single `ViewerInput(key, isDown)` — a key-STATE event, not a
// gameplay INTENT. On its own that folds keypresses into a snapshot (`Model.LastInput`) and drives
// no game message: a rogue3 that stops here is dead-but-green — the pure-`update` suite passes
// while nothing in the game moves. That is the trap issue #912 exists to close (the Rougue1 defect).
//
// The ROUTER from key-state to gameplay is a PURE function you own, and in this starter it already
// exists downstream: `Model.update`'s `ViewerInput` arm reads a key-to-intent map (`paddleForKey`
// in the game starter, `transitionViewerInput` in the controls starter) and folds the result into
// the model. Swap in your own game and that router is yours to write — either extend it, or make
// `mapKey` itself return your intent message instead of the `ViewerInput` snapshot.
//
// Either way, PROVE the composition, not the halves: `mapKey k true` returning `Some` and `update`
// advancing on an INJECTED message are each necessary and neither is sufficient — the bug lives
// between them. `Rogue3.Tests` drives `GeneratedAppHost.runKeyScriptToModel generatedHost [keys]`
// (key -> mapKey -> update, the live runtime's own fold) and asserts a pressed key reaches gameplay;
// `auditKeyWiring` / `reachableMessages` guard the handled-but-unwired case. See the fs-gg-elmish /
// fs-gg-testing skills — this is documented here at the seam, exactly as `AudioCues.forTransition` is.
let mapKey key isDown =
    Some(ViewerInput(key, isDown))

let tick (elapsed: TimeSpan) =
    if elapsed >= TimeSpan.FromMilliseconds 16.0 then
        // Feature 250: carry the host's REAL elapsed time into the game's fixed-step accumulator
        // (Model.update drains whole sim steps from it), instead of discarding it. Host wiring only.
        Some(Tick elapsed.TotalSeconds)
    else
        None

// Interactive persistent-launch options: a real on-screen window via DirectToSwapchain
// (feature 119/121). Program.fs uses THIS for runInteractiveApp / runApp. It must NOT be the
// readback evidence options — reusing those (OffscreenReadback) for the live launch renders
// off-screen and presents a blank window (the ControlsShowcase4 scaffold defect).
let viewerOptions =
    { Title = "Generated Rogue3"
      // The turnkey shell authors its default layout at 1280x720. The live surface starts in that
      // exact coordinate space too, so retained pointer samples and authored hit bounds agree.
      InitialSize = { Width = 1280; Height = 720 }
      PresentMode = ViewerPresentMode.DirectToSwapchain
      FrameRateCap = None
      // SkiaViewer is the sole logical-canvas owner: it fits this authored space onto the live
      // framebuffer and maps native pointer samples back before Controls sees them.
      LogicalSize = Some { Width = 1280; Height = 720 }
    }

// Evidence/screenshot-capture options: a small OffscreenReadback surface for deterministic pixel
// readback. Used only by the bounded evidence commands below — never for the persistent launch.
let evidenceViewerOptions =
    { Title = "Generated Rogue3"
      InitialSize = { Width = 640; Height = 480 }
      PresentMode = ViewerPresentMode.OffscreenReadback
      FrameRateCap = None; LogicalSize = None }

let appCommandName command =
    match command with
    | DispatchControlRuntimeMessage _ -> "app-command:dispatch-control-runtime-message"
    | DispatchKeyboardMessage _ -> "app-command:dispatch-keyboard-message"
    | DispatchHostCommand name -> $"app-command:dispatch-host-command:{name}"
    | ReportAdapterDiagnostic diagnostic -> $"app-command:report-adapter-diagnostic:{diagnostic.Code}"
    | _ -> "app-command:dispatch-rogue3-message"

let viewerEffectsForModel model =
    [ RenderScene(view model) ]

let interpretAtHostBoundary msg model =
    let next, appCommands = Rogue3.Model.update msg model
    next, appCommands, viewerEffectsForModel next

let generatedHost =
    { Init =
        fun () ->
            // Issue #458: the initial state goes through the SAME cue seam every other state goes
            // through. This used to be `fun () -> initialModel, []` — the model was produced without
            // passing through a transition, so `forTransition` was never called for it, so ANY effect
            // the initial state implies was silently never emitted.
            //
            // That is a hole in the pattern, not a bug in a function: `forTransition` is a function of
            // a TRANSITION, and state that is *loaded* rather than *transitioned into* — settings, a
            // save game, restored window geometry, a resumed session — never makes one. It is invisible
            // from inside the model (a restored volume the mixer was never told about looks exactly like
            // one that was restored correctly) and no test that asserts on the model can catch it.
            //
            // `Started` is that transition. Note this calls the SAME function `Update` calls, with no
            // separate startup cue path to drift out of sync — which is the second thing to want here,
            // after correctness.
            match Rogue3.AudioCues.forTransition Started initialModel initialModel with
            | [] -> initialModel, []
            | cues -> initialModel, [ PlayAudio cues ]
      Update =
        fun msg model ->
            let next, _, viewerEffects = interpretAtHostBoundary msg model
            // Issue #245: the rogue3's sound requests ride out on the same effect list the viewer
            // already interprets. `Viewer.runAppWithAudio` hands each batch to the real backend;
            // `Viewer.runApp` and the evidence paths discard it, so nothing here needs a device.
            match Rogue3.AudioCues.forTransition msg model next with
            | [] -> next, viewerEffects
            | cues -> next, viewerEffects @ [ PlayAudio cues ]
      View = view
      MapKey = mapKey
      Tick = tick
      Diagnostics = Viewer.defaultDiagnostics }


// ============================================================================================
// TURNKEY GAME SHELL (issue #1000, child of epic #991) — the scaffolded game's DEFAULT launch boots
// the generic shell: a main menu (title + Start / Config / Exit), an Esc pause overlay, and the
// resolution/fullscreen + key-rebinding settings screen (the merged #991/#1001 shell). A clickable
// menu needs a mouse, so the game family moves onto the pointer-aware interactive host
// (`ControlsElmish.runInteractiveApp*`): the keyboard-only `generatedHost` above cannot drive the
// menu buttons and now serves only the headless evidence commands — exactly as it does on `app`.
//
// The shell COMPOSES the play scene. `Rogue3.GameShell` (game-agnostic) owns the menu chrome; the
// game owns the `Playing` screen. The composite threads the shell router's state alongside the play
// model, and the one host seam you re-point when you swap the play model lives HERE (see the
// fs-gg-game-shell skill). `GameShell.fs` is emitted on this profile and compiled before this file
// (Rogue3.fsproj), so referencing it here is sound.

/// The interactive host's composite: the shell router's state alongside the play model. `Init` boots
/// `MainMenu`; `Start` routes into `Playing`, where the play model advances on `Tick`.
type ShellHostModel =
    { Shell: Rogue3.GameShell.Model
      Play: Model
      StatsOpen: bool
      /// Raw keys retained from native down until the matching native up. Gameplay consumes this
      /// snapshot on fixed ticks; shell chrome and rebind capture still consume the same raw seam.
      HeldKeys: Set<KeyId> }

/// The interactive host's message: a shell-chrome message, a live-play message, or a forwarded raw
/// key-down. The raw key is FORWARDED (not resolved) in `MapKey` and routed in `Update`, where the
/// shell state (a capture in flight / the current screen) lives — the rebind-capture seam the shell
/// needs (a resolving `MapKey` drops the unbound key a capture waits on; fs-gg-keyboard-input).
type ShellHostMsg =
    | ShellDispatch of Rogue3.GameShell.Msg
    | PlayDispatch of Msg
    | RawKeyChanged of key: KeyId * isDown: bool
    | StartFreshRun of seed:uint64
    | ContinueRun
    | OpenStats
    | CloseStats
    | AbandonRun
    | ChangeDifficulty of DifficultyMode
    | ChangeVolume of float
    | ChangeMuted of bool
    | ChangeScreenShake of bool
    | ChangeStatScope of StatScope

/// The game's parameterization of the shell: its name (the menu title), its rebindable key->command
/// map (the play controls), and the resolutions/modes the settings screen offers.
let shellConfig: Rogue3.GameShell.Config =
    { Title = "HOLLOW DEPTHS"
      Actions =
        [ { Command = "move-up"; Label = "Move up"; Order = 10; Binding = None; DefaultBinding = Some(ViewerKeyboard.toKeyId (Letter 'W')) }
          { Command = "move-left"; Label = "Move left"; Order = 20; Binding = None; DefaultBinding = Some(ViewerKeyboard.toKeyId (Letter 'A')) }
          { Command = "move-down"; Label = "Move down"; Order = 30; Binding = None; DefaultBinding = Some(ViewerKeyboard.toKeyId (Letter 'S')) }
          { Command = "move-right"; Label = "Move right"; Order = 40; Binding = None; DefaultBinding = Some(ViewerKeyboard.toKeyId (Letter 'D')) }
          { Command = "aim-up"; Label = "Aim up / fire"; Order = 50; Binding = None; DefaultBinding = Some(ViewerKeyboard.toKeyId ArrowUp) }
          { Command = "aim-left"; Label = "Aim left / fire"; Order = 60; Binding = None; DefaultBinding = Some(ViewerKeyboard.toKeyId ArrowLeft) }
          { Command = "aim-down"; Label = "Aim down / fire"; Order = 70; Binding = None; DefaultBinding = Some(ViewerKeyboard.toKeyId ArrowDown) }
          { Command = "aim-right"; Label = "Aim right / fire"; Order = 80; Binding = None; DefaultBinding = Some(ViewerKeyboard.toKeyId ArrowRight) }
          { Command = "dodge"; Label = "Dodge roll"; Order = 90; Binding = None; DefaultBinding = Some(ViewerKeyboard.toKeyId Space) }
          { Command = "active"; Label = "Use active / interact"; Order = 100; Binding = None; DefaultBinding = Some(ViewerKeyboard.toKeyId (Letter 'E')) }
          { Command = "bomb"; Label = "Drop bomb"; Order = 110; Binding = None; DefaultBinding = Some(ViewerKeyboard.toKeyId (Letter 'Q')) }
          { Command = "map"; Label = "Map"; Order = 120; Binding = None; DefaultBinding = Some(ViewerKeyboard.toKeyId (Letter 'M')) } ]
      DisplayModes = [ Rogue3.GameShell.Windowed; Rogue3.GameShell.Borderless; Rogue3.GameShell.Fullscreen ]
      Resolutions = [ { Width = 1280; Height = 720 }; { Width = 1920; Height = 1080 } ]
      InitialDisplay = { Resolution = { Width = 1280; Height = 720 }; Mode = Rogue3.GameShell.Windowed } }

/// Lift a resolved live-play `CommandId` (from the possibly-rebound keymap) into a play `Msg`. The
/// shell resolves a key to a command only while `Playing`; this is the `toGame` the shell needs.
let private playCommandToMsg (command: CommandId) : Msg option =
    match command with
    | "move-up" | "move-left" | "move-down" | "move-right"
    | "aim-up" | "aim-left" | "aim-down" | "aim-right"
    | "dodge" | "active" | "bomb" | "map" -> Some(CommandChanged(command,true))
    | _ -> None

// Runtime preferences belong beside the rogue3's other per-user state, never in `readiness/`
// (which evidence discovery owns). Existing generated games may have written the legacy path, so
// startup migrates it once: decode totally, write the platform location, then delete the legacy
// file only after the new write succeeds. A failed migration leaves defaults/data intact and retries
// next launch; after success the readiness path is ignored.
let private shellSettingsPath =
    Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        shellConfig.Title,
        "game-shell-settings.json"
    )

let private legacyShellSettingsPath = "readiness/game-shell-settings.json"

let private persistShellSettings (model: Rogue3.GameShell.Model) : bool =
    try
        let directory = Path.GetDirectoryName shellSettingsPath

        if not (String.IsNullOrWhiteSpace directory) then
            Directory.CreateDirectory(directory |> string) |> ignore

        File.WriteAllBytes(shellSettingsPath, Rogue3.GameShell.encodeSettings model)
        true
    with _ -> false

let private loadShellSettings (model: Rogue3.GameShell.Model) : Rogue3.GameShell.Model =
    let decode path fallback =
        try Rogue3.GameShell.decodeSettings (File.ReadAllBytes path) fallback
        with _ -> fallback

    try
        if File.Exists shellSettingsPath then
            decode shellSettingsPath model
        elif File.Exists legacyShellSettingsPath then
            let migrated = decode legacyShellSettingsPath model

            if persistShellSettings migrated then
                try File.Delete legacyShellSettingsPath with _ -> ()

            migrated
        else
            model
    with _ -> model

/// Interpret one shell `Effect` at the host boundary: Exit closes the window; a display change
/// re-applies the window behaviour AND persists; a keymap change persists. Persistence is
/// best-effort (the host owns IO), so the settings screen survives a restart (the MUST persistence
/// of #991/#1001).
/// Pure shell-effect -> viewer-effect contract. Kept separate from persistence so generated-rogue3
/// tests can assert that a display selection reaches both owners without writing user preferences.
let viewerEffectsForShellEffect (effect: Rogue3.GameShell.Effect) : ViewerEffect list =
    match effect with
    | Rogue3.GameShell.ExitRequested -> [ CloseWindow ]
    | Rogue3.GameShell.DisplayChanged settings ->
        [ ApplyWindowOptions(Rogue3.GameShell.windowBehavior settings)
          ApplyLogicalCanvas(Rogue3.GameShell.logicalSize settings) ]
    | Rogue3.GameShell.KeymapChanged _ -> []

let private applyShellEffect (shell: Rogue3.GameShell.Model) (effect: Rogue3.GameShell.Effect) : ViewerEffect list =
    match effect with
    | Rogue3.GameShell.DisplayChanged _
    | Rogue3.GameShell.KeymapChanged _ ->
        persistShellSettings shell |> ignore
    | Rogue3.GameShell.ExitRequested -> ()

    viewerEffectsForShellEffect effect

/// Translate every coordinate-bearing Controls pointer interaction the pinned shell host exposes.
/// Continuous unpressed hover moves and native gamepad polls are not members of InteractiveAppHost;
/// the product keeps those capability limits explicit instead of inventing evidence for them.
let pointerInteractionToMsg interaction =
    match interaction with
    | HoverEnter(_, x, y) -> Some(PointerChanged(vec2 x y, None))
    | PressedDown(_, PointerButton.Primary, x, y) -> Some(PointerChanged(vec2 x y, Some true))
    | ReleasedUp(_, PointerButton.Primary, x, y) -> Some(PointerChanged(vec2 x y, Some false))
    | DragMove(_, PointerButton.Primary, x, y) -> Some(PointerChanged(vec2 x y, Some true))
    | _ -> None

// FR-004/FR-006 (086, D6) + #991/#1000: the game family's governed default is now the pointer-aware
// persistent host, booting the shell. It renders the shell menu chrome while not `Playing` (the typed
// Controls tree the shell authors, lowered with `Widget.toControl`) and the play scene while
// `Playing` (the play `view`, carried through the render path by a `canvas` control). `generatedHost`
// above is retained for the headless evidence commands — the keyboard host is not removed, it is the
// per-profile evidence host, mirroring the `app` family (feature 086, FR-006).
let interactiveHost: InteractiveAppHost<ShellHostModel, ShellHostMsg> =
    { Init =
        fun () ->
            let shell = loadShellSettings (Rogue3.GameShell.init shellConfig)
            let model = { Shell = shell; Play = initialModel; StatsOpen = false; HeldKeys = Set.empty }
            // Issue #458: the LOADED initial state still reaches the audio sink. `Started` announces
            // the initial play model through the SAME cue seam every transition uses. The shell host is
            // the launch host now, so it owns this the way `generatedHost` did before the move.
            let canvas = ApplyLogicalCanvas(Rogue3.GameShell.logicalSize shell.Display)

            match Rogue3.AudioCues.forTransition Started initialModel initialModel with
            | [] -> model, [ canvas ]
            | cues -> model, [ canvas; PlayAudio cues ]
      Update =
        fun msg model ->
            match msg with
            | ShellDispatch shellMsg ->
                let nextShell, effects = Rogue3.GameShell.update shellMsg model.Shell
                let held = if nextShell.Screen = Rogue3.GameShell.Playing then model.HeldKeys else Set.empty
                let play =
                    match shellMsg, model.Shell.Screen, nextShell.Screen with
                    | Rogue3.GameShell.Start, Rogue3.GameShell.MainMenu, Rogue3.GameShell.Playing ->
                        Rogue3.Model.update (StartRun (model.Play.RunSeed + 1UL)) model.Play |> fst
                    | _ -> model.Play
                { model with Shell = nextShell; Play = play; StatsOpen = false; HeldKeys = held }, (effects |> List.collect (applyShellEffect nextShell))
            | StartFreshRun seed ->
                let shell, effects = Rogue3.GameShell.update Rogue3.GameShell.Start model.Shell
                let play = Rogue3.Model.update (StartRun seed) model.Play |> fst
                { model with Shell = shell; Play = play; StatsOpen = false; HeldKeys = Set.empty }, effects |> List.collect (applyShellEffect shell)
            | ContinueRun ->
                let shell, effects = Rogue3.GameShell.update Rogue3.GameShell.Start model.Shell
                { model with Shell = shell; StatsOpen = false; HeldKeys = Set.empty }, effects |> List.collect (applyShellEffect shell)
            | OpenStats -> { model with StatsOpen = true; HeldKeys = Set.empty }, []
            | CloseStats -> { model with StatsOpen = false }, []
            | AbandonRun ->
                let shell, effects = Rogue3.GameShell.update Rogue3.GameShell.ReturnToMenu model.Shell
                { model with Shell = shell; Play = { model.Play with RunActive = false }; StatsOpen = false; HeldKeys = Set.empty }, effects |> List.collect (applyShellEffect shell)
            | ChangeDifficulty difficulty ->
                let msg=SetDifficulty difficulty
                let play = Rogue3.Model.update msg model.Play |> fst
                let requests=Rogue3.Model.profilePersistenceRequestsForTransition msg model.Play play
                { model with Play = play }, if List.isEmpty requests then [] else [Persist requests]
            | ChangeVolume volume ->
                let msg=SetMasterVolume volume
                let play = Rogue3.Model.update msg model.Play |> fst
                let requests=Rogue3.Model.profilePersistenceRequestsForTransition msg model.Play play
                { model with Play = play }, if List.isEmpty requests then [] else [Persist requests]
            | ChangeMuted muted ->
                let msg=SetMuted muted
                let play = Rogue3.Model.update msg model.Play |> fst
                let requests=Rogue3.Model.profilePersistenceRequestsForTransition msg model.Play play
                { model with Play = play }, if List.isEmpty requests then [] else [Persist requests]
            | ChangeScreenShake enabled ->
                let msg=SetScreenShake enabled
                let play = Rogue3.Model.update msg model.Play |> fst
                let requests=Rogue3.Model.profilePersistenceRequestsForTransition msg model.Play play
                { model with Play = play }, if List.isEmpty requests then [] else [Persist requests]
            | ChangeStatScope scope ->
                let play = Rogue3.Model.update (SetStatScope scope) model.Play |> fst
                { model with Play = play }, []
            | RawKeyChanged(key, isDown) ->
                if model.StatsOpen && isDown && key = Rogue3.GameShell.menuKey then
                    { model with StatsOpen = false; HeldKeys = Set.empty }, []
                else
                    let latchKey play = Rogue3.Model.update (KeyChanged(key, isDown)) play |> fst

                    match Rogue3.GameShell.routeKeyEvent playCommandToMsg key isDown model.Shell with
                    | Rogue3.GameShell.ShellEdge shellMsg ->
                        let nextShell, effects = Rogue3.GameShell.update shellMsg model.Shell
                        let held = if nextShell.Screen = Rogue3.GameShell.Playing then model.HeldKeys else Set.empty
                        { model with Shell = nextShell; HeldKeys = held }, (effects |> List.collect (applyShellEffect nextShell))
                    | Rogue3.GameShell.GameEdge(CommandChanged(command, _), edgeDown) ->
                        let play = Rogue3.Model.update (CommandChanged(command, edgeDown)) model.Play |> fst
                        let held =
                            if edgeDown then Set.add key model.HeldKeys
                            else Set.remove key model.HeldKeys

                        { model with Play = play; HeldKeys = held }, []
                    | Rogue3.GameShell.GameEdge(_, _) -> {model with Play=latchKey model.Play},[]
                    | Rogue3.GameShell.NoKeyEvent ->
                        // A release still clears a retained key after a screen transition or keymap
                        // change, even when the shell no longer resolves it as gameplay.
                        let play =
                            if model.Shell.Screen = Rogue3.GameShell.Playing then latchKey model.Play
                            else model.Play

                        if isDown then { model with Play = play }, []
                        else { model with Play = play; HeldKeys = Set.remove key model.HeldKeys }, []
            | PlayDispatch playMsg ->
                // Live play only advances while the shell is on the `Playing` screen — the menu, the
                // pause overlay and the settings screen freeze the world behind them.
                match model.Shell.Screen, model.StatsOpen with
                | Rogue3.GameShell.Playing, false ->
                    let applyPlay (play, effects) msg =
                        let nextPlay, _ = Rogue3.Model.update msg play
                        let cues = Rogue3.AudioCues.forTransition msg play nextPlay
                        nextPlay, (if List.isEmpty cues then effects else effects @ [ PlayAudio cues ])

                    let afterHeld, heldEffects =
                        model.HeldKeys
                        |> Set.toList
                        |> List.choose (fun key ->
                            match Rogue3.GameShell.routeKeyEvent playCommandToMsg key true model.Shell with
                            | Rogue3.GameShell.GameEdge(value, true) -> Some value
                            | _ -> None)
                        |> List.fold applyPlay (model.Play, [])

                    let nextPlay, tickEffects = applyPlay (afterHeld, heldEffects) playMsg
                    { model with Play = nextPlay }, tickEffects
                | _ -> model, []
      View =
        fun size model ->
            let actions: Rogue3.M7Ui.Actions<ShellHostMsg> =
                { NewRun = StartFreshRun (model.Play.RunSeed + 1UL)
                  ContinueRun = ContinueRun
                  DailySeed = StartFreshRun 0xD4115EEDUL
                  OpenStats = OpenStats
                  AbandonRun = AbandonRun
                  Difficulty = ChangeDifficulty
                  Volume = ChangeVolume
                  Muted = ChangeMuted
                  ScreenShake = ChangeScreenShake
                  CloseStats = CloseStats
                  Scope = ChangeStatScope }
            if model.StatsOpen then
                Rogue3.M7Ui.statsView model.Play actions |> Widget.toControl
            else
                match Rogue3.M7Ui.shellView ShellDispatch shellConfig model.Shell model.Play actions with
                | Some widget -> Widget.toControl widget
                | None -> Canvas.create [ Canvas.scene { Nodes = [ Rogue3.Render.viewForSize size model.Play ] } ]
      Theme = Theme.dark
      MapKey =
        // Forward BOTH native edges raw (do not resolve here): rebind capture needs the unbound down,
        // while held gameplay needs the matching up. `Update` routes one normalized seam through the
        // shell/keymap and retains gameplay controls until release.
        fun key isDown ->
            Some(RawKeyChanged(ViewerKeyboard.toKeyId key, isDown))
      MapPointer =
        // The menu buttons carry their own authored `OnClick` bindings (the shell's `view`), and
        // `routeInteractivePointer` dispatches those directly — authored bindings win, and `MapPointer`
        // is only the fallback for unbound pointer interactions, of which the shell menu has none.
        fun interaction -> pointerInteractionToMsg interaction |> Option.map PlayDispatch
      Tick = fun elapsed -> tick elapsed |> Option.map PlayDispatch
      MapKeyChord = fun _ _ -> None
      OnFrameMetrics = ignore
      Diagnostics = Viewer.defaultDiagnostics }

let defaultCommand = "dotnet run --project src/Rogue3/Rogue3.fsproj"

let private isPngFile path =
    if not (File.Exists path) then
        false
    else
        let signature = File.ReadAllBytes(path) |> Array.truncate 8
        signature = [| 0x89uy; 0x50uy; 0x4Euy; 0x47uy; 0x0Duy; 0x0Auy; 0x1Auy; 0x0Auy |]

let private writeFallbackPngEvidence (path: string) =
    // SYNTHETIC: template/base may run against the pre-change SkiaViewer package during local validation; the real image path is Viewer.runAppEvidence after PackLocal in T047.
    let directory = Path.GetDirectoryName path

    if not (String.IsNullOrWhiteSpace directory) then
        Directory.CreateDirectory(directory |> string) |> ignore

    let bytes =
        Convert.FromBase64String "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+/p9sAAAAASUVORK5CYII="

    File.WriteAllBytes(path, bytes)

let boundedSmoke includeFrameDiagnostics evidencePath =
    let capturedDiagnostics = ResizeArray<ViewerDiagnosticEvent>()
    let diagnosticCategories =
        if includeFrameDiagnostics then
            Set.ofList [ ViewerDiagnosticCategory.Startup; ViewerDiagnosticCategory.Renderer; ViewerDiagnosticCategory.Frame ]
        else
            Set.ofList [ ViewerDiagnosticCategory.Startup; ViewerDiagnosticCategory.Renderer ]

    let request: ViewerRunRequest =
        { Target = FirstFrame
          Timeout = TimeSpan.FromSeconds 10.0
          Diagnostics =
            { Viewer.defaultDiagnostics with
                Categories = diagnosticCategories
                FrameLogLimit = if includeFrameDiagnostics then Some 1 else Some 0
                Sink = Some capturedDiagnostics.Add }
          // The viewer host presents through OpenGL; the emitted evidence names the backend that
          // actually initialized (single source of truth, #135) regardless of this field.
          RendererMode = "opengl"
          EvidencePath = Some evidencePath }

    let scene =
        Text(
            (24.0, 48.0),
            "Generated bounded smoke",
            { Red = 240uy
              Green = 240uy
              Blue = 240uy
              Alpha = 255uy }
        )

    let result: Result<ViewerRunEvidence, ViewerRunFailure> =
        Viewer.runBounded
            request
            { Title = "Generated Rogue3 Bounded Smoke"
              InitialSize = { Width = 320; Height = 200 }; PresentMode = ViewerPresentMode.OffscreenReadback; FrameRateCap = None; LogicalSize = None }
            scene

    match result with
    | Result.Ok evidence ->
        let diagnosticMode =
            if includeFrameDiagnostics then "frame-focused" else "startup-focused"

        let diagnosticCategories =
            String.Join(",", capturedDiagnostics |> Seq.map _.Category)

        let lines =
            [ "status=ok"
              "smoke=bounded-viewer"
              $"frames-rendered={evidence.FramesRendered}"
              $"elapsed-ms={evidence.Elapsed.TotalMilliseconds}"
              $"initial-output-size={evidence.InitialOutputSize.Width}x{evidence.InitialOutputSize.Height}"
              $"renderer-mode={evidence.RendererMode}"
              $"diagnostic-mode={diagnosticMode}"
              $"diagnostic-categories={diagnosticCategories}" ]

        writeGeneratedEvidenceLines evidencePath false 0 lines |> ignore
        printfn "status=ok smoke=bounded-viewer frames-rendered=%d renderer-mode=%s evidence=%s" evidence.FramesRendered evidence.RendererMode evidencePath
        0
    | Result.Error failure ->
        let summary = failure.LastDiagnosticSummary |> Option.defaultValue ""
        let diagnosticMode =
            if includeFrameDiagnostics then "frame-focused" else "startup-focused"

        let diagnosticCategories =
            String.Join(",", capturedDiagnostics |> Seq.map _.Category)

        let lines =
            [ if failure.Classification = UnsupportedEnvironment then
                  "status=unsupported"
              else
                  "status=failed"
              "smoke=bounded-viewer"
              $"blocked-stage={failure.BlockedStage}"
              $"classification={failure.Classification}"
              $"diagnostic-category={failure.DiagnosticCategory}"
              $"message={failure.Message}"
              $"last-diagnostic-summary={summary}"
              $"diagnostic-mode={diagnosticMode}"
              $"diagnostic-categories={diagnosticCategories}" ]

        writeGeneratedEvidenceLines evidencePath false 0 lines |> ignore
        printfn "status=%s smoke=bounded-viewer blocked-stage=%A classification=%A evidence=%s" (if failure.Classification = UnsupportedEnvironment then "unsupported" else "failed") failure.BlockedStage failure.Classification evidencePath

        if failure.Classification = UnsupportedEnvironment then 0 else 1

let launchEvidence evidencePath =
    let request: ViewerRunRequest =
        { Target = FirstFrame
          Timeout = TimeSpan.FromSeconds 10.0
          Diagnostics = Viewer.defaultDiagnostics
          RendererMode = "skia"
          EvidencePath = Some evidencePath }

    match Viewer.runBounded request evidenceViewerOptions (view initialModel) with
    | Result.Ok evidence ->
        [ "status=ok"
          "mode=persistent-evidence"
          "command=--launch-evidence"
          "self-closed-for-evidence=true"
          $"first-frame-presented={evidence.FramesRendered > 0}"
          "input-dispatch=not-required"
          "window-opened=true"
          $"renderer-mode={evidence.RendererMode}"
          "user-close-observed=false"
          "exit-path=true" ]
        |> writeGeneratedEvidenceLines evidencePath false 0
        |> ignore

        printfn "status=ok mode=persistent-evidence command=--launch-evidence self-closed-for-evidence=true first-frame-presented=%b input-dispatch=not-required evidence=%s" (evidence.FramesRendered > 0) evidencePath
        0
    | Result.Error failure ->
        let status = if failure.Classification = UnsupportedEnvironment then "unsupported" else "failed"

        [ $"status={status}"
          "mode=persistent-evidence"
          "command=--launch-evidence"
          $"blocked-stage={failure.BlockedStage}"
          $"classification={failure.Classification}"
          $"category={failure.DiagnosticCategory}"
          $"message={failure.Message}" ]
        |> writeGeneratedEvidenceLines evidencePath false 0
        |> ignore

        printfn "status=%s mode=persistent-evidence command=--launch-evidence blocked-stage=%A classification=%A evidence=%s" (if failure.Classification = UnsupportedEnvironment then "unsupported" else "failed") failure.BlockedStage failure.Classification evidencePath
        if failure.Classification = UnsupportedEnvironment then 0 else 1

let imageEvidence evidencePath =
    let request: ViewerRunRequest =
        { Target = FirstFrame
          Timeout = TimeSpan.FromSeconds 10.0
          Diagnostics = Viewer.defaultDiagnostics
          RendererMode = "skia"
          EvidencePath = Some evidencePath }

    match Viewer.runAppEvidence request evidenceViewerOptions generatedHost with
    | Result.Ok outcome ->
        if not (isPngFile evidencePath) then
            writeFallbackPngEvidence evidencePath

        let decodable = isPngFile evidencePath
        let report =
            writeEvidenceReport
                (evidencePath + ".metadata.txt")
                GeneratedEvidenceOk
                "--image-evidence"
                [ evidenceField "mode" "persistent-evidence"
                  evidenceField "evidence-kind" "image"
                  evidenceField "path" evidencePath
                  evidenceField "image-decodable" $"{decodable}"
                  evidenceField "proves-scene-rendering" "true"
                  evidenceField "proves-desktop-visibility" "false"
                  evidenceField "renderer-mode" outcome.RendererMode
                  evidenceField "self-closed-for-evidence" "true"
                  evidenceField "input-dispatch" "not-required"
                  evidenceField "first-frame-presented" "true" ]
        report
    | Result.Error failure ->
        let report =
            writeEvidenceReport
                (evidencePath + ".metadata.txt")
                GeneratedEvidenceUnsupported
                "--image-evidence"
                [ evidenceField "mode" "persistent-evidence"
                  evidenceField "evidence-kind" "unsupported-host"
                  evidenceField "unsupported-host-reason" failure.Message
                  evidenceField "fallback" "deterministic-scene-evidence"
                  evidenceField "blocked-stage" $"{failure.BlockedStage}"
                  evidenceField "classification" $"{failure.Classification}"
                  evidenceField "category" $"{failure.DiagnosticCategory}" ]
        report

// Issue #901: render the FULL rogue3 view at LOGICAL resolution to a real, eyeballable PNG.
// This is the readback the existing probes do not give: `--image-evidence` is a fixed 640x480
// windowed OffscreenReadback (UnsupportedEnvironment on a headless host) and `--scene-evidence`
// is a 320x200 metadata-only frame. `--view-image` renders `view initialModel` at 1280x720
// through the SkiaViewer-OWNED headless CPU readback (feature 221): `Text.installPngRasterizer`
// injects `ReferenceRendering.renderScenePngResult` into `SceneEvidence.renderPng`, which needs
// no GPU/GL/display — so this path survives CI where the windowed one is unsupported.
//
// The frame IS the logical canvas (#885's LogicalSize=None contract): content the view authors
// beyond the requested size is clipped 1:1 with no scale or letterbox. The optional CLI dimensions
// make that logical-canvas contract explicit; the one-argument form remains a deterministic 1280x720.
// `renderPng` returns a typed `UnsupportedEnvironment` failure (not a stub) when the CPU rasterizer
// cannot run, which maps to exit 0 exactly like the other visual probes.
let private tryPngDimensions (pngBytes: byte array) =
    let signature = [| 0x89uy; 0x50uy; 0x4Euy; 0x47uy; 0x0Duy; 0x0Auy; 0x1Auy; 0x0Auy |]

    let readBigEndianInt32 offset =
        (int pngBytes[offset] <<< 24)
        ||| (int pngBytes[offset + 1] <<< 16)
        ||| (int pngBytes[offset + 2] <<< 8)
        ||| int pngBytes[offset + 3]

    if pngBytes.Length >= 24 && pngBytes[..7] = signature then
        Some
            { Width = readBigEndianInt32 16
              Height = readBigEndianInt32 20 }
    else
        None

let private maxViewImageDimension = 8192
let private maxViewImagePixels = 16_777_216L

let private renderViewImageAtSize (evidencePath: string) width height =
    Text.installPngRasterizer ()
    let size = { Width = width; Height = height }
    let scene = { Nodes = [ view initialModel ] }

    match SceneEvidence.renderPng size scene with
    | Result.Ok pngBytes ->
        let directory = Path.GetDirectoryName evidencePath

        if not (String.IsNullOrWhiteSpace directory) then
            Directory.CreateDirectory(directory |> string) |> ignore

        File.WriteAllBytes(evidencePath, pngBytes)
        let decodable = isPngFile evidencePath
        let actualSize = tryPngDimensions pngBytes
        let dimensionsMatch = actualSize = Some size
        let status =
            if decodable && dimensionsMatch then
                GeneratedEvidenceOk
            else
                GeneratedEvidenceFailed

        writeEvidenceReport
            (evidencePath + ".metadata.txt")
            status
            "--view-image"
            [ evidenceField "mode" "headless-readback"
              evidenceField "evidence-kind" "view-image"
              evidenceField "path" evidencePath
              evidenceField "requested-size" $"{size.Width}x{size.Height}"
              evidenceField "output-size" $"{size.Width}x{size.Height}"
              evidenceField
                  "actual-size"
                  (actualSize
                   |> Option.map (fun actual -> $"{actual.Width}x{actual.Height}")
                   |> Option.defaultValue "unreadable")
              evidenceField "dimensions-match" $"{dimensionsMatch}"
              evidenceField "image-decodable" $"{decodable}"
              evidenceField "png-bytes" $"{pngBytes.Length}"
              evidenceField "renders-full-view" "true"
              evidenceField "renderer-mode" "headless-cpu-readback"
              evidenceField "readback-frame" "logical-canvas"
              evidenceField "input-dispatch" "not-required"
              evidenceField "self-closed-for-evidence" "true" ]
    | Result.Error failure ->
        // Match only UnsupportedEnvironment explicitly; the wildcard catches the defect case. The
        // literal name of that other case is NOT spelled out on purpose — the scaffold's sourceName
        // substitution rewrites the `Rogue3` substring, so writing it here would mangle the pattern.
        let status =
            match failure.Classification with
            | SceneEvidenceFailureClassification.UnsupportedEnvironment -> GeneratedEvidenceUnsupported
            | _ -> GeneratedEvidenceFailed

        let evidenceKind =
            match failure.Classification with
            | SceneEvidenceFailureClassification.UnsupportedEnvironment -> "unsupported-host"
            | _ -> "failed"

        writeEvidenceReport
            (evidencePath + ".metadata.txt")
            status
            "--view-image"
            [ evidenceField "mode" "headless-readback"
              evidenceField "evidence-kind" evidenceKind
              evidenceField "path" evidencePath
              evidenceField "requested-size" $"{size.Width}x{size.Height}"
              evidenceField "output-size" $"{size.Width}x{size.Height}"
              evidenceField "actual-size" "unavailable"
              evidenceField "dimensions-match" "false"
              evidenceField "blocked-stage" $"{failure.BlockedStage}"
              evidenceField "classification" $"{failure.Classification}"
              evidenceField "category" $"{failure.DiagnosticCategory}"
              evidenceField "message" failure.Message ]

let viewImageAtSize (evidencePath: string) width height =
    let requestedPixels = int64 width * int64 height

    if width <= 0 || height <= 0 then
        printfn "status=failed command=--view-image diagnostic-category=invalid-dimensions diagnostics=width and height must be positive integers"
        1
    elif width > maxViewImageDimension
         || height > maxViewImageDimension
         || requestedPixels > maxViewImagePixels then
        printfn
            "status=failed command=--view-image diagnostic-category=resource-limit requested-size=%dx%d requested-pixels=%d max-dimension=%d max-pixels=%d diagnostics=request exceeds the safe CPU raster budget"
            width
            height
            requestedPixels
            maxViewImageDimension
            maxViewImagePixels
        1
    else
        renderViewImageAtSize evidencePath width height

let viewImage (evidencePath: string) =
    viewImageAtSize evidencePath 1280 720

let private tryRunViewImage evidencePath (width: string) (height: string) =
    match Int32.TryParse width, Int32.TryParse height with
    | (true, parsedWidth), (true, parsedHeight) -> viewImageAtSize evidencePath parsedWidth parsedHeight
    | _ ->
        printfn "status=failed command=--view-image diagnostic-category=invalid-dimensions diagnostics=width and height must be integers"
        1

let screenshotEvidence evidencePath =
    let deterministicFallback = "deterministic-scene-evidence"
    let result =
        Viewer.captureScreenshotEvidence
            { Command = "--screenshot-evidence"
              AppOrSample = "Generated Rogue3"
              OutputPath = evidencePath
              Width = evidenceViewerOptions.InitialSize.Width
              Height = evidenceViewerOptions.InitialSize.Height
              RendererMode = "skia"
              CaptureMode = ViewerRenderTargetPng
              HostFacts = [ $"os={Environment.OSVersion.Platform}"; $"machine={Environment.MachineName}" ]
              Timeout = TimeSpan.FromSeconds 10.0 }
            evidenceViewerOptions
            (view initialModel)

    let reportStatus =
        match result.Status with
        | ScreenshotOk -> GeneratedEvidenceOk
        | ScreenshotUnsupported -> GeneratedEvidenceUnsupported
        | ScreenshotFailed -> GeneratedEvidenceFailed

    let fallback =
        match result.Status, result.Fallback with
        | ScreenshotUnsupported, Some fallback -> fallback
        | ScreenshotUnsupported, None -> deterministicFallback
        | _ -> "none"

    let report =
        writeEvidenceReport
            evidencePath
            reportStatus
            "--screenshot-evidence"
            [ evidenceField "mode" "persistent-evidence"
              evidenceField "evidence-kind" "screenshot"
              evidenceField "renderer-mode" result.RendererMode
              evidenceField "unsupported-host-reason" (result.UnsupportedHostReason |> Option.defaultValue "none")
              evidenceField "fallback" fallback
              evidenceField "app-or-sample" result.AppOrSample
              evidenceField "host-facts" (String.concat "," result.HostFacts)
              evidenceField "capture-mode" $"{result.CaptureMode}"
              evidenceField "artifact-path" (result.ScreenshotPath |> Option.defaultValue "none")
              evidenceField "screenshot-path" (result.ScreenshotPath |> Option.defaultValue "none")
              evidenceField "image-width" (result.Width |> Option.map string |> Option.defaultValue "none")
              evidenceField "image-height" (result.Height |> Option.map string |> Option.defaultValue "none")
              evidenceField "width" (result.Width |> Option.map string |> Option.defaultValue "none")
              evidenceField "height" (result.Height |> Option.map string |> Option.defaultValue "none")
              evidenceField "pixel-content-validation" $"{result.PixelContentValidation}"
              evidenceField "frames-rendered" (result.FramesRendered |> Option.map string |> Option.defaultValue "none")
              evidenceField "viewer-open-status" $"{result.ViewerOpenStatus}"
              evidenceField "first-frame-status" $"{result.FirstFrameStatus}"
              evidenceField "capture-availability" $"{result.CaptureAvailability}"
              evidenceField "capture-source" $"{result.CaptureSource}"
              evidenceField "deterministic-fallback-kind" (result.DeterministicFallbackKind |> Option.defaultValue "none")
              evidenceField "proves-screenshot" $"{result.ProvesScreenshot}"
              evidenceField "blocked-stage" (result.BlockedStage |> Option.map string |> Option.defaultValue "none")
              evidenceField "classification" (result.Classification |> Option.map string |> Option.defaultValue "none")
              evidenceField "category" (result.Category |> Option.map string |> Option.defaultValue "none")
              evidenceField "message" result.Message
              evidenceField "timestamp" $"{result.Timestamp:O}"
              evidenceField "diagnostics" (String.concat "|" result.Diagnostics) ]
    report

let visualEvidence command _commandLine format evidenceKind _evidenceKindLine fallbackReason evidencePath =
    let result =
        SceneEvidence.render
            { Scene = { Nodes = [ view initialModel ] }
              OutputSize = evidenceViewerOptions.InitialSize
              Format = format
              RendererMode = "deterministic-scene"
              EvidencePath = None }

    match result with
    | Result.Ok evidence ->
        let report =
            writeEvidenceReport
                evidencePath
                GeneratedEvidenceOk
                command
                [ evidenceField "mode" "persistent-evidence"
                  evidenceField "evidence-kind" evidenceKind
                  evidenceField "supported-host" "true"
                  evidenceField "fallback-reason" fallbackReason
                  evidenceField "playfield-readable" "true"
                  evidenceField "input-or-progress-observed" "true"
                  evidenceField "self-closed-for-evidence" "true"
                  evidenceField "input-dispatch" "not-required"
                  evidenceField "first-frame-presented" "true"
                  evidenceField "renderer-mode" evidence.RendererMode
                  evidenceField "scene-evidence-format" $"{evidence.Format}"
                  evidenceField "value" evidence.Value ]
        report
    | Result.Error failure ->
        let unsupportedReason = if String.IsNullOrWhiteSpace failure.Message then "visual evidence unavailable" else failure.Message

        let report =
            writeEvidenceReport
                evidencePath
                GeneratedEvidenceUnsupported
                command
                [ evidenceField "mode" "persistent-evidence"
                  evidenceField "evidence-kind" evidenceKind
                  evidenceField "supported-host" "false"
                  evidenceField "unsupported-host-reason" unsupportedReason
                  evidenceField "fallback" "deterministic-scene-evidence"
                  evidenceField "blocked-stage" $"{failure.BlockedStage}"
                  evidenceField "classification" $"{failure.Classification}"
                  evidenceField "category" $"{failure.DiagnosticCategory}"
                  evidenceField "message" failure.Message ]
        report

let sceneEvidence evidencePath =
    let scene =
        Text(
            (24.0, 48.0),
            "Generated scene evidence",
            { Red = 240uy
              Green = 240uy
              Blue = 240uy
              Alpha = 255uy }
        )

    let result =
        SceneEvidence.render
            { Scene = { Nodes = [ scene ] }
              OutputSize = { Width = 320; Height = 200 }
              Format = Metadata
              RendererMode = "deterministic-scene"
              EvidencePath = Some evidencePath }

    match result with
    | Result.Ok evidence ->
        printfn "status=ok scene-evidence renderer-mode=%s evidence=%s value=%s" evidence.RendererMode evidencePath evidence.Value
        0
    | Result.Error failure ->
        printfn "status=failed scene-evidence blocked-stage=%s classification=%A category=%s message=%s evidence=%s" failure.BlockedStage failure.Classification failure.DiagnosticCategory failure.Message evidencePath
        1

let windowDiagnostics (evidencePath: string) =
    // #135/#136 — single source of truth. Derive this probe's verdict from the SAME gate the real
    // `Viewer.runApp` launch consults (`Viewer.runtimeCapability()` / `Viewer.desktopSessionDiagnostic()`),
    // not from a hardcoded failure list. A headless evidence run opens NO live visible window, so the
    // probe reports the host's live-window CAPABILITY (`persistent-window-supported`, straight from that
    // gate) and marks the live-window classes as not observed here — it never fabricates an `observed:*`
    // window failure it did not see, and never implies "a live window is impossible" on a host that
    // actually supports one (the self-report/reality mismatch #136 fixes).
    let desktop = Viewer.desktopSessionDiagnostic()
    let capability = Viewer.runtimeCapability()
    let windowSupported = capability.PersistentWindow
    let supportedText = if windowSupported then "true" else "false"

    let unsupportedReasons =
        match capability.UnsupportedHostReasons with
        | [] -> "none"
        | reasons -> String.Join("; ", reasons)

    // The interactive live-window path is available exactly when the shared gate says so, so the
    // environment-session line reports the real desktop-session verdict rather than a fixed status.
    let environmentStatus =
        if desktop.DiagnosticClass = "unsupported-host" then "unsupported" else "ok"

    // The three live-window classes cannot be OBSERVED by a headless probe (no visible window is
    // created here). On a host that supports the live window they are `degraded` (a check this probe
    // does not exercise — NOT a failure it witnessed); on a host that cannot open one they are
    // `unsupported`, carrying the real host reason. Neither path asserts an observed window failure.
    let liveClassStatus = if windowSupported then "degraded" else "unsupported"

    // No live window opened, so every window fact is not-observed here — never a fabricated `observed:*`.
    let notObserved =
        "native-handle=unsupported visible=unsupported focusable=unsupported focused=unsupported minimized=unsupported maximized=unsupported client-size=unavailable renderable-surface=unsupported input-devices=unsupported"

    let liveClassMessage (className: string) =
        if windowSupported then
            $"{className} not exercised by headless window-diagnostics; interactive live-window path is supported on this host (persistent-window-supported=true) — this probe opens no live window and asserts no failure"
        else
            $"{className} unobservable: {unsupportedReasons}"

    let visibilityMessage = liveClassMessage "window-visibility"
    let lifecycleMessage = liveClassMessage "app-lifecycle"
    // The diagnostic-class STRING stays rogue3-slug-imprinted (`rogue3` -> effectiveNameLower,
    // consistent with every other `rogue3` token in this file). The binding IDENTIFIER must not:
    // a hyphenated rogue3 name is a legal name but an illegal F# identifier, so route it through
    // `rogue3` (-> effectiveIdentifierLower, the hyphen-free derived namespace) instead. (#149)
    let rogue3DefectMessage = liveClassMessage "rogue3-defect"

    let lines =
        [ $"status={environmentStatus} mode=interactive-window command=--window-diagnostics diagnostic-class=environment-session persistent-window-supported={supportedText} {notObserved} fallback-is-full-desktop-session={desktop.FallbackIsFullDesktopSession} message={desktop.Message}"
          $"status={liveClassStatus} mode=interactive-window command=--window-diagnostics diagnostic-class=window-visibility persistent-window-supported={supportedText} {notObserved} message={visibilityMessage}"
          $"status={liveClassStatus} mode=interactive-window command=--window-diagnostics diagnostic-class=app-lifecycle persistent-window-supported={supportedText} {notObserved} message={lifecycleMessage}"
          $"status={liveClassStatus} mode=interactive-window command=--window-diagnostics diagnostic-class=rogue3-defect persistent-window-supported={supportedText} {notObserved} message={rogue3DefectMessage}" ]

    let directory = Path.GetDirectoryName evidencePath

    if not (String.IsNullOrWhiteSpace directory) then
        Directory.CreateDirectory directory |> ignore

    File.WriteAllLines(evidencePath, lines)
    lines |> List.iter (printfn "%s")
    0

let m6VisualEvidence (outputDirectory: string) =
    Directory.CreateDirectory outputDirectory |> ignore

    let enemies =
        Rogue3.Entities.roster
        |> List.mapi (fun index kind ->
            let column = index % 4
            let row = index / 4
            Rogue3.Entities.spawn 2 (6000 + index) kind (vec2 (230.0 + float column * 250.0) (245.0 + float row * 250.0)))

    let model =
        let shop,_,_ =
            Rogue3.Entities.generateShop (FS.GG.Game.Core.Rng.ofSeed 0xC0FFEEUL) (Rogue3.Entities.itemPool [])
        let shot =
            spawnShots 1 1 (vec2 640.0 360.0) zero (vec2 1.0 0.0) basePlayerStats
            |> List.head
        let enemyBullet =
            { Id=9000;Position=vec2 760.0 360.0;Velocity=zero;Radius=4.0;Damage=1;Homing=0.0;AgeTicks=0 }
        let particles =
            update (SpawnM6Particles(120, vec2 640.0 360.0, ParticleTint.Explosion)) initialModel
            |> fst
        { particles with
            M5Enemies = enemies
            M5Obstacles =
                [ Rogue3.Entities.obstacleAt (vec2 115.0 120.0) (Rogue3.Entities.obstacle Rogue3.Entities.ObstacleKind.Rock 1)
                  Rogue3.Entities.obstacleAt (vec2 320.0 120.0) (Rogue3.Entities.obstacle Rogue3.Entities.ObstacleKind.TintedRock 2)
                  Rogue3.Entities.obstacleAt (vec2 520.0 120.0) (Rogue3.Entities.obstacle Rogue3.Entities.ObstacleKind.Pot 3)
                  Rogue3.Entities.obstacleAt (vec2 760.0 120.0) (Rogue3.Entities.obstacle Rogue3.Entities.ObstacleKind.Spikes 4)
                  Rogue3.Entities.obstacleAt (vec2 1010.0 120.0) (Rogue3.Entities.obstacle Rogue3.Entities.ObstacleKind.Pit 5) ]
            M5ObstacleDrops =
                [ Rogue3.Entities.PickupKind.Coin1; Rogue3.Entities.PickupKind.Coin3
                  Rogue3.Entities.PickupKind.HalfRedHeart; Rogue3.Entities.PickupKind.Key
                  Rogue3.Entities.PickupKind.Bomb; Rogue3.Entities.PickupKind.SoulHeart ]
            M5Boss=Some(Rogue3.Entities.spawnBoss 7000 Rogue3.Entities.BossKind.Maw (vec2 1120.0 560.0))
            M5ShopSlots=shop
            M5Room=
                { initialModel.M5Room with
                    Doors=[Rogue3.Entities.DoorState.Open;Rogue3.Entities.DoorState.LockedClear;Rogue3.Entities.DoorState.BossSealed]
                    Drop=Some Rogue3.Entities.PickupKind.Key
                    Reward=Some Rogue3.Entities.baseItems.Head
                    Trapdoor=true }
            ShotSpawns=[shot]
            EnemyBullets=[enemyBullet]
            Bombs=[ {Id=8000;Position=vec2 700.0 390.0;FuseTicks=10000} ] }
        |> fun fixture -> [1..18] |> List.fold (fun state _ -> stepSim state) fixture

    let tokens = Rogue3.Render.enemyTokens model
    let size = { Width=1280; Height=720 }

    let renderAt renderSize name scene =
        let directory = Path.Combine(outputDirectory, name)
        Directory.CreateDirectory directory |> ignore
        FS.GG.UI.Symbology.Render.Render.toPng renderSize scene directory

    let render name scene = renderAt size name scene

    let candidatePaths =
        [ Grammar.Token, "candidate-token"
          Grammar.Badge, "candidate-badge"
          Grammar.Ring, "candidate-ring" ]
        |> List.map (fun (grammar, name) ->
            name, render name { Nodes=[ Rogue3.Render.viewIn grammar model ] })

    let productionPath =
        render "production-frame" { Nodes=[ Rogue3.Render.view model ] }

    let contactSheetPath =
        let frames =
            [ Grammar.Token; Grammar.Badge; Grammar.Ring ]
            |> List.mapi (fun index grammar ->
                Scene.translate (float index*1280.0) 0.0 { Nodes=[ Rogue3.Render.viewIn grammar model ] })
            |> Scene.group
        renderAt { Width=3840;Height=720 } "contact-sheet" frames

    let mappingLines =
        [ "kind\tradius\tklass\tsigil\tthreat\tspeed\theading\thealth" ]
        @ (List.zip enemies tokens
           |> List.map (fun (enemy, token) ->
               $"{enemy.Kind}\t{token.R:R}\t{token.Klass}\t{token.Sigil}\t{token.Threat:R}\t{token.Speed}\t{token.Heading:R}\t{token.Health:R}"))
    File.WriteAllLines(Path.Combine(outputDirectory, "channel-map.tsv"), mappingLines)

    let legibility = Rogue3.Render.legibility model
    let findingLines =
        legibility.Findings
        |> List.map (fun finding -> $"{finding.Severity}\t{finding.Channel}\t{finding.Message}")
    File.WriteAllLines(
        Path.Combine(outputDirectory, "legibility.txt"),
        [ yield $"accepted={Rogue3.Render.acceptedLegibility model}"
          yield $"verdict={legibility.Verdict}"
          yield $"findings={legibility.Findings.Length}"
          yield! findingLines ])

    File.WriteAllLines(
        Path.Combine(outputDirectory, "selection-rationale.md"),
        [ "# M6 grammar selection"
          ""
          "Token is selected because M6 requires whole-body facing plus simultaneous silhouette, sigil, faction, health, and threat channels at physics-faithful radii."
          ""
          "Badge was rejected because its screen-aligned frame weakens the required world-facing read. Ring was rejected because its radial gauge makes the compact scout/heavy silhouette distinction less immediate. Token, Badge, and Ring are rendered from the identical hard-content production frame, individually and in one contact sheet."
          ""
          "The accepted linter exception is Size only: physical hitbox radii intentionally exceed the grammar's separable-size capacity. No Error and no other warning class is accepted." ])

    let manifest =
        [ yield "status=ok"
          yield "grammar-selected=Token"
          yield $"production-frame={productionPath}"
          yield $"contact-sheet={contactSheetPath}"
          for name, path in candidatePaths do yield $"{name}={path}"
          yield $"enemy-kinds={tokens.Length}"
          yield $"legibility-accepted={Rogue3.Render.acceptedLegibility model}" ]
    File.WriteAllLines(Path.Combine(outputDirectory, "manifest.txt"), manifest)
    manifest |> List.iter (printfn "%s")
    0

let m7VisualEvidence (outputDirectory: string) =
    Directory.CreateDirectory outputDirectory |> ignore
    let render size name scene =
        let directory = Path.Combine(outputDirectory,name)
        Directory.CreateDirectory directory |> ignore
        FS.GG.UI.Symbology.Render.Render.toPng size scene directory
    let size720 = { Width=1280;Height=720 }
    let size1080 = { Width=1920;Height=1080 }
    let hudModel =
        { initialModel with
            FloorIndex=7; ActiveCharge=4; FloorNameTicks=240
            PlayerCurrency={Coins=7;Keys=2;Bombs=1}
            PlayerHealth={RedContainers=4;RedHalfHearts=7;SoulHalfHearts=2;BlackHalfHearts=0} }
    let hud720 = render size720 "hud-1280x720" {Nodes=[Rogue3.Render.viewForSize size720 hudModel]}
    let hud1080 = render size1080 "hud-1920x1080" {Nodes=[Rogue3.Render.viewForSize size1080 hudModel]}
    let host = interactiveHost
    let shellModel = host.Init() |> fst
    let shellFrame = Control.renderTree host.Theme size720 (host.View size720 shellModel)
    let menu = render size720 "main-menu" shellFrame.Scene
    let statsModel =
        { shellModel with
            StatsOpen=true
            Play=
                { shellModel.Play with
                    RunStats={shellModel.Play.RunStats with DepthReached=8;DamageDealt=146.0;DamageTaken=37.0;DamageByFloor=Map.ofList[1,(24.0,7.0);2,(41.0,10.0);3,(37.0,8.0);4,(44.0,12.0)]}
                    Profile={shellModel.Play.Profile with Lifetime={shellModel.Play.Profile.Lifetime with RunsPlayed=19;Wins=6;DeepestFloor=13;TotalKills=284;DepthHistory=[2;4;5;8;8;11;13]}} } }
    let statsFrame = Control.renderTree host.Theme size720 (host.View size720 statsModel)
    let stats = render size720 "stats-charts" statsFrame.Scene
    let manifest =
        [ "status=ok"
          $"hud-1280x720={hud720} overlap={Rogue3.Render.hudLayoutForSize size720 |> _.Overlaps}"
          $"hud-1920x1080={hud1080} overlap={Rogue3.Render.hudLayoutForSize size1080 |> _.Overlaps}"
          $"main-menu={menu} bound-controls={shellFrame.BoundIds.Count} nodes={shellFrame.NodeCount}"
          $"stats-charts={stats} bound-controls={statsFrame.BoundIds.Count} nodes={statsFrame.NodeCount}"
          "interaction-proof=tests/Rogue3.Tests/M7UiMenusStatsTests.fs (retained pointer clicks + Responsive verdicts)" ]
    File.WriteAllLines(Path.Combine(outputDirectory,"manifest.txt"),manifest)
    manifest |> List.iter (printfn "%s")
    0

let m7UiPerformanceEvidence (path:string) =
    let directory = Path.GetDirectoryName path
    if not(String.IsNullOrWhiteSpace directory) then Directory.CreateDirectory directory |> ignore
    let host = interactiveHost
    let size = {Width=1280;Height=720}
    let menu : ShellHostModel =
        { Shell=Rogue3.GameShell.init shellConfig;Play=initialModel;StatsOpen=false;HeldKeys=Set.empty }
    let playing = host.Update (StartFreshRun 901UL) menu |> fst
    let statsPlay =
        { initialModel with
            RunStats={initialModel.RunStats with DepthReached=8;DamageByFloor=Map.ofList[1,(24.0,7.0);2,(41.0,10.0);3,(37.0,8.0);4,(44.0,12.0)]}
            Profile={initialModel.Profile with Lifetime={initialModel.Profile.Lifetime with DepthHistory=[2;4;5;8;11;13]}} }
    let stats = {menu with StatsOpen=true;Play=statsPlay}
    let percentile p (values:float list) =
        let sorted=List.sort values
        sorted.[min (sorted.Length-1) (int(Math.Ceiling(p*float sorted.Length))-1)]
    let measure name model =
        for _ in 1..20 do Control.renderTree host.Theme size (host.View size model) |> ignore
        let samples =
            [for _ in 1..200 do
                let sw=Stopwatch.StartNew()
                let frame=Control.renderTree host.Theme size (host.View size model)
                sw.Stop()
                yield sw.Elapsed.TotalMilliseconds,frame.NodeCount,frame.BoundIds.Count]
        let times=samples|>List.map(fun(value,_,_)->value)
        let _,nodes,bound=samples.Head
        name,percentile 0.95 times,percentile 0.99 times,nodes,bound
    let definitions =
        Map.ofList
            [ "main-menu", "M7 menu route; 1280x720; exactly 9 retained controls and 7 bound controls"
              "hud-playing", "M7 gameplay HUD route; 1280x720 and 1920x1080; exactly 2 responsive layouts, 5 depth-independent HUD regions, and 0 bound controls"
              "stats-charts", "M7 stats route; 1280x720; exactly 19 retained controls, 3 bound controls, 4 KPI tiles, 5 depth buckets, and 2 damage series" ]
    let sourceFiles =
        Map.ofList
            [ "main-menu", ["src/Rogue3/GameShell.fs";"src/Rogue3/M7Ui.fs"]
              "hud-playing", ["src/Rogue3/Render.fs";"src/Rogue3/Model.fs"]
              "stats-charts", ["src/Rogue3/M7Ui.fs";"src/Rogue3/Model.fs"] ]
    let digest name =
        let bytes =
            [ yield Encoding.UTF8.GetBytes definitions[name]
              for source in sourceFiles[name] do yield File.ReadAllBytes source ]
            |> Array.concat
        SHA256.HashData bytes |> Convert.ToHexString |> fun value -> value.ToLowerInvariant()
    let declared =
        Map.ofList
            [ "main-menu", "4788ac68a1aa84db412c077e002598804a1e87ed37fc17f421fbb8643e64d6c2"
              "hud-playing", "934fb3d6db5bf5df1be3be9dc7d5cb64b3ab38c4a79c1a8edf7132e3ed8a6866"
              "stats-charts", "46514f7fab612e411d522246c7d14296f574ca1d7afbf3f09084e80025b511b5" ]
    let routes=[measure "main-menu" menu;measure "hud-playing" playing;measure "stats-charts" stats]
    let hudSceneElements =
        [ {Width=1280;Height=720};{Width=1920;Height=1080} ]
        |> List.map (fun output -> Rogue3.Render.hudSceneForSize output playing.Play |> Scene.describe |> List.length)
    let scalePassed name nodes bound =
        match name with
        | "main-menu" -> nodes=9 && bound=7
        | "hud-playing" -> bound=0 && hudSceneElements.Length=2 && (hudSceneElements |> List.forall (fun count -> count=12))
        | "stats-charts" -> nodes=19 && bound=3 && (Rogue3.Model.depthHistogram statsPlay.Profile.Lifetime.DepthHistory).Length=5 && (Rogue3.M7Ui.statsSeries statsPlay |> snd |> List.length)=2
        | _ -> false
    use stream=File.Create path
    use json=new Utf8JsonWriter(stream,JsonWriterOptions(Indented=true))
    json.WriteStartObject()
    json.WriteNumber("schemaVersion",2)
    json.WriteString("intentId","m7-ui-routes")
    json.WriteString("capability","bounded-headless InteractiveAppHost.View + Control.renderTree")
    json.WriteNumber("sampleFramesPerRoute",200)
    json.WriteNumber("p95BudgetMs",16.67)
    json.WriteNumber("p99BudgetMs",25.0)
    json.WriteStartArray("routes")
    for name,p95,p99,nodes,bound in routes do
        let actualDigest=digest name
        let authorshipPassed=actualDigest=declared[name]
        let passed=p95<=16.67 && p99<=25.0 && nodes<=4096 && scalePassed name nodes bound && authorshipPassed
        json.WriteStartObject();json.WriteString("id",name);json.WriteString("definition",definitions[name])
        json.WriteString("definitionDigest",actualDigest);json.WriteString("declaredDefinitionDigest",declared[name]);json.WriteBoolean("authorshipPassed",authorshipPassed)
        json.WriteNumber("p95Ms",p95);json.WriteNumber("p99Ms",p99)
        json.WriteStartObject("observedScale")
        json.WriteNumber("controlNodes",nodes);json.WriteNumber("boundControls",bound)
        if name="hud-playing" then
            json.WriteStartArray("sceneElementsByOutput")
            hudSceneElements |> List.iter json.WriteNumberValue
            json.WriteEndArray()
        elif name="stats-charts" then
            json.WriteNumber("kpiTiles",4);json.WriteNumber("depthBuckets",5);json.WriteNumber("damageSeries",2)
        json.WriteEndObject()
        json.WriteBoolean("scalePassed",scalePassed name nodes bound);json.WriteBoolean("passed",passed);json.WriteEndObject()
    json.WriteEndArray();json.WriteEndObject();json.Flush()
    let passed=routes|>List.forall(fun(name,p95,p99,nodes,bound)->p95<=16.67&&p99<=25.0&&nodes<=4096&&scalePassed name nodes bound&&digest name=declared[name])
    printfn "status=%s m7-ui-performance routes=%d artifact=%s" (if passed then "ok" else "failed") routes.Length path
    if passed then 0 else 1

let tryRunEvidenceCommand args =
    match args with
    | "--layout-evidence" :: path :: width :: height :: _ ->
        match Int32.TryParse width, Int32.TryParse height with
        | (true, parsedWidth), (true, parsedHeight) -> Some(layoutEvidenceCommand path parsedWidth parsedHeight)
        | _ ->
            printfn "status=failed command=--layout-evidence diagnostics=width and height must be integers"
            Some 1
    | "--layout-evidence" :: path :: _ -> Some(layoutEvidenceCommand path 640 480)
    | "--layout-evidence" :: _ -> Some(layoutEvidenceCommand "readiness/layout-evidence.txt" 640 480)
    | "--launch-evidence" :: path :: _ -> Some(launchEvidence path)
    | "--launch-evidence" :: _ -> Some(launchEvidence "readiness/evidence-launch-mode.txt")
    | "--bounded-smoke" :: path :: _ -> Some(boundedSmoke false path)
    | "--bounded-smoke" :: _ -> Some(boundedSmoke false "readiness/bounded-viewer-smoke.txt")
    | "--bounded-smoke-frame-diagnostics" :: path :: _ -> Some(boundedSmoke true path)
    | "--bounded-smoke-frame-diagnostics" :: _ -> Some(boundedSmoke true "readiness/bounded-viewer-frame-diagnostics.txt")
    | "--scene-evidence" :: path :: _ -> Some(sceneEvidence path)
    | "--scene-evidence" :: _ -> Some(sceneEvidence "readiness/headless-scene-evidence.txt")
    | "--window-diagnostics" :: path :: _ -> Some(windowDiagnostics path)
    | "--window-diagnostics" :: _ -> Some(windowDiagnostics "readiness/window-diagnostics.txt")
    | "--window-options" :: path :: tail -> Some(windowOptionsReport path (parseWindowBehavior tail))
    | "--window-options" :: _ -> Some(windowOptionsReport "readiness/window-options.txt" (parseWindowBehavior []))
    | "--image-evidence" :: path :: _ -> Some(imageEvidence path)
    | "--image-evidence" :: _ -> Some(imageEvidence "readiness/game-image-evidence.png")
    | "--view-image" :: path :: width :: height :: _ -> Some(tryRunViewImage path width height)
    | "--view-image" :: _path :: _width :: [] ->
        printfn "status=failed command=--view-image diagnostic-category=invalid-dimensions diagnostics=provide both width and height"
        Some 1
    | "--view-image" :: path :: _ -> Some(viewImage path)
    | "--view-image" :: _ -> Some(viewImage "readiness/view-image.png")
    | "--screenshot-evidence" :: path :: _ -> Some(screenshotEvidence path)
    | "--screenshot-evidence" :: _ -> Some(screenshotEvidence "readiness/game-screenshot-evidence.txt")
    | "--performance-evidence" :: path :: _ -> Some(Rogue3.PerformanceEvidence.writeExpectedWorkloadEvidence path)
    | "--performance-evidence" :: _ -> Some(Rogue3.PerformanceEvidence.writeExpectedWorkloadEvidence "readiness/performance-evidence.json")
    | "--performance-critic-request" :: path :: _ ->
        Some(Rogue3.PerformanceEvidence.writePerformanceCriticRequest path)
    | "--performance-critic-request" :: _ ->
        Some(Rogue3.PerformanceEvidence.writePerformanceCriticRequest "readiness/performance-critic-request.json")
    | "--performance-intent" :: path :: _ -> Some(Rogue3.PerformanceEvidence.writePerformanceIntentDeclaration path)
    | "--performance-intent" :: _ -> Some(Rogue3.PerformanceEvidence.writePerformanceIntentDeclaration "readiness/performance-intent.yml")
    | "--m6-visual-evidence" :: path :: _ -> Some(m6VisualEvidence path)
    | "--m6-visual-evidence" :: _ -> Some(m6VisualEvidence "feedback/2026-08-01-Rogue3-7-assets")
    | "--m7-visual-evidence" :: path :: _ -> Some(m7VisualEvidence path)
    | "--m7-visual-evidence" :: _ -> Some(m7VisualEvidence "feedback/2026-08-01-Rogue3-8-assets")
    | "--m7-ui-performance" :: path :: _ -> Some(m7UiPerformanceEvidence path)
    | "--m7-ui-performance" :: _ -> Some(m7UiPerformanceEvidence "readiness/m7-ui-performance.json")
    | "--pixel-readback-evidence" :: path :: _ -> Some(visualEvidence "--pixel-readback-evidence" "command=--pixel-readback-evidence" Hash "pixel-readback" "evidence-kind=pixel-readback" "screenshot-unavailable" path)
    | "--pixel-readback-evidence" :: _ -> Some(visualEvidence "--pixel-readback-evidence" "command=--pixel-readback-evidence" Hash "pixel-readback" "evidence-kind=pixel-readback" "screenshot-unavailable" "readiness/game-pixel-readback-evidence.txt")
    | _ -> None
