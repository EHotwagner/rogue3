// The DECLARED public surface of this module (EHotwagner/rogue3#96, constitution principle III:
// "Public Surface Is Declared, Not Incidental"). A binding absent from this file is private to
// the implementation and is NOT product API -- adding one here is a contracted (Tier 1) change.
module Rogue3.LayoutEvidence

val hudRegionForSize:
  size: FS.GG.UI.Scene.Size -> FS.GG.UI.Scene.LayoutRegionEvidence

val gameplayRegionForSize:
  size: FS.GG.UI.Scene.Size -> FS.GG.UI.Scene.LayoutRegionEvidence

val boundsInside:
  outer: FS.GG.UI.Scene.Rect -> inner: FS.GG.UI.Scene.Rect -> bool

val activeGameplayBoundsForSize:
  size: FS.GG.UI.Scene.Size ->
    model: Model.Model -> FS.GG.UI.Scene.LayoutGameplayBounds

val movementUsesGameplayRegion:
  size: FS.GG.UI.Scene.Size -> model: Model.Model -> bool

val spawnUsesGameplayRegion:
  size: FS.GG.UI.Scene.Size -> model: Model.Model -> bool

val collisionUsesGameplayRegion:
  size: FS.GG.UI.Scene.Size -> model: Model.Model -> bool

val layoutEvidenceForSize:
  size: FS.GG.UI.Scene.Size ->
    model: Model.Model -> FS.GG.UI.Scene.LayoutEvidenceReport

val validateGeneratedLayout:
  report: FS.GG.UI.Scene.LayoutEvidenceReport ->
    Model.GeneratedLayoutValidationResult
