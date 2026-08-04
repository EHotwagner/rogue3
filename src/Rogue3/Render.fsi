// The DECLARED public surface of this module (EHotwagner/rogue3#96, constitution principle III:
// "Public Surface Is Declared, Not Incidental"). A binding absent from this file is private to
// the implementation and is NOT product API -- adding one here is a contracted (Tier 1) change.
module Rogue3.Render

[<RequireQualifiedAccess>]
type RenderLayer =
    | FloorBackground
    | FloorDecals
    | Obstacles
    | Pickups
    | Shadows
    | Enemies
    | Player
    | Projectiles
    | Particles
    | Hud
    | ScreenOverlays

type LayerScene =
    {
      Layer: RenderLayer
      Scene: FS.GG.UI.Scene.Scene
    }

val layerOrder: RenderLayer list

type HudLayoutEvidence =
    {
      Size: FS.GG.UI.Scene.Size
      HeartsBounds: FS.GG.UI.Scene.Rect
      CurrencyBounds: FS.GG.UI.Scene.Rect
      ChargeBounds: FS.GG.UI.Scene.Rect
      MinimapBounds: FS.GG.UI.Scene.Rect
      FloorNameBounds: FS.GG.UI.Scene.Rect
      Overlaps: bool
    }

val hudLayoutForSize: size: FS.GG.UI.Scene.Size -> HudLayoutEvidence

/// The named production HUD regions. Rendering and exact-scale evidence consume the same layout
/// record, so removing or renaming a region cannot be hidden by an unchanged aggregate node count.
val hudRegionsForSize:
  size: FS.GG.UI.Scene.Size -> (string * FS.GG.UI.Scene.Rect) list

/// The named HUD regions in the order they are composed. Splitting the inventory must not change one
/// byte of what the viewer receives, so `hudSceneForSize` is exactly this concatenation.
val hudRegionScenes:
  size: FS.GG.UI.Scene.Size ->
    model: Model.Model -> (string * string * FS.GG.UI.Scene.Scene list) list

val hudSceneForSize:
  size: FS.GG.UI.Scene.Size -> model: Model.Model -> FS.GG.UI.Scene.Scene

val enemyToken:
  floorIndex: int ->
    playerPosition: Geometry.Vec2 ->
    actor: Entities.EnemyActor -> FS.GG.UI.Symbology.Token

val enemyTokens: model: Model.Model -> FS.GG.UI.Symbology.Token list

val legibility: model: Model.Model -> FS.GG.UI.Symbology.Legibility.Report

val acceptedLegibility: model: Model.Model -> bool

val particleOpacity: particle: Model.M6Particle -> float

/// Re-exported from `Model`, which owns the geometry so the drawn band and the player's collider are
/// one value (work item 014, DEC-008). Callers and tests keep reading `Render.wallThickness`.
val wallThickness: float

/// The opening a door occupies in the wall its `Direction` names.
val doorwayRect: direction: FloorGeneration.DoorDirection -> FS.GG.UI.Scene.Rect

val roomWallsScene: model: Model.Model -> FS.GG.UI.Scene.Scene

/// The element id and handle a door presents, given the floor-graph state and the derived combat
/// lock. `HiddenWall` wins over the lock — a wall does not become a sealed door when enemies are
/// alive — and the lock wins over `Open`, because a sealed room really has no usable exit.
val doorPresentation:
  graphState: FloorGeneration.DoorState ->
    lock: Entities.DoorState -> string * string

/// How far past the wall, into the room, a door's threshold is drawn. The door then reads as a
/// frame you walk through rather than a stripe painted on the very edge of the screen.
///
/// Re-exported from `Model`, which needs it to keep a placed fixture out from under a door panel.
val doorApron: float

val shopSlotScene:
  at: Geometry.Vec2 -> slot: Entities.ShopSlot -> FS.GG.UI.Scene.Scene

/// Board item #55: the shop's answer to `trapdoorReadyScene`.
///
/// `shopSlotScene` already says what a slot SELLS and what it costs. Nothing said that the player is
/// standing where the purchase can be made, or which button makes it — and #55 is the item that gives
/// a button that meaning at all. Deliberately the same idiom as the trapdoor's `E  DESCEND` halo — a
/// ring around the fixture plus a verb keyed to the same interact button — because the two are the
/// same gesture, and teaching them as two would be a worse product than teaching them as one.
///
/// It also carries the REFUSAL. `Entities.purchase` returns `ok=false` for an unaffordable or
/// key-locked slot and `purchaseM5ShopSlot` then returns the model UNCHANGED, so a refused purchase
/// moves no currency, no offer, no counter and no audio event. The player could always compare the
/// price under the plinth against the HUD's coin count and work it out; what they could not do is
/// tell a refused press apart from a press at empty floor, because both change nothing. The
/// affordable and refused frames differ in colour, in ring stroke and in words, so the answer is
/// legible BEFORE the press as well as after it.
val shopSlotReadyScene:
  at: Geometry.Vec2 ->
    refusal: string option -> _slot: Entities.ShopSlot -> FS.GG.UI.Scene.Scene

/// Where the departed room sits relative to the entered room, in world units.
val departedRoomStep: direction: Model.RoomSlideDirection -> Geometry.Vec2

/// One room's shell: floor, wall band, doors and the trapdoor fixture when the floor records one.
///
/// Doors are read from the floor graph with the combat lock LIFTED, which is honest rather than
/// convenient: `playerRoomIntentsIn` refuses a crossing through a sealed doorway, so the only room a
/// player can be sliding away from is a room whose lock has already lifted.
val roomShellScene: roomId: int -> model: Model.Model -> FS.GG.UI.Scene.Scene

type RenderedElement =
    {
      ElementId: string
      Handle: string
      Layer: RenderLayer
      Scene: FS.GG.UI.Scene.Scene
    }

val renderedElementsIn:
  grammar: FS.GG.UI.Symbology.Grammar ->
    model: Model.Model -> RenderedElement list

val renderedElements: model: Model.Model -> RenderedElement list

val cameraOffset: model: Model.Model -> Geometry.Vec2

val layers: model: Model.Model -> LayerScene list

val viewIn:
  grammar: FS.GG.UI.Symbology.Grammar ->
    model: Model.Model -> FS.GG.UI.Scene.SceneNode

val view: model: Model.Model -> FS.GG.UI.Scene.SceneNode

val viewForSize:
  size: FS.GG.UI.Scene.Size -> model: Model.Model -> FS.GG.UI.Scene.SceneNode
