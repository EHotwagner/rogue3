module Rogue3.GameplayVisualInventory

open Microsoft.FSharp.Reflection
open FS.GG.Game.Core
open FS.GG.UI.Scene
open Rogue3.Geometry
open Rogue3.Model
open Rogue3.Entities

/// The production-owned M6 gameplay-visual subject set. Decorative empty layers are not gameplay
/// elements; every case here resolves to a named scene emitted by Rogue3.Render.
type GameplayVisualElement =
    | FloorBackground
    | ObstacleRock | ObstacleTintedRock | ObstaclePot | ObstacleSpikes | ObstaclePit
    | PickupCoin1 | PickupCoin3 | PickupHalfRedHeart | PickupKey | PickupBomb | PickupSoulHeart
    | EnemyGrub | EnemyMaggot | EnemySpitter | EnemyFly
    | EnemyCharger | EnemyTurret | EnemyCaster | EnemyBrute
    | BossGnawer | BossHollowChoir | BossMaw
    | ShopItem | Shadow | Player | PlayerShot | EnemyBullet | Particle | HudScore

let all =
    FSharpType.GetUnionCases typeof<GameplayVisualElement>
    |> Array.map (fun case -> FSharpValue.MakeUnion(case, [||]) :?> GameplayVisualElement)
    |> Array.toList

let elementId element = string element

let handle = function
    | FloorBackground -> "scene/floor-background"
    | ObstacleRock -> "scene/obstacle/rock"
    | ObstacleTintedRock -> "scene/obstacle/tintedrock"
    | ObstaclePot -> "scene/obstacle/pot"
    | ObstacleSpikes -> "scene/obstacle/spikes"
    | ObstaclePit -> "scene/obstacle/pit"
    | PickupCoin1 -> "scene/pickup/coin-1"
    | PickupCoin3 -> "scene/pickup/coin-3"
    | PickupHalfRedHeart -> "scene/pickup/half-red-heart"
    | PickupKey -> "scene/pickup/key"
    | PickupBomb -> "scene/pickup/bomb"
    | PickupSoulHeart -> "scene/pickup/soul-heart"
    | EnemyGrub -> "token/enemy/grub"
    | EnemyMaggot -> "token/enemy/maggot"
    | EnemySpitter -> "token/enemy/spitter"
    | EnemyFly -> "token/enemy/fly"
    | EnemyCharger -> "token/enemy/charger"
    | EnemyTurret -> "token/enemy/turret"
    | EnemyCaster -> "token/enemy/caster"
    | EnemyBrute -> "token/enemy/brute"
    | BossGnawer -> "token/boss/gnawer"
    | BossHollowChoir -> "token/boss/hollowchoir"
    | BossMaw -> "token/boss/maw"
    | ShopItem -> "scene/shop-item"
    | Shadow -> "scene/shadow"
    | Player -> "scene/player"
    | PlayerShot -> "scene/player-shot"
    | EnemyBullet -> "scene/enemy-bullet"
    | Particle -> "effects/particle"
    | HudScore -> "scene/hud-score"

type VisualBinding =
    { Element: GameplayVisualElement
      Handle: string
      RequiredStates: (string * Model) list
      Project: Model -> Scene }

type RuntimeProjection =
    { Element: GameplayVisualElement
      Handle: string
      Scene: Scene }

let private kindOfEnemy = function
    | EnemyGrub -> Some EnemyKind.Grub | EnemyMaggot -> Some EnemyKind.Maggot
    | EnemySpitter -> Some EnemyKind.Spitter | EnemyFly -> Some EnemyKind.Fly
    | EnemyCharger -> Some EnemyKind.Charger | EnemyTurret -> Some EnemyKind.Turret
    | EnemyCaster -> Some EnemyKind.Caster | EnemyBrute -> Some EnemyKind.Brute
    | _ -> None

let private kindOfBoss = function
    | BossGnawer -> Some BossKind.Gnawer
    | BossHollowChoir -> Some BossKind.HollowChoir
    | BossMaw -> Some BossKind.Maw
    | _ -> None

let private kindOfObstacle = function
    | ObstacleRock -> Some ObstacleKind.Rock | ObstacleTintedRock -> Some ObstacleKind.TintedRock
    | ObstaclePot -> Some ObstacleKind.Pot | ObstacleSpikes -> Some ObstacleKind.Spikes
    | ObstaclePit -> Some ObstacleKind.Pit | _ -> None

let private kindOfPickup = function
    | PickupCoin1 -> Some PickupKind.Coin1 | PickupCoin3 -> Some PickupKind.Coin3
    | PickupHalfRedHeart -> Some PickupKind.HalfRedHeart | PickupKey -> Some PickupKind.Key
    | PickupBomb -> Some PickupKind.Bomb | PickupSoulHeart -> Some PickupKind.SoulHeart
    | _ -> None

let private sampleShopSlots =
    let slots,_,_ = generateShop (Rng.ofSeed 0xC0FFEEUL) (itemPool [])
    slots

let private playerShot =
    spawnShots 1 1 (vec2 640.0 360.0) zero (vec2 1.0 0.0) basePlayerStats
    |> List.head

let private enemyBullet =
    { Id=1;Position=vec2 760.0 360.0;Velocity=zero;Radius=4.0;Damage=1;Homing=0.0;AgeTicks=0 }

let private evidenceModel element =
    match kindOfEnemy element, kindOfBoss element, kindOfObstacle element, kindOfPickup element with
    | Some kind, _, _, _ ->
        { initialModel with M5Enemies=[ spawn 1 100 kind (vec2 320.0 280.0) ] }
    | _, Some kind, _, _ ->
        { initialModel with M5Boss=Some(spawnBoss 200 kind (vec2 420.0 300.0)) }
    | _, _, Some kind, _ ->
        { initialModel with M5Obstacles=[ obstacleAt (vec2 260.0 260.0) (obstacle kind 300) ] }
    | _, _, _, Some kind -> { initialModel with M5ObstacleDrops=[ kind ] }
    | _ ->
        match element with
        | ShopItem -> { initialModel with M5ShopSlots=sampleShopSlots }
        | PlayerShot -> { initialModel with ShotSpawns=[ playerShot ] }
        | EnemyBullet -> { initialModel with EnemyBullets=[ enemyBullet ] }
        | Particle -> update (SpawnM6Particles(1, vec2 640.0 360.0, ParticleTint.Explosion)) initialModel |> fst
        | HudScore -> { initialModel with LeftScore=7;RightScore=3 }
        | FloorBackground | Shadow | Player -> initialModel
        | _ -> invalidOp $"unhandled visual fixture {element}"

let private tryElement elementIdValue = all |> List.tryFind (fun element -> elementId element=elementIdValue)

let project model : RuntimeProjection list =
    Rogue3.Render.renderedElements model
    |> List.groupBy (fun item -> item.ElementId, item.Handle)
    |> List.choose (fun ((id, runtimeHandle), items) ->
        tryElement id
        |> Option.map (fun element ->
            { Element=element;Handle=runtimeHandle;Scene=items |> List.map _.Scene |> Scene.group }))

let bindings =
    all
    |> List.map (fun element ->
        let expectedHandle = handle element
        let state = evidenceModel element
        { Element=element
          Handle=expectedHandle
          RequiredStates=[ "production", state ]
          Project=fun model ->
              project model
              |> List.filter (fun item -> item.Element=element && item.Handle=expectedHandle)
              |> List.map _.Scene
              |> Scene.group })

let registeredBindings = bindings |> List.map (fun item -> elementId item.Element, item.Handle)

let representativeModels = bindings |> List.collect _.RequiredStates |> List.map snd
