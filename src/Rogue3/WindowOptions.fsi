// The DECLARED public surface of this module (EHotwagner/rogue3#96, constitution principle III:
// "Public Surface Is Declared, Not Incidental"). A binding absent from this file is private to
// the implementation and is NOT product API -- adding one here is a contracted (Tier 1) change.
module Rogue3.WindowOptions

type WindowBehaviorSettings =
    {
      Resize: string
      Maximize: string
      Startup: string
      Position: string
      Backend: string
    }

val windowBehaviorArgsFromFile: path: string -> string list

val parseWindowBehavior: args: string list -> WindowBehaviorSettings

val toViewerWindowBehavior: behavior: 'a -> 'a

/// Map the parsed string settings onto a real ViewerWindowBehaviorRequest so the
/// live launch (runAppWithWindowBehavior) honors the request — not only the report.
val toViewerLaunchRequest:
  behavior: WindowBehaviorSettings ->
    FS.GG.UI.SkiaViewer.ViewerWindowBehaviorRequest

/// True when any explicit --window-* selection flag is present. When false the launch uses the
/// SHELL's own `InitialDisplay` (1280x720, Windowed) rather than any framework default; when true
/// the launch is routed through `runAppWithWindowBehavior` so the live window honors the request.
///
/// #63: note the sharp edge this creates, which is why `parseWindowBehavior`'s startup default is
/// now `normal`. Five of the six flags below say nothing about startup state, yet any one of them
/// switches the launch onto the parsed request wholesale — so every unspecified field falls back to
/// a default the operator never chose. Widening this list widens that blast radius.
val windowFlagSupplied: args: string list -> bool

val windowOptionStatusText: status: 'a -> 'a

val manualWindowOptionResults:
  behavior: WindowBehaviorSettings ->
    (string * string * string * string * string) list

val windowOptionsReport:
  evidencePath: string -> behavior: WindowBehaviorSettings -> int
