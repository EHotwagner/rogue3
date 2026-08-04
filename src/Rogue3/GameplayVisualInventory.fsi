// The DECLARED public surface of this module (EHotwagner/rogue3#96, constitution principle III:
// "Public Surface Is Declared, Not Incidental"). A binding absent from this file is private to
// the implementation and is NOT product API -- adding one here is a contracted (Tier 1) change.
module Rogue3.GameplayVisualInventory

/// The production-owned M6 gameplay-visual subject set. Decorative empty layers are not gameplay
/// elements; every case here resolves to a named scene emitted by Rogue3.Render.
type GameplayVisualElement =
    | FloorBackground
    | ObstacleRock
    | ObstacleTintedRock
    | ObstaclePot
    | ObstacleSpikes
    | ObstaclePit
    | PickupCoin1
    | PickupCoin3
    | PickupHalfRedHeart
    | PickupKey
    | PickupBomb
    | PickupSoulHeart
    | EnemyGrub
    | EnemyMaggot
    | EnemySpitter
    | EnemyFly
    | EnemyCharger
    | EnemyTurret
    | EnemyCaster
    | EnemyBrute
    | BossGnawer
    | BossHollowChoir
    | BossMaw
    | ShopItem
    | ShopSlotReady
    | DoorOpen
    | DoorLockedKey
    | DoorBossDoor
    | DoorHiddenWall
    | DoorLockedClear
    | DoorBossSealed
    | RoomWalls
    | TrapdoorReady
    | DepartedRoom
    | PlayerInvulnerable
    | PlayerDodgeRoll
    | PlayerDown
    | EnemyTelegraph
    | HudHearts
    | HudCurrency
    | HudActiveCharge
    | HudMinimap
    | HudFloorBanner
    | RoomDrop
    | RoomReward
    | Trapdoor
    | Shadow
    | Player
    | PlayerShot
    | EnemyBullet
    | PlacedBomb
    | Particle
    | RunResultOverlay

val all: GameplayVisualElement list

val elementId: element: 'a -> string

type VisualBinding =
    {
      Element: GameplayVisualElement
      Handle: string
      RequiredStates: (string * Model.Model) list
      Project: (Model.Model -> FS.GG.UI.Scene.Scene)
    }

type RuntimeProjection =
    {
      Element: GameplayVisualElement
      Handle: string
      Scene: FS.GG.UI.Scene.Scene
    }

val project: model: Model.Model -> RuntimeProjection list

val bindings: VisualBinding list

val registeredBindings: (string * string) list

val representativeModels: Model.Model list
