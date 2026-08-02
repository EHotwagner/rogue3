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
    | ShopItem
    // Board item #55: the shop's affordance. `ShopItem` says what is for sale; this says that the
    // player is standing where they can buy it, and — when they cannot afford it — why not. Its own
    // element rather than a variation inside `ShopItem`, for the same reason `TrapdoorReady` is its
    // own element and not a variation inside `Trapdoor`: the coverage audit can only require a frame
    // for a state it can NAME.
    | ShopSlotReady
    // M11: one element per door PRESENTATION. The first three come from the floor graph's own
    // `DoorState`; the last two are the room's combat lock, which hides whatever the graph says.
    | DoorOpen | DoorLockedKey | DoorBossDoor | DoorHiddenWall | DoorLockedClear | DoorBossSealed
    | RoomWalls | TrapdoorReady
    // M13: the room being LEFT during a crossing. Without it a slide is a blank screen.
    | DepartedRoom
    // M13: the four states that decide whether a player lives, in world space.
    | PlayerInvulnerable | PlayerDodgeRoll | PlayerDown | EnemyTelegraph
    // M13: the HUD, one row per REGION. It used to be a single `HudScore` row covering hearts,
    // currency, charge, banner and the whole minimap, so deleting the boss-room minimap colour left
    // the coverage audit complete.
    | HudHearts | HudCurrency | HudActiveCharge | HudMinimap | HudFloorBanner
    | RoomDrop | RoomReward | Trapdoor | Shadow | Player | PlayerShot | EnemyBullet | PlacedBomb | Particle | RunResultOverlay

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
    | ShopSlotReady -> "scene/shop-slot-ready"
    | DoorOpen -> "scene/door/open"
    | DoorLockedKey -> "scene/door/locked-key"
    | DoorBossDoor -> "scene/door/boss-door"
    | DoorHiddenWall -> "scene/door/hidden-wall"
    | DoorLockedClear -> "scene/door/locked-clear"
    | DoorBossSealed -> "scene/door/boss-sealed"
    | RoomWalls -> "scene/room-walls"
    | DepartedRoom -> "scene/departed-room"
    | PlayerInvulnerable -> "scene/player-invulnerable"
    | PlayerDodgeRoll -> "scene/player-dodge-roll"
    | PlayerDown -> "scene/player-down"
    | EnemyTelegraph -> "scene/enemy-telegraph"
    | HudHearts -> "scene/hud/hearts"
    | HudCurrency -> "scene/hud/currency"
    | HudActiveCharge -> "scene/hud/active-charge"
    | HudMinimap -> "scene/hud/minimap"
    | HudFloorBanner -> "scene/hud/floor-banner"
    | TrapdoorReady -> "scene/trapdoor-ready"
    | RoomDrop -> "scene/room-drop"
    | RoomReward -> "scene/room-reward"
    | Trapdoor -> "scene/trapdoor"
    | Shadow -> "scene/shadow"
    | Player -> "scene/player"
    | PlayerShot -> "scene/player-shot"
    | EnemyBullet -> "scene/enemy-bullet"
    | PlacedBomb -> "scene/placed-bomb"
    | Particle -> "effects/particle"
    | RunResultOverlay -> "scene/run-result"

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

/// A production model standing in the current floor room, rewritten to carry `doorStates` on the
/// FLOOR GRAPH plus the derived combat `locks`. M11 renders doors from the graph, so a visual fixture
/// that only set the cosmetic `Room.Doors` list would prove nothing about what a player sees.
let private roomShowing doorStates locks trapdoor drop reward =
    let model = initialModel
    let roomId = model.Floor.CurrentRoom
    let room = model.Floor.Rooms.[roomId]
    let doors =
        doorStates
        |> List.mapi (fun index (direction, state) ->
            { FloorGeneration.ToRoom = 500 + index
              FloorGeneration.Direction = direction
              FloorGeneration.State = state })
    let fixtures =
        if trapdoor && not (room.Fixtures |> List.contains FloorGeneration.Trapdoor) then
            room.Fixtures @ [ FloorGeneration.Trapdoor ]
        else room.Fixtures
    { model with
        Floor = { model.Floor with Rooms = Map.add roomId { room with Doors = doors; Fixtures = fixtures } model.Floor.Rooms }
        Room = { model.Room with Doors = locks; Drop = drop; Reward = reward; Trapdoor = trapdoor } }

