module Rogue3.Program

open System
open Rogue3.Model
open Rogue3.View
open Rogue3.LayoutEvidence
open FS.GG.UI.SkiaViewer
open System.IO
open Rogue3.WindowOptions
open FS.GG.UI.Controls.Elmish

type Model = Rogue3.Model.Model
type Msg = Rogue3.Model.Msg
type GeneratedLayoutValidationFailureClass = Rogue3.Model.GeneratedLayoutValidationFailureClass
type GeneratedLayoutValidationResult = Rogue3.Model.GeneratedLayoutValidationResult
type WindowBehaviorSettings = Rogue3.WindowOptions.WindowBehaviorSettings

let initialModel = Rogue3.Model.initialModel
let keyName = Rogue3.Model.keyName
let init = Rogue3.Model.init
let update = Rogue3.Model.update
let subscriptions = Rogue3.Model.subscriptions
let hudRegionForSize = Rogue3.LayoutEvidence.hudRegionForSize
let gameplayRegionForSize = Rogue3.LayoutEvidence.gameplayRegionForSize
let boundsInside = Rogue3.LayoutEvidence.boundsInside
let activeGameplayBoundsForSize = Rogue3.LayoutEvidence.activeGameplayBoundsForSize
let movementUsesGameplayRegion = Rogue3.LayoutEvidence.movementUsesGameplayRegion
let spawnUsesGameplayRegion = Rogue3.LayoutEvidence.spawnUsesGameplayRegion
let collisionUsesGameplayRegion = Rogue3.LayoutEvidence.collisionUsesGameplayRegion
let layoutEvidenceForSize = Rogue3.LayoutEvidence.layoutEvidenceForSize
let validateGeneratedLayout = Rogue3.LayoutEvidence.validateGeneratedLayout
let view = Rogue3.View.view
let mapKey = Rogue3.EvidenceCommands.mapKey
let tick = Rogue3.EvidenceCommands.tick
let viewerOptions = Rogue3.EvidenceCommands.viewerOptions
let appCommandName = Rogue3.EvidenceCommands.appCommandName
let viewerEffectsForModel = Rogue3.EvidenceCommands.viewerEffectsForModel
let interpretAtHostBoundary = Rogue3.EvidenceCommands.interpretAtHostBoundary
let generatedHost = Rogue3.EvidenceCommands.generatedHost
let interactiveHost = Rogue3.EvidenceCommands.interactiveHost
let defaultCommand = Rogue3.EvidenceCommands.defaultCommand
let windowBehaviorArgsFromFile = Rogue3.WindowOptions.windowBehaviorArgsFromFile
let parseWindowBehavior = Rogue3.WindowOptions.parseWindowBehavior
let toViewerWindowBehavior = Rogue3.WindowOptions.toViewerWindowBehavior
let windowOptionStatusText = Rogue3.WindowOptions.windowOptionStatusText
let manualWindowOptionResults = Rogue3.WindowOptions.manualWindowOptionResults
let windowOptionsReport = Rogue3.WindowOptions.windowOptionsReport

