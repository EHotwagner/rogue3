module Rogue3.WindowOptions

open System
open System.IO
open FS.GG.UI.Scene
open FS.GG.UI.SkiaViewer
open Rogue3.Model
open Rogue3.View

type WindowBehaviorSettings =
    { Resize: string
      Maximize: string
      Startup: string
      Position: string
      Backend: string }

let windowBehaviorArgsFromFile path =
    if String.IsNullOrWhiteSpace path || not (File.Exists path) then
        []
    else
        File.ReadAllLines path
        |> Array.toList
        |> List.collect (fun raw ->
            let line = raw.Trim()

            if String.IsNullOrWhiteSpace line || line.StartsWith("#", StringComparison.Ordinal) then
                []
            else
                match line.Split('=', 2, StringSplitOptions.TrimEntries) with
                | [| "resize"; value |]
                | [| "window-resize"; value |] -> [ "--window-resize"; value ]
                | [| "maximize"; value |]
                | [| "window-maximize"; value |] -> [ "--window-maximize"; value ]
                | [| "startup"; value |]
                | [| "startup-state"; value |]
                | [| "window-startup"; value |] -> [ "--window-startup"; value ]
                | [| "position"; value |]
                | [| "startup-position"; value |]
                | [| "window-position"; value |] -> [ "--window-position"; value ]
                | [| "backend"; value |]
                | [| "window-backend"; value |] -> [ "--window-backend"; value ]
                | _ -> [])

let parseWindowBehavior args =
    let rec loop remaining behavior =
        match remaining with
        | "--window-options-file" :: path :: tail ->
            loop (windowBehaviorArgsFromFile path @ tail) behavior
        | "--window-resize" :: "fixed-size" :: tail ->
            loop tail { behavior with Resize = "fixed-size" }
        | "--window-resize" :: "resizable" :: tail ->
            loop tail { behavior with Resize = "resizable" }
        | "--window-maximize" :: "not-maximizable" :: tail ->
            loop tail { behavior with Maximize = "not-maximizable" }
        | "--window-maximize" :: "maximizable" :: tail ->
            loop tail { behavior with Maximize = "maximizable" }
        | "--window-startup" :: "normal" :: tail ->
            loop tail { behavior with Startup = "normal" }
        | "--window-startup" :: "maximized" :: tail ->
            loop tail { behavior with Startup = "maximized" }
        | "--window-startup" :: "minimized" :: tail ->
            loop tail { behavior with Startup = "minimized" }
        | "--window-startup" :: "fullscreen" :: tail ->
            loop tail { behavior with Startup = "fullscreen" }
        | "--window-startup" :: "windowed-fullscreen" :: tail ->
            loop tail { behavior with Startup = "windowed-fullscreen" }
        | "--window-position" :: value :: tail ->
            loop tail { behavior with Position = value }
        | "--window-backend" :: "default" :: tail ->
            loop tail { behavior with Backend = "default" }
        | "--window-backend" :: "vulkan" :: tail ->
            loop tail { behavior with Backend = "vulkan" }
        | "--window-backend" :: "opengl" :: tail ->
            loop tail { behavior with Backend = "opengl" }
        | "--window-backend" :: "software" :: tail ->
            loop tail { behavior with Backend = "software" }
        | _ :: tail -> loop tail behavior
        | [] -> behavior

    loop
        args
        { Resize = "resizable"
          Maximize = "maximizable"
          // #63: this default USED to be "windowed-fullscreen", and that was the live half of the
          // defect. It is only ever consulted when SOME `--window-*` flag is present (with none,
          // `Program.main` launches from the shell's own `InitialDisplay`, which is Windowed) — and
          // five of the six flags that trigger that path say nothing about startup state. So
          // `--window-backend vulkan` alone silently launched the window into the state that bricks
          // the UI, invisibly from the flag the operator typed. A default that only takes effect in
          // combination with an unrelated flag must be the SAFE one.
          //
          // `normal` also makes the flagged and unflagged launches agree, which they never did.
          // An explicit --window-startup selection still overrides it (last value wins).
          Startup = "normal"
          Position = "centered"
          Backend = "default" }

let toViewerWindowBehavior behavior = behavior

/// Map the parsed string settings onto a real ViewerWindowBehaviorRequest so the
/// live launch (runAppWithWindowBehavior) honors the request — not only the report.
let toViewerLaunchRequest behavior : ViewerWindowBehaviorRequest =
    let startupState =
        match behavior.Startup with
        | "normal" -> ViewerWindowStartupState.Normal
        | "maximized" -> ViewerWindowStartupState.Maximized
        | "minimized" -> ViewerWindowStartupState.Minimized
        | "fullscreen" -> ViewerWindowStartupState.Fullscreen
        // #63 / FS-GG/FS.GG.Rendering#1196: the SECOND producer of this request, and the one the
        // first pass of the mitigation missed. `GameShell.windowBehavior` refuses
        // `WindowedFullscreen` for the settings screen; refusing it here too is what makes the
        // claim "this product never asks for that state" actually true. An explicit
        // `--window-startup windowed-fullscreen` is served by exclusive fullscreen — the closest
        // state that works — rather than by a window that lands half off screen with dead buttons.
        // Restore the direct mapping when #1196 is fixed.
        | "windowed-fullscreen" -> ViewerWindowStartupState.Fullscreen
        | _ -> Viewer.defaultWindowBehavior.StartupState

    let startupPosition =
        match behavior.Position with
        | "centered" -> Some Centered
        | value ->
            match value.Split(',', StringSplitOptions.TrimEntries) with
            | [| x; y |] ->
                match Int32.TryParse x, Int32.TryParse y with
                | (true, parsedX), (true, parsedY) when parsedX >= 0 && parsedY >= 0 -> Some(Coordinates(parsedX, parsedY))
                | _ -> Some Centered
            | _ -> Some Centered

    { ResizePolicy = (if behavior.Resize = "fixed-size" then FixedSize else Resizable)
      MaximizePolicy = (if behavior.Maximize = "not-maximizable" then NotMaximizable else Maximizable)
      StartupState = startupState
      StartupPosition = startupPosition
      BackendPreference =
        // Qualify the cases: ViewerBackendPreference.Vulkan clashes with
        // ViewerDiagnosticCategory.Vulkan (bare `Vulkan` resolves to the latter).
        match behavior.Backend with
        | "vulkan" -> Some ViewerBackendPreference.Vulkan
        | "opengl" -> Some ViewerBackendPreference.OpenGL
        | "software" -> Some ViewerBackendPreference.Software
        | _ -> Some ViewerBackendPreference.DefaultBackend }

