// The DECLARED public surface of this module (EHotwagner/rogue3#96, constitution principle III:
// "Public Surface Is Declared, Not Incidental"). A binding absent from this file is private to
// the implementation and is NOT product API -- adding one here is a contracted (Tier 1) change.
module Rogue3.Program

type Model = Model.Model

type Msg = Model.Msg

val initialModel: Model.Model

val init:
  (unit -> Model.Model * FS.GG.UI.Controls.Elmish.AdapterCommand<Model.Msg>)

val update:
  (Model.Msg ->
     Model.Model ->
     Model.Model * FS.GG.UI.Controls.Elmish.AdapterCommand<Model.Msg>)

val gameplayRegionForSize:
  (FS.GG.UI.Scene.Size -> FS.GG.UI.Scene.LayoutRegionEvidence)

val boundsInside: (FS.GG.UI.Scene.Rect -> FS.GG.UI.Scene.Rect -> bool)

val activeGameplayBoundsForSize:
  (FS.GG.UI.Scene.Size -> Model.Model -> FS.GG.UI.Scene.LayoutGameplayBounds)

val movementUsesGameplayRegion: (FS.GG.UI.Scene.Size -> Model.Model -> bool)

val spawnUsesGameplayRegion: (FS.GG.UI.Scene.Size -> Model.Model -> bool)

val collisionUsesGameplayRegion: (FS.GG.UI.Scene.Size -> Model.Model -> bool)

val layoutEvidenceForSize:
  (FS.GG.UI.Scene.Size -> Model.Model -> FS.GG.UI.Scene.LayoutEvidenceReport)

val validateGeneratedLayout:
  (FS.GG.UI.Scene.LayoutEvidenceReport -> Model.GeneratedLayoutValidationResult)

val view: (Model.Model -> FS.GG.UI.Scene.SceneNode)

val mapKey: (FS.GG.UI.KeyboardInput.ViewerKey -> bool -> Model.Msg option)

val tick: (System.TimeSpan -> Model.Msg option)

val viewerOptions: FS.GG.UI.SkiaViewer.ViewerOptions

val interpretAtHostBoundary:
  (Model.Msg ->
     Model.Model ->
     Model.Model * FS.GG.UI.Controls.Elmish.AdapterCommand<Model.Msg> *
     FS.GG.UI.SkiaViewer.ViewerEffect list)

val generatedHost: FS.GG.UI.SkiaViewer.GeneratedAppHost<Model.Model,Model.Msg>

val interactiveHost:
  FS.GG.UI.Controls.Elmish.InteractiveAppHost<EvidenceCommands.ShellHostModel,
                                              EvidenceCommands.ShellHostMsg>

val parseWindowBehavior: (string list -> WindowOptions.WindowBehaviorSettings)

val main: args: string array -> int