[<EntryPoint>]
let main args =
    match Rogue3.EvidenceCommands.tryRunEvidenceCommand (List.ofArray args) with
    | Some exitCode -> exitCode
    | None ->
        let args = List.ofArray args
        let windowBehavior = parseWindowBehavior args
        let windowBehaviorRequest = toViewerWindowBehavior windowBehavior
        let capability = Viewer.runtimeCapability()
        let desktopSessionDiagnosticApi = "Viewer.desktopSessionDiagnostic()"

        let optional value =
            value |> Option.defaultValue "none"

        let envOption name =
            match Environment.GetEnvironmentVariable name with
            | null -> None
            | value when String.IsNullOrWhiteSpace value -> None
            | value -> Some value

        let runtimeDirectory = envOption "XDG_RUNTIME_DIR"
        let runtimeDirectoryExists = runtimeDirectory |> Option.exists Directory.Exists
        let waylandDisplay = envOption "WAYLAND_DISPLAY"
        let x11Display = envOption "DISPLAY"

        let displayVariable =
            match waylandDisplay, x11Display with
            | Some value, _ -> Some $"WAYLAND_DISPLAY={value}"
            | None, Some value -> Some $"DISPLAY={value}"
            | None, None -> None

        let displaySocket =
            if runtimeDirectory.IsSome && waylandDisplay.IsSome then
                Some(Path.Combine(runtimeDirectory.Value, waylandDisplay.Value))
            elif x11Display.IsSome then
                let display = x11Display.Value
                let number = display.TrimStart(':').Split('.').[0]
                Some($"/tmp/.X11-unix/X{number}")
            else
                None

        let displaySocketExists = displaySocket |> Option.exists File.Exists
        let sessionBus = envOption "DBUS_SESSION_BUS_ADDRESS"

        let diagnosticClass, desktopMessage =
            if runtimeDirectory.IsNone || displayVariable.IsNone || (displaySocket.IsSome && not displaySocketExists) then
                "unsupported-host", "Desktop session prerequisites are missing before app lifecycle debugging."
            else
                "environment-session-ready", "Desktop session prerequisites are present."

        let missingPackageCapability =
            if List.isEmpty capability.MissingPackageCapabilities then
                "none"
            else
                String.concat "," capability.MissingPackageCapabilities

        let unsupportedHostReasons =
            if List.isEmpty capability.UnsupportedHostReasons then
                "none"
            else
                String.concat "|" capability.UnsupportedHostReasons

        let fallbackFullDesktopSession = "fallback-is-full-desktop-session=false"

        let windowOptionResults =
            manualWindowOptionResults windowBehaviorRequest

        let windowOptionSummary =
            windowOptionResults
            |> List.map (fun (option, _, _, status, _) -> $"{option}:{windowOptionStatusText status}")
            |> String.concat ","

        // Issue #245 (game) / #429 (the Controls seam) / #436 (this line reaching every profile that
        // opens a window). `OpenAlBackend.create` opens a real device and degrades to the record-only
        // Null backend when OpenAL or the device is unavailable, so this is safe headless and in CI:
        // it never throws into rogue3 code. `Audio.play backend` is the `AudioEffect list -> unit`
        // sink the viewer hands every `PlayAudio` batch, in dispatch order. Nothing else in the
        // rogue3 knows a device exists.
        //
        // It is hoisted ABOVE the family branch on purpose: the sink is identical for every family,
        // and the only thing that differs is which launch entry point takes it. Keeping one
        // construction site is what stops a profile from quietly acquiring the packages and a cue
        // seam while still launching through a sinkless overload — which is the exact shape of the
        // defect #436 fixed (`app` could not name `AudioEffect`; `sample-pack` referenced all four
        // FS.GG.Audio packages and then launched via `Viewer.runApp`, wiring none of them).
        use audioBackend = FS.GG.Audio.Host.OpenAlBackend.create Rogue3.AudioCues.resolver
        let audioSink = FS.GG.Audio.Host.Audio.play audioBackend
        use profileStore = new Rogue3.ProfileStore.Store(Rogue3.ProfileStore.platformProfilePath(), TimeSpan.FromMilliseconds 250.0)
        let interactiveHost = Rogue3.EvidenceCommands.createInteractiveHost profileStore

        // Per-family governed default launch (feature 086, FR-004/005/006, D6):
        // POINTER-HOST family (CONTROLS + GAME): a pointer-aware persistent host — a mouse click on a
        // live control dispatches that control's bound message (via MapPointer over the renderTree
        // bounds). The GAME family joins it here (#991/#1000): its turnkey default boots the generic
        // game shell (main menu / Esc pause / settings + rebinding), and a clickable menu needs the
        // pointer host — the keyboard-only `generatedHost` cannot drive the menu buttons. A window flag
        // (e.g. --window-startup normal) threads the parsed behavior into the ACTUAL live launch
        // (feature 122, FR-005), so the scaffold-map remedy is effective instead of inert; with no flag
        // the default windowed-fullscreen path is preserved.
        //
        // #429/#436: both overloads carry the audio sink. `runInteractiveAppWithAudio` is the same
        // message->update->retained-step code path as the sinkless `runInteractiveApp` with the terminal
        // viewer launcher swapped, so sound cannot drift away from the live loop.
        let launchResult =
            // One launch overload owns both paths. With no flags, the shell's authored default
            // (1280x720, Windowed) is explicit rather than inheriting a viewer default that can select
            // different surface/window behavior. Flagged launches replace that request, not the host.
            let launchRequest =
                if Rogue3.WindowOptions.windowFlagSupplied args then
                    Rogue3.WindowOptions.toViewerLaunchRequest windowBehavior
                else
                    Rogue3.GameShell.windowBehavior Rogue3.EvidenceCommands.shellConfig.InitialDisplay

            ControlsElmish.runInteractiveAppWithWindowBehaviorAndAudio viewerOptions launchRequest audioSink interactiveHost

        match launchResult with
        | Result.Ok outcome ->
            let inputDispatchStatus =
                match $"%A{outcome.InputDispatch}" with
                | "Verified"
                | "true" -> "verified"
                | "NotVerified"
                | "false" -> "not-verified"
                | value -> value.ToLowerInvariant()

            printfn "status=%s mode=%s command=%s window-opened=%b window-visible=observed:true accessible-window=true first-frame-presented=%b user-close-observed=%b self-closed-for-evidence=%b input-dispatch=%s exit-path=%b renderer-mode=%s blocked-stage=none classification=none category=none window-options=%s missing-package-capability=%s unsupported-host-reasons=%s diagnostic-api=%s diagnostic-class=%s runtime-directory=%s runtime-directory-exists=%b display-variable=%s display-socket-exists=%b session-bus=%s %s message=%s desktop-message=%s" outcome.Status outcome.Mode defaultCommand outcome.WindowOpened outcome.FirstFramePresented outcome.UserCloseObserved outcome.SelfClosedForEvidence inputDispatchStatus outcome.ExitPath outcome.RendererMode windowOptionSummary missingPackageCapability unsupportedHostReasons desktopSessionDiagnosticApi diagnosticClass (optional runtimeDirectory) runtimeDirectoryExists (optional displayVariable) displaySocketExists (optional sessionBus) fallbackFullDesktopSession outcome.Message desktopMessage
            0
        | Result.Error (failure: ViewerRunFailure) ->
            printfn "status=%s mode=interactive-window command=%s window-visible=unsupported accessible-window=false blocked-stage=%A classification=%A category=%A window-options=%s missing-package-capability=%s unsupported-host-reasons=%s diagnostic-api=%s diagnostic-class=%s runtime-directory=%s runtime-directory-exists=%b display-variable=%s display-socket-exists=%b session-bus=%s %s message=%s desktop-message=%s" (if failure.Classification = UnsupportedEnvironment then "unsupported" else "failed") defaultCommand failure.BlockedStage failure.Classification failure.DiagnosticCategory windowOptionSummary missingPackageCapability unsupportedHostReasons desktopSessionDiagnosticApi diagnosticClass (optional runtimeDirectory) runtimeDirectoryExists (optional displayVariable) displaySocketExists (optional sessionBus) fallbackFullDesktopSession failure.Message desktopMessage
            if failure.Classification = UnsupportedEnvironment then 0 else 1