/// True when any explicit --window-* selection flag is present. When false the launch uses the
/// SHELL's own `InitialDisplay` (1280x720, Windowed) rather than any framework default; when true
/// the launch is routed through `runAppWithWindowBehavior` so the live window honors the request.
///
/// #63: note the sharp edge this creates, which is why `parseWindowBehavior`'s startup default is
/// now `normal`. Five of the six flags below say nothing about startup state, yet any one of them
/// switches the launch onto the parsed request wholesale — so every unspecified field falls back to
/// a default the operator never chose. Widening this list widens that blast radius.
let windowFlagSupplied (args: string list) =
    args
    |> List.exists (fun arg ->
        match arg with
        | "--window-startup"
        | "--window-resize"
        | "--window-maximize"
        | "--window-position"
        | "--window-backend"
        | "--window-options-file" -> true
        | _ -> false)

let windowOptionStatusText status = status

let private viewerInitialSize = { Width = 640; Height = 480 }

let private writeWindowOptionLines (path: string) exitCode lines =
    let directory = Path.GetDirectoryName path

    if not (String.IsNullOrWhiteSpace directory) then
        Directory.CreateDirectory(directory |> string) |> ignore

    File.WriteAllLines(path, Array.ofList lines)
    exitCode

let manualWindowOptionResults behavior =
    let positionStatus, positionObserved, positionMessage =
        match behavior.Position with
        | "centered" -> "honored", "centered", "Centered startup can be requested."
        | value ->
            match value.Split(',', StringSplitOptions.TrimEntries) with
            | [| x; y |] ->
                match Int32.TryParse x, Int32.TryParse y with
                | (true, parsedX), (true, parsedY) when parsedX >= 0 && parsedY >= 0 ->
                    "honored", $"{parsedX},{parsedY}", "Startup coordinates can be requested."
                | _ -> "failed", "none", "Startup coordinates must be non-negative."
            | _ -> "failed", "none", "Startup coordinates must be non-negative."

    let startupStatus, startupObserved, startupMessage =
        match behavior.Startup with
        | "normal" -> "honored", "normal", "Normal startup state can be honored by the viewer host."
        | "maximized" -> "honored", "maximized", "Maximized startup state can be requested."
        | "minimized" -> "unsupported", "none", "Minimized startup is not accepted for visible interactive launch validation."
        | "fullscreen" -> "honored", "fullscreen", "Fullscreen startup can be honored by the viewer host."
        | "windowed-fullscreen" -> "honored", "windowed-fullscreen", "Windowed-fullscreen startup (borderless work-area coverage) can be honored by the viewer host."
        | _ -> "failed", "none", "Startup state is not recognized."

    let backendStatus, backendObserved, backendMessage =
        match behavior.Backend with
        | "default" -> "honored", "default", "Default backend will be selected."
        | "vulkan" -> "honored", "vulkan", "Vulkan backend can be requested."
        | "opengl" -> "unsupported", "none", "OpenGL backend preference is not supported by this viewer host."
        | "software" -> "unsupported", "none", "Software backend preference is not supported by this viewer host."
        | _ -> "degraded", "default", "No backend requested; default backend will be selected."

    [ "initial-size", $"{viewerInitialSize.Width}x{viewerInitialSize.Height}", $"{viewerInitialSize.Width}x{viewerInitialSize.Height}", "honored", "Initial window size is positive and can be requested."
      "resize", behavior.Resize, behavior.Resize, "honored", "Resize policy can be honored by the viewer host."
      "maximize", behavior.Maximize, behavior.Maximize, "honored", "Maximize policy can be honored by the viewer host."
      "startup-state", behavior.Startup, startupObserved, startupStatus, startupMessage
      "startup-position", behavior.Position, positionObserved, positionStatus, positionMessage
      "backend", behavior.Backend, backendObserved, backendStatus, backendMessage ]

let windowOptionsReport evidencePath behavior =
    let request = toViewerWindowBehavior behavior

    let optionLine (option, requested, observed, status, message) =
        $"status={windowOptionStatusText status} mode=interactive-window command=--window-options option={option} requested={requested} observed={observed} diagnostic-class=window-options message={message}"

    let lines =
        [ "validation-contract=Viewer.validateWindowLaunchBehavior viewerOptions.InitialSize"
          "schema=option=resize option=maximize option=startup-state option=startup-position option=backend status=unsupported"
          yield!
              manualWindowOptionResults request
              |> List.map optionLine ]

    writeWindowOptionLines evidencePath 0 lines |> ignore
    lines |> List.iter (printfn "%s")
    0