let private evidenceModel element =
    match kindOfEnemy element, kindOfBoss element, kindOfObstacle element, kindOfPickup element with
    | Some kind, _, _, _ ->
        { initialModel with Enemies=[ spawn 1 100 kind (vec2 320.0 280.0) ] }
    | _, Some kind, _, _ ->
        { initialModel with Boss=Some(spawnBoss 200 kind (vec2 420.0 300.0)) }
    | _, _, Some kind, _ ->
        { initialModel with Obstacles=[ obstacleAt (vec2 260.0 260.0) (obstacle kind 300) ] }
    // M13: a drop is a POSITIONED pickup, so the fixture must carry a position too — and a distinct
    // one per kind, so no two pickup elements can render byte-identical scenes.
    | _, _, _, Some kind ->
        { initialModel with ObstacleDrops=[ { Id=901; Room=initialModel.Floor.CurrentRoom; Kind=kind; Position=vec2 360.0 430.0 } ] }
    | _ ->
        match element with
        | ShopItem -> { initialModel with ShopSlots=sampleShopSlots }
        // Like `TrapdoorReady`, the fixture must be a model the PURCHASE ROUTE accepts, not merely
        // one that carries stock: the player has to be standing at the slot, and hold enough coin
        // that the frame shows the affordable state rather than the refusal. A representative state
        // that only placed the stock would publish a frame the product never draws.
        | ShopSlotReady ->
            let stocked = { initialModel with ShopSlots=sampleShopSlots; PlayerCurrency={ initialModel.PlayerCurrency with Coins=99 } }
            match shopSlotPositions stocked with
            | at :: _ -> { stocked with PlayerPosition=at }
            | [] -> stocked
        | DoorOpen -> roomShowing [ FloorGeneration.North, FloorGeneration.Open ] [ DoorState.Open ] false None None
        | DoorLockedKey -> roomShowing [ FloorGeneration.East, FloorGeneration.LockedKey ] [ DoorState.Open ] false None None
        | DoorBossDoor -> roomShowing [ FloorGeneration.South, FloorGeneration.BossDoor ] [ DoorState.Open ] false None None
        | DoorHiddenWall -> roomShowing [ FloorGeneration.West, FloorGeneration.HiddenWall ] [ DoorState.Open ] false None None
        | DoorLockedClear -> roomShowing [ FloorGeneration.North, FloorGeneration.Open ] [ DoorState.LockedClear ] false None None
        | DoorBossSealed -> roomShowing [ FloorGeneration.North, FloorGeneration.BossDoor ] [ DoorState.BossSealed ] false None None
        | RoomWalls -> roomShowing [ FloorGeneration.North, FloorGeneration.Open; FloorGeneration.East, FloorGeneration.LockedKey ] [ DoorState.Open; DoorState.Open ] false None None
        | RoomDrop -> roomShowing [] [] false (Some PickupKind.Key) None
        | RoomReward -> roomShowing [] [] false None (Some baseItems.Head)
        | Trapdoor -> roomShowing [] [] true None None
        // The ready state must be a model the DESCENT GUARD accepts, not merely one that
        // carries the fixture: the player has to be standing on the trapdoor.
        | TrapdoorReady ->
            roomShowing [] [] true None None
            |> fun model -> { model with PlayerPosition = trapdoorCenter }
        | PlayerShot -> { initialModel with ShotSpawns=[ playerShot ] }
        | EnemyBullet -> { initialModel with EnemyBullets=[ enemyBullet ] }
        | PlacedBomb ->
            { initialModel with Bombs=[ {Id=1;Position=vec2 700.0 390.0;FuseTicks=bombFuseTicks} ] }
        | Particle -> update (SpawnM6Particles(1, vec2 640.0 360.0, ParticleTint.Explosion)) initialModel |> fst
        // `LeftScore`/`RightScore` are the Pong starter's fields and the HUD reads NEITHER, so the
        // previous representative state was a no-op that exercised nothing it claimed to. Vary
        // what the HUD actually draws: hearts of all three kinds, currency, charge and a
        // multi-room minimap.
        | HudHearts | HudCurrency | HudActiveCharge | HudMinimap | HudFloorBanner ->
            { initialModel with
                PlayerHealth = { RedContainers=4; RedHalfHearts=5; SoulHalfHearts=3; BlackHalfHearts=2 }
                PlayerCurrency = { Coins=42; Keys=3; Bombs=7 }
                ActiveCharge = 4
                FloorNameTicks = 120
                Floor = { initialModel.Floor with MapRevealed = initialModel.Floor.Rooms |> Map.toList |> List.map fst |> Set.ofList } }
        // M13: a crossing in flight. The transition names a REAL neighbouring room, so the shell drawn
        // behind the slide is a room the floor graph actually holds rather than an empty group.
        | DepartedRoom ->
            let neighbour =
                initialModel.Floor.Rooms.[initialModel.Floor.CurrentRoom].Doors
                |> List.tryHead
                |> Option.map _.ToRoom
                |> Option.defaultValue initialModel.Floor.CurrentRoom
            { initialModel with
                CameraTransition = Some { Direction=RoomSlideDirection.East; ElapsedTicks=0; FromRoom=neighbour } }
        | PlayerInvulnerable -> { initialModel with PostHitInvulnTicks = postHitInvulnTicks }
        | PlayerDodgeRoll ->
            { initialModel with DodgeRollTicks = rollDurationTicks; PlayerVelocity = vec2 rollSpeed 0.0 }
        // Dead AND at zero health. `{ initialModel with PlayerLifeState = Dead }` alone published a
        // frame showing a downed avatar under three full red hearts — a state play cannot reach, and
        // the two indicators contradicting each other is the opposite of what this element claims.
        | PlayerDown ->
            { initialModel with
                PlayerLifeState = Dead
                PlayerHealth = { initialModel.PlayerHealth with RedHalfHearts = 0 } }
        | EnemyTelegraph ->
            { initialModel with
                Enemies =
                    [ { spawn 1 100 EnemyKind.Charger (vec2 360.0 300.0) with
                          State = EnemyState.ChargerWindUp(vec2 1.0 0.0, 20) } ] }
        | RunResultOverlay ->
            finishRun false (Some DeathCause.Trap) { initialModel with RunActive=true;RunStats={emptyRunStats with DepthReached=3} }
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
