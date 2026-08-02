module Rogue3.Model

open System
open System.Text.Json
// ============================================================================================
// GAME family — minimal, replaceable Pong-style starter (feature 220).
//
//   REPLACE ME. This Model/Msg/update is the developer-owned game seam. Swap in your own game
//   by editing Model.fs + View.fs + tests/Rogue3.Tests/BehaviorTests.fs (plus the documented
//   field re-points in LayoutEvidence.fs / EvidenceCommands.fs). The durable governance spine
//   (GovernanceTests.fs, Program.fs, WindowOptions.fs) never calls update/view, so it keeps
//   passing across the swap — see docs/scaffold-map.md.
//
//   Collision-safe positions (feature 250, fs-gg-scene pitfall): a game record that names its
//   fields X/Y/Width/Height collides with FS.GG.UI.Scene.Point/Rect — because the durable
//   LayoutEvidence.fs opens BOTH Scene and this model, its bare `{ X=…; Y=…; Width=…; Height=… }`
//   Rect literals then mis-resolve to YOUR record (a wall of errors in a file you must not touch,
//   surfacing only after a whole model is written). So DON'T put X/Y/Width/Height labels on a game
//   record while Scene is open. Use the collision-safe `Geometry.Vec2` (Vx/Vy) for positions and
//   velocities, and `Geometry.toPoint`/`toRect` to cross into the scene. See Vec2.fs and the
//   `fs-gg-model-swap` / `fs-gg-game-core` skills.
// ============================================================================================
open FS.GG.UI.KeyboardInput
open FS.GG.UI.Canvas
open FS.GG.Game.Core // FixedStep.drain — the fixed-timestep accumulator drain (ADR-0022 P5: moved from FS.GG.UI.Canvas to the FS.GG.Game.Core bottom layer)
open FS.GG.UI.Controls.Elmish
open FS.GG.UI.Controls.Elmish.Authoring // Cmd.none / Sub.none (Elmish-convention no-ops for `[]`)
open Rogue3.Geometry // Vec2 + vec2/add/scale/clamp/toPoint/toRect (collision-safe positions)
open Rogue3.FloorGeneration

type Ball = { Pos: Vec2; Velocity: Vec2 }

type PaddleSide =
    | LeftSide
    | RightSide

type PaddleDirection =
    | PaddleUp
    | PaddleDown

/// One controller poll captured by the host edge. Values remain raw in [-1,1]; resolution applies
/// deadzones and normalization inside the pure fixed-step transition.
type GamepadSnapshot =
    { LeftStick: Vec2
      RightStick: Vec2
      RightTrigger: float
      Buttons: Set<KeyId> }

/// Device state sampled independently from simulation. `InputChanged` replaces this whole value;
/// keyboard and pointer messages update the same shape one field at a time.
type InputSnapshot =
    { Keys: Set<KeyId>
      Commands: Set<string>
      MousePosition: Vec2 option
      MousePrimaryDown: bool
      Gamepad: GamepadSnapshot }

/// The current/previous pair is the replay contract. `PressedThisTick` is derived only when a Tick
/// actually drains a fixed step, then current becomes previous after all drained steps complete.
type InputState =
    { Current: InputSnapshot
      Previous: InputSnapshot
      PressedThisTick: Set<KeyId> }

type AimSource =
    | NoAim
    | ArrowAim
    | MouseAim
    | GamepadAim

type ResolvedInput =
    { Move: Vec2
      Aim: Vec2
      AimSource: AimSource
      FireHeld: bool
      PressedThisTick: Set<KeyId> }

type PlayerStats =
    { Damage: float
      FireRate: float
      ShotSpeed: float
      Range: float
      ShotRadius: float
      Knockback: float
      Multishot: int
      Pierce: int
      Bounce: int
      Homing: float
      SpeedMultiplier: float }

type Stat =
    | DamageStat | TearDelayStat | ShotSpeedStat | RangeStat | SpeedMultiplierStat
    | MultishotStat | ShotRadiusStat | KnockbackStat | PierceStat | BounceStat | HomingStat

type ModifierKind = Add | Mul

type StatModifier =
    { Stat: Stat
      Kind: ModifierKind
      Value: float }

type PlayerItem =
    { Id: string
      Modifiers: StatModifier list }

type Health =
    { RedContainers: int
      RedHalfHearts: int
      SoulHalfHearts: int
      BlackHalfHearts: int }

type Currency = { Coins: int; Keys: int; Bombs: int }

// Board item #20 removed the pre-M5 `Enemy` record that stood here. `Rogue3.Entities.EnemyActor` is
// now the product's only representation of a live enemy: it absorbed `Velocity`, `LastContactTick`
// and `HitFlashTicks`, and `Radius`/`ContactDamage` are read from `Rogue3.Entities.definition` at
// the use site rather than stored per instance.

type EnemyBullet =
    { Id: int
      Position: Vec2
      Velocity: Vec2
      Radius: float
      Damage: int
      Homing: float
      AgeTicks: int }

type Bomb =
    { Id: int
      Position: Vec2
      FuseTicks: int }

// Board item #20 removed the pre-M5 `ShopCost`/`ShopSlot` pair that stood here, together with the
// `InteractShop` message and the `purchaseShopSlot` reducer. `Rogue3.Entities.ShopSlot` (Offer /
// Price / KeyLocked) is the product's only shop stock type, `InteractM5Shop` its only message and
// `purchaseM5ShopSlot` its only reducer. The removed half had zero production dispatch sites.

type PlayerLifeState = Alive | Dead

/// Ordered, transient facts produced while one host update drains fixed simulation steps.
/// AudioCues translates these values at the effect boundary; the simulation never touches a device.
[<RequireQualifiedAccess>]
type AudioEvent =
    | ShotFired
    | ShotHit
    | EnemyDied
    | PlayerHit
    | PlayerDied
    | DodgeRolled
    | BombExploded
    /// A run item entered `PlayerItems`. Emitted by `grantItem` on every route that grants one.
    ///
    /// Board item #55 made a purchase resolve inside a fixed step too, so all three routes — the
    /// pedestal, the boss reward and the shop — now raise this from inside a `Tick`. That is why
    /// `AudioCues.m5ShopPickupCues` suppresses its own item cue on `Tick`: this event already covers
    /// it, and cueing both would play the acquisition sound twice for one purchase.
    | ItemGranted

[<RequireQualifiedAccess>]
type DifficultyMode = Easy | Normal | Hard

type DifficultyScaling =
    { EnemyHpScale: float
      PostHitInvulnSeconds: float
      DropNothingWeight: int
      ExtraStartingContainers: int
      ExtraElitePerCombatRoom: int
      PostBossHeal: bool }

type GameSettings =
    { Difficulty: DifficultyMode
      MasterVolume: float
      Muted: bool
      ScreenShake: bool }

[<RequireQualifiedAccess>]
type DeathCause = Enemy of string | Trap | Bomb

[<RequireQualifiedAccess>]
type StatScope = ThisRun | Lifetime

type RunStats =
    { DepthReached: int
      FloorsCleared: int
      BossKills: int
      KillsByType: Map<Rogue3.Entities.EnemyKind, int>
      ItemsFound: int
      CoinsCollected: int
      RunSeconds: float
      DamageDealt: float
      DamageTaken: float
      DamageByFloor: Map<int, float * float>
      DeathCause: DeathCause option
      Character: string }

type LifetimeStats =
    { RunsPlayed: int
      DeepestFloor: int
      Wins: int
      TotalKills: int
      DeathsByCause: Map<DeathCause, int>
      DepthHistory: int list }

type MetaProfile =
    { Settings: GameSettings
      Lifetime: LifetimeStats
      UnlockedItems: Set<string>
      UnlockedCharacters: Set<string>
      BestScoresBySeed: Map<uint64, int> }

[<RequireQualifiedAccess>]
type RunOutcome = GameOver | Victory

type RunSummary =
    { Outcome: RunOutcome
      Seed: uint64
      FloorsCleared: int
      BossKills: int
      EnemyKills: int
      CoinsCollected: int
      ItemsCollected: int
      RunSeconds: float
      NoHitFloors: int
      Score: int
      UnlocksEarned: string list
      Stats: RunStats }

[<RequireQualifiedAccess>]
type ParticleShape = Circle | Quad

[<RequireQualifiedAccess>]
type ParticleTint = Death | Muzzle | Explosion

type M6Particle =
    { Id: int
      Position: Vec2
      Velocity: Vec2
      LifetimeTicks: int
      AgeTicks: int
      Radius: float
      Shape: ParticleShape
      Tint: ParticleTint }

[<RequireQualifiedAccess>]
type RoomSlideDirection = North | East | South | West

/// A room crossing in flight.
///
/// `FromRoom` is M13's addition and it is what makes the slide watchable. `Render.cameraOffset`
/// translates the entered room a full playfield away at `remaining = 1.0`, so without knowing which
/// room was LEFT the renderer has nothing to put in the space that offset vacates — which is why M11
/// suppressed the slide rather than ship 0.35 s of empty screen. The renderer draws that room's shell
/// one playfield back along the slide axis, so a crossing reads as one room leaving and one arriving.
type M6CameraTransition =
    { Direction: RoomSlideDirection
      ElapsedTicks: int
      FromRoom: int }

/// A pickup lying on the floor of the room, at a world position a player can walk to.
///
/// Before M13 `Model.M5ObstacleDrops` was a bare `PickupKind list`: a smashed pot recorded WHAT it
/// dropped and nothing about WHERE, so the renderer drew the drops as an indexed strip at fixed
/// coordinates and nothing could be collected. `Id` is the destroyed obstacle's id and `Position` is
/// where it stood, so the coin lies where the pot was.
type FloorPickup =
    { Id: int
      /// The room this pickup is lying in. Without it a drop was destroyed by the next room change:
      /// `loadM5Room` cleared the list outright while `recordDestroyedObstacle` is durable floor
      /// state, so smashing a pot and stepping through a door before collecting lost the reward
      /// permanently and the pot never came back to re-roll it.
      Room: int
      Kind: Rogue3.Entities.PickupKind
      Position: Vec2 }

type HomingTarget = { Id: int; Position: Vec2 }

/// A live M2 projectile. Integer age/hit budgets make termination exact at the fixed-step boundary.
type ShotSpawn =
    { Id: int
      Position: Vec2
      Direction: Vec2
      Velocity: Vec2
      Damage: float
      FireRate: float
      Speed: float
      Range: float
      Radius: float
      Knockback: float
      Pierce: int
      HitsRemaining: int
      BouncesRemaining: int
      Homing: float
      AgeTicks: int
      MaxAgeTicks: int
      DistanceTravelled: float
      HitEnemyIds: Set<int>
      SimStep: int }

type Model =
    { Ball: Ball
      LeftPaddleY: float
      RightPaddleY: float
      PaddleHeight: float
      LeftScore: int
      RightScore: int
      Playfield: Vec2 // width = Playfield.Vx, height = Playfield.Vy (no Width/Height labels)
      SimAccumulator: float // seconds carried between Ticks for the fixed-step drain
      SimStepCount: int // whole 1/120-second simulation steps completed
      TickCount: int
      RunSeed: uint64
      LayoutRng: Rng // layout/template stream; combat must never advance it
      DropRng: Rng // drop/AI-variance stream; layout must never advance it
      FloorIndex: int
      Floor: Floor
      LastInput: ViewerKey option
      Input: InputState
      PlayerPosition: Vec2
      PlayerVelocity: Vec2
      PlayerStats: PlayerStats
      PlayerItems: PlayerItem list
      PlayerHealth: Health
      PlayerCurrency: Currency
      PlayerLifeState: PlayerLifeState
      PostHitInvulnTicks: int
      HomingTargets: HomingTarget list
      EnemyBullets: EnemyBullet list
      Bombs: Bomb list
      // Board item #20: the pre-M5 `Obstacles: Rect list`, `Enemies: Enemy list` and
      // `ShopSlots: ShopSlot list` fields stood here beside the three below, with no rule about
      // which was authoritative. All three are gone. The player's blocking-rect set is now derived
      // on demand by `blockingObstacleRects` and stored nowhere, so it cannot go stale.
      M5Enemies: Rogue3.Entities.EnemyActor list
      M5Boss: Rogue3.Entities.BossActor option
      M5ChoirMemberIds: Set<int>
      M5Room: Rogue3.Entities.CombatRoom
      M5Obstacles: Rogue3.Entities.Obstacle list
      M5ShopSlots: Rogue3.Entities.ShopSlot list
      M5ObstacleDrops: FloorPickup list
      M5ItemPool: Rogue3.Entities.ItemPool
      M5AiDecisions: int
      M5BulletEmissions: int
      M5BossBulletEmissions: int
      M5BossPatternEmissions: int
      M5NextEntityId: int
      M5NextBulletId: int
      NextBombId: int
      Facing: Vec2
      LastResolvedInput: ResolvedInput
      FireCooldown: float
      WasFiring: bool
      ShotSpawns: ShotSpawn list
      TotalShotSpawns: int
      NextShotId: int
      DodgeRollTicks: int
      DodgeIFrameTicks: int
      DodgeCooldownTicks: int
      TotalWallQueries: int
      TotalHomingQueries: int
      TotalCombatCandidates: int
      /// Pending secret/adjacent pairs examined by the §14.14 blast scan. A deterministic cost
      /// counter for the `secret-reveal` performance workload, not gameplay state.
      TotalSecretRevealCandidates: int
      /// Doorway sensors examined by the M11 fixed-step door scan. A deterministic cost counter for
      /// the `simulation.door-sensor-candidates` driver, bounded by the four walls of a room.
      TotalDoorSensorQueries: int
      /// Player-versus-floor-pickup overlap tests performed by the M13 fixed-step collection scan.
      /// A deterministic cost counter for the `simulation.floor-pickup-candidates` driver, bounded by
      /// the destructible obstacles a room carries.
      TotalFloorPickupCandidates: int
      BlackHeartBursts: int
      EdgeActionCount: int
      M6Particles: M6Particle list
      M6NextParticleId: int
      M6CameraTransition: M6CameraTransition option
      Profile: MetaProfile
      RunStats: RunStats
      ActiveDifficulty: DifficultyScaling option
      RunActive: bool
      RunOutcome: RunOutcome option
      LastRunSummary: RunSummary option
      StatScope: StatScope
      ActiveCharge: int
      ActiveChargeMaximum: int
      FloorNameTicks: int
      AudioEvents: AudioEvent list }

type Msg =
    /// The program started, and `initialModel` is the state it started in (issue #458).
    ///
    /// This exists so that the initial state passes through the SAME seam every other state passes
    /// through. `AudioCues.forTransition` is a function of a *transition* — and without this message
    /// there is no transition into the initial model, so nothing the initial state implies is ever
    /// cued. Anything you *load* rather than *transition into* — settings, a save game, a restored
    /// session, a replayed checkpoint — enters the model through that door, and a
    /// transition-shaped effect seam cannot see it.
    ///
    /// The failure is invisible from inside the model: a restored volume the mixer was never told
    /// about is indistinguishable, in the model, from one that was restored correctly. It surfaces
    /// as "turn the music down, restart, and get full-volume music from a settings screen that
    /// correctly reports it as quiet".
    ///
    /// `EvidenceCommands.generatedHost.Init` dispatches it as `forTransition Started m m` — the same
    /// function `Update` calls, so there is no separate startup path to drift out of sync. `update`
    /// treats it as identity: it announces the initial state, it does not build it.
    | Started
    | Tick of dtSeconds: float
    | MovePaddle of PaddleSide * PaddleDirection
    | ViewerInput of ViewerKey * isDown: bool
    | KeyChanged of KeyId * isDown: bool
    | CommandChanged of command:string * isDown:bool
    | PointerChanged of position: Vec2 * primaryDown: bool option
    | InputChanged of InputSnapshot
    | RevealSecret of adjacentRoom: int * secretRoom: int
    /// §14.16: spend exactly one key to open a `LockedKey` door from the current room, or change
    /// nothing. Never charges twice, because an already-open door is not `LockedKey`.
    | UnlockDoor of roomId: int
    /// §14.15: cross an open (or boss) doorway from the current room and land at the reciprocal
    /// doorway of the destination. The departed room keeps its cleared/fixture state.
    | TraverseDoor of roomId: int
    | BossCleared of roomId: int
    | DescendFloor
    | EnterM5Room of roomId:int
    | DamageM5Enemy of enemyId:int * damage:float
    | DamageM5Boss of damage:float
    | InteractM5Shop of slotId:int
    | DamageM5Obstacle of obstacleId:int * damage:int
    | SpawnM6Particles of count:int * origin:Vec2 * tint:ParticleTint
    | BeginM6RoomTransition of RoomSlideDirection
    | StartRun of seed:uint64
    | SetDifficulty of DifficultyMode
    | SetMasterVolume of float
    | SetMuted of bool
    | SetScreenShake of bool
    | SetStatScope of StatScope
    // Board item #47 removed `RecordItemFound` here. It incremented `RunStats.ItemsFound` WITHOUT
    // granting an item, it had zero production dispatch sites, and it was the last remaining way the
    // counter and `PlayerItems` could disagree. `Model.grantItem` is now the only writer of either.
    | RecordCoinsCollected of int
    | CompleteRunStats of won:bool * cause:DeathCause option
    | ProfileLoaded of MetaProfile
    | NoOp

// Kept model-agnostic so the durable LayoutEvidence spine validates the skeleton AND a swap.
type GeneratedLayoutValidationFailureClass =
    | MissingLayoutFacts
    | OverlappingLayoutBounds

type GeneratedLayoutValidationResult =
    { Accepted: bool
      FailureClass: GeneratedLayoutValidationFailureClass option
      Diagnostics: string list }

/// Hollow Depths' stable logical coordinate system. The host letterboxes this canvas onto the
/// physical output; simulation and view never observe the window dimensions.
let playfieldWidth = 1280.0
let playfieldHeight = 720.0
let paddleHeight = 96.0
let paddleSpeed = 24.0
let ballRadius = 6.0
let leftPaddleX = 16.0
let rightPaddleX = playfieldWidth - 24.0
let paddleThickness = 8.0

/// One fixed simulation step is 1/120 s (Hollow Depths §7.3 / §13).
let fixedDt = 1.0 / 120.0

/// A host frame may advance no more than five fixed steps. `FixedStep.drainWith` receives this as
/// a maximum accepted frame-time budget, preventing a tab-out or stall from creating catch-up debt.
let maxSteps = 5
let simInterval = fixedDt
let maxFrameTime = float maxSteps * fixedDt
let playerRadius = 13.0
let basePlayerSpeed = 240.0
let playerAcceleration = 2400.0
let playerFriction = 3000.0
let rollSpeed = 460.0
let rollDurationTicks = int (0.45 / fixedDt + 0.5)
let dodgeIFrameTicks = int (0.40 / fixedDt + 0.5)
let dodgeCooldownTicks = int (0.90 / fixedDt + 0.5)
let spreadDegrees = 18.0
let shotVelocityInheritance = 0.25
let postHitInvulnTicks = int (0.80 / fixedDt + 0.5)
let contactRetickTicks = int (0.50 / fixedDt + 0.5)
let hitFlashTicks = int (ceil (0.06 / fixedDt))
let bombFuseTicks = int (1.50 / fixedDt + 0.5)
let bombRadius = 90.0
let spatialCellSize = 64.0

/// Every obstacle is drawn and collided as a 40x40 box centred on its position.
let obstacleExtent = 40.0

/// The player's static collider set for a room: the SINGLE description of which obstacles block
/// movement, and the reason board item #20's `Model.Obstacles` field no longer exists.
///
/// This expression used to be copy-pasted at four assignment sites that each refreshed a stored
/// `Obstacles: Rect list` cache — `resolveBombs`, `loadM5Room`, `damageM5Obstacle` and the boot
/// initialiser. A cache can be stale and a function cannot, so the field went and this stayed.
/// Callers: the player sweep and the `shotWalls` filter in `stepInput`, and the `TotalWallQueries`
/// accounting that counts what those two iterate.
let blockingObstacleRects (obstacles: Rogue3.Entities.Obstacle list) : SimRect list =
    obstacles
    |> List.filter (fun obstacle ->
        Rogue3.Entities.blocksMovement Rogue3.Entities.MovementClass.Grounded obstacle.Kind)
    |> List.map (fun obstacle -> toSimRect obstacle.Position obstacleExtent obstacleExtent)

/// An actor's collision radius. A function of `Kind`, never stored per instance — storing it is what
/// let the maximum-content fixture build a 64-unit enemy no floor can spawn (board item #20).
let actorRadius (actor: Rogue3.Entities.EnemyActor) = (Rogue3.Entities.definition actor.Kind).Radius

/// An actor's contact damage. A function of `Kind`, for the same reason as `actorRadius`.
let actorContactDamage (actor: Rogue3.Entities.EnemyActor) =
    (Rogue3.Entities.definition actor.Kind).ContactDamage

/// Midpoint of the room wall a door in `direction` is carved through, in logical room coordinates.
/// A blast reaches that wall when its radius covers this point, which is the §14.14 trigger and the
/// landing point a traversal arrives at from the opposite side.
let wallMidpoint direction =
    match direction with
    | FloorGeneration.North -> vec2 (playfieldWidth / 2.0) 0.0
    | FloorGeneration.East -> vec2 playfieldWidth (playfieldHeight / 2.0)
    | FloorGeneration.South -> vec2 (playfieldWidth / 2.0) playfieldHeight
    | FloorGeneration.West -> vec2 0.0 (playfieldHeight / 2.0)

// ------------------------------------------------------------------------------------------------
// M11 doorway geometry. A room has at most one door per wall (two rooms cannot share a grid edge in
// two directions), so a door's `Direction` fully determines where it sits: centred on that wall.
//
// THE SENSOR MUST BE SHALLOWER THAN THE ARRIVAL CLEARANCE. A crossing lands the player
// `doorwayClearance` inside the destination's reciprocal doorway; if the sensor reached that far the
// arrival would immediately re-trigger and the player would bounce between two rooms forever.
// ------------------------------------------------------------------------------------------------

/// Half the width of a doorway opening along its wall, in logical room units.
let doorwayHalfSpan = 56.0

/// How far into the room a doorway sensor reaches. Strictly less than `doorwayClearance`.
let doorwaySensorDepth = 14.0

/// How far inside the destination a crossing lands the player, measured from the wall it entered
/// through. The player radius plus a margin, so the arrival never overlaps the wall it came through.
let doorwayClearance = playerRadius + 4.0

/// Wall-normal distance from `position` to the wall `direction` names, and the lateral offset from
/// that wall's midpoint. Together they place a point relative to one doorway.
let doorwayOffsets direction (position: Vec2) =
    match direction with
    | FloorGeneration.North -> position.Vy, position.Vx - playfieldWidth / 2.0
    | FloorGeneration.South -> playfieldHeight - position.Vy, position.Vx - playfieldWidth / 2.0
    | FloorGeneration.West -> position.Vx, position.Vy - playfieldHeight / 2.0
    | FloorGeneration.East -> playfieldWidth - position.Vx, position.Vy - playfieldHeight / 2.0

/// True when `position` is standing in the doorway on the wall `direction` names.
let doorwaySensorContains direction position =
    let depth, lateral = doorwayOffsets direction position
    depth >= 0.0 && depth <= doorwaySensorDepth && abs lateral <= doorwayHalfSpan

// ------------------------------------------------------------------------------------------------
// M13 room shell geometry. THIS IS THE ONE DESCRIPTION OF THE WALL BAND.
//
// It used to live in `Render.fs` alone, so the 24-unit stone band a player looks at was decoration:
// the player's collision bound was the raw playfield rectangle, and walking north put the player's
// disc inside — and partly behind — the wall the renderer had just drawn. Moving the geometry here
// lets `Render.roomWallsScene` and the player's swept cast consume the SAME value, so a slab cannot
// be drawn without being solid or made solid without being drawn.
//
// The gaps are load-bearing and deliberate: every wall that carries a door of ANY state opens a
// `2 * doorwayHalfSpan` gap. `doorwaySensorDepth` (14) is shallower than `wallThickness` (24), so a
// slab spanning a locked doorway would hold the player's centre at depth `24 + playerRadius = 37`
// and `UnlockDoor` could never fire — M11's key-door route, which is driven through movement keys
// alone, would become unreachable. A player may therefore stand in the MOUTH of a locked doorway;
// that is the affordance, not an oversight (work item 014, DEC-002).
// ------------------------------------------------------------------------------------------------

/// Thickness of the drawn stone band that frames a room, in logical room units.
let wallThickness = 24.0

/// The four wall slabs of a room whose doors occupy `directions`, with a gap at each of those walls.
///
/// These are SIMULATION rects (`FS.GG.Game.Core.Rect`) because that is what `Collision.sweepCircle`
/// speaks and the player sweep is the consumer that must not be able to disagree with the picture.
/// `Render` converts them into the scene vocabulary, which is a field-for-field copy.
let roomWallSlabsFor (directions: Set<FloorGeneration.DoorDirection>) : Rect list =
    let hasDoor direction = Set.contains direction directions
    [ if hasDoor FloorGeneration.North then
          yield { X=0.0;Y=0.0;Width=playfieldWidth/2.0-doorwayHalfSpan;Height=wallThickness }
          yield { X=playfieldWidth/2.0+doorwayHalfSpan;Y=0.0;Width=playfieldWidth/2.0-doorwayHalfSpan;Height=wallThickness }
      else yield { X=0.0;Y=0.0;Width=playfieldWidth;Height=wallThickness }
      if hasDoor FloorGeneration.South then
          yield { X=0.0;Y=playfieldHeight-wallThickness;Width=playfieldWidth/2.0-doorwayHalfSpan;Height=wallThickness }
          yield { X=playfieldWidth/2.0+doorwayHalfSpan;Y=playfieldHeight-wallThickness;Width=playfieldWidth/2.0-doorwayHalfSpan;Height=wallThickness }
      else yield { X=0.0;Y=playfieldHeight-wallThickness;Width=playfieldWidth;Height=wallThickness }
      if hasDoor FloorGeneration.West then
          yield { X=0.0;Y=0.0;Width=wallThickness;Height=playfieldHeight/2.0-doorwayHalfSpan }
          yield { X=0.0;Y=playfieldHeight/2.0+doorwayHalfSpan;Width=wallThickness;Height=playfieldHeight/2.0-doorwayHalfSpan }
      else yield { X=0.0;Y=0.0;Width=wallThickness;Height=playfieldHeight }
      if hasDoor FloorGeneration.East then
          yield { X=playfieldWidth-wallThickness;Y=0.0;Width=wallThickness;Height=playfieldHeight/2.0-doorwayHalfSpan }
          yield { X=playfieldWidth-wallThickness;Y=playfieldHeight/2.0+doorwayHalfSpan;Width=wallThickness;Height=playfieldHeight/2.0-doorwayHalfSpan }
      else yield { X=playfieldWidth-wallThickness;Y=0.0;Width=wallThickness;Height=playfieldHeight } ]

/// The directions in which `roomId` carries a PASSABLE doorway on the floor graph.
///
/// `HiddenWall` is excluded, and that exclusion is the whole point. A hidden wall is DRAWN as
/// `Scene.filledRectangle ... stone` in the same colour as the band, and its own renderer comment
/// says "It reads as WALL, not door" -- so opening a collider gap there let the player stand a full
/// 24 units inside solid-looking stone, which is exactly the defect this milestone's wall row exists
/// to close. Nothing needs the gap: a secret is revealed by a bomb blast testing `wallMidpoint`
/// against `bombRadius`, not by the player reaching the wall.
let roomDoorDirections roomId (floor: FloorGeneration.Floor) =
    match Map.tryFind roomId floor.Rooms with
    | None -> Set.empty
    | Some room ->
        room.Doors
        |> List.filter (fun door -> door.State <> FloorGeneration.HiddenWall)
        |> List.map _.Direction
        |> Set.ofList

/// The wall slabs of the room the player is standing in — drawn by `Render.roomWallsScene` and swept
/// by the player in the same fixed step.
let roomWallSlabs (model: Model) = roomWallSlabsFor (roomDoorDirections model.Floor.CurrentRoom model.Floor)

/// How far past the wall, into the room, a door's threshold is drawn. A placed pickup or pedestal
/// must clear this too, or it would sit under a door panel.
let doorApron = 18.0

/// How close the player's centre must come to a floor pickup's centre to collect it.
let floorPickupRadius = 12.0

/// The floor pickups lying in the room the player is standing in. Drops persist across a crossing,
/// so everything that draws or collects them asks this rather than reading the whole list.
let floorPickupsHere (model: Model) =
    model.M5ObstacleDrops |> List.filter (fun pickup -> pickup.Room = model.Floor.CurrentRoom)

/// The drawn trapdoor sits at the centre of the room it belongs to, so it reads as a floor feature
/// a player walks onto rather than a decoration parked near the HUD. Rendering and the `DescendFloor`
/// guard consume this one record, so the fixture a player sees is the fixture the guard tests.
let trapdoorHalfWidth = 44.0
let trapdoorHalfHeight = 26.0
let trapdoorCenter = vec2 (playfieldWidth / 2.0) (playfieldHeight / 2.0)

/// True when the player standing at `position` is on the trapdoor.
let trapdoorContains (position: Vec2) =
    abs (position.Vx - trapdoorCenter.Vx) <= trapdoorHalfWidth
    && abs (position.Vy - trapdoorCenter.Vy) <= trapdoorHalfHeight

// ------------------------------------------------------------------------------------------------
// M13 room placement. Shop stock and the reward pedestal used to be drawn at literal screen
// coordinates — `X = 520 + index*90, Y = 160` and `X = 620, Y = 450` — chosen once and never checked
// against anything. M11's `13-shop-and-reward` frame shows the result: the first shop slot sits on
// top of the pot, and the reward plinth overlaps the spike trap.
//
// Placement is a deterministic FIRST FIT over an authored candidate list, so it is a pure function of
// the room. That matters twice: a test and a frame see the same answer, and adding a candidate to the
// end of the list cannot move an existing room's stock while the earlier candidates still fit.
// ------------------------------------------------------------------------------------------------

/// How far a placed fixture keeps from an obstacle's centre. An obstacle occupies a 40x40 AABB, so
/// half its diagonal is ~28.3; 46 leaves a visible gap rather than a touching edge.
let obstacleClearance = 46.0

/// True when `position` is inside the stone band, or inside a doorway opening plus the apron the door
/// panel is drawn across. Either would put a fixture under the room shell.
let insideRoomShell (position: Vec2) =
    let margin = wallThickness + doorApron
    position.Vx < margin
    || position.Vy < margin
    || position.Vx > playfieldWidth - margin
    || position.Vy > playfieldHeight - margin

/// The authored candidate positions for placed room fixtures, in the order they are tried. They form
/// two rows across the lower half of the room — clear of the four doorways, clear of the trapdoor
/// footprint at the centre, and clear of the HUD bands along the top and the right edge.
let roomPlacementCandidates : Vec2 list =
    [ for row in [ 440.0; 520.0 ] do
        for column in [ 300.0; 440.0; 580.0; 720.0; 860.0; 1000.0 ] -> vec2 column row ]

let private distanceBetween (a: Vec2) (b: Vec2) =
    let d = sub a b
    sqrt (d.Vx*d.Vx + d.Vy*d.Vy)

/// True when a fixture may stand at `position` in the room `obstacles` furnishes.
let placementAccepts (obstacles: Rogue3.Entities.Obstacle list) (taken: Vec2 list) (position: Vec2) =
    not (insideRoomShell position)
    && not (trapdoorContains position)
    && obstacles |> List.forall (fun obstacle -> distanceBetween position obstacle.Position >= obstacleClearance)
    && taken |> List.forall (fun other -> distanceBetween position other >= obstacleClearance)

/// The first `count` accepted candidate positions for a room furnished with `obstacles`.
///
/// Deterministic and total: if the candidate list is exhausted the remaining fixtures fall back to
/// the last accepted position offset along the row, so a pathologically furnished room still places
/// its stock somewhere inside the shell rather than throwing or vanishing.
let placeRoomFixtures (obstacles: Rogue3.Entities.Obstacle list) count : Vec2 list =
    // The fallback lattice only fires when every authored candidate is rejected -- measured over 121
    // seeds x six floors x 1,331 fixture-bearing rooms it never fires -- but it must still be honest
    // when it does. Its first draft stepped 40 units from the previous position and clamped at the
    // right margin, so past the clamp every further fixture landed on the SAME point, and 40 is
    // narrower than the `obstacleClearance` the accept predicate enforces everywhere else. A lattice
    // at `obstacleClearance` spacing inside the shell is distinct by construction.
    let margin = wallThickness + doorApron
    let columns = max 1 (int ((playfieldWidth - 2.0 * margin) / obstacleClearance))
    let fallbackAt index =
        vec2
            (margin + obstacleClearance * (0.5 + float (index % columns)))
            (margin + obstacleClearance * (0.5 + float (index / columns)))
    let rec take remaining candidates fallbackIndex taken =
        if remaining <= 0 then List.rev taken
        else
            match candidates with
            | [] -> take (remaining - 1) [] (fallbackIndex + 1) (fallbackAt fallbackIndex :: taken)
            | candidate :: rest ->
                if placementAccepts obstacles (List.rev taken) candidate then take (remaining - 1) rest fallbackIndex (candidate :: taken)
                else take remaining rest fallbackIndex taken
    take (max 0 count) roomPlacementCandidates 0 []

let basePlayerStats =
    { Damage = 3.5
      FireRate = 2.5
      ShotSpeed = 420.0
      Range = 1.6
      ShotRadius = 5.0
      Knockback = 40.0
      Multishot = 1
      Pierce = 0
      Bounce = 0
      Homing = 0.0
      SpeedMultiplier = 0.0 }

let difficultyScaling = function
    | DifficultyMode.Easy ->
        { EnemyHpScale=0.08; PostHitInvulnSeconds=1.10; DropNothingWeight=35
          ExtraStartingContainers=1; ExtraElitePerCombatRoom=0; PostBossHeal=true }
    | DifficultyMode.Normal ->
        { EnemyHpScale=0.12; PostHitInvulnSeconds=0.80; DropNothingWeight=45
          ExtraStartingContainers=0; ExtraElitePerCombatRoom=0; PostBossHeal=true }
    | DifficultyMode.Hard ->
        { EnemyHpScale=0.18; PostHitInvulnSeconds=0.55; DropNothingWeight=55
          ExtraStartingContainers=0; ExtraElitePerCombatRoom=1; PostBossHeal=false }

let activeScaling model = model.ActiveDifficulty |> Option.defaultValue (difficultyScaling DifficultyMode.Normal)
let difficultyHpMultiplier floor scaling = 1.0 + scaling.EnemyHpScale * float(max 1 floor)

let defaultGameSettings =
    { Difficulty=DifficultyMode.Normal; MasterVolume=1.0; Muted=false; ScreenShake=true }

let emptyRunStats =
    { DepthReached=1; FloorsCleared=0; BossKills=0; KillsByType=Map.empty; ItemsFound=0; CoinsCollected=0; RunSeconds=0.0
      DamageDealt=0.0; DamageTaken=0.0; DamageByFloor=Map.empty; DeathCause=None; Character="Delver" }

let defaultMetaProfile =
    { Settings=defaultGameSettings
      Lifetime={ RunsPlayed=0; DeepestFloor=0; Wins=0; TotalKills=0; DeathsByCause=Map.empty; DepthHistory=[] }
      UnlockedItems=Set.empty; UnlockedCharacters=Set.singleton "delver"; BestScoresBySeed=Map.empty }

let winRatePct lifetime =
    if lifetime.RunsPlayed <= 0 then 0.0 else 100.0 * float lifetime.Wins / float lifetime.RunsPlayed

let depthHistogram depths =
    let bucket floor = if floor <= 3 then 0 elif floor <= 6 then 1 elif floor <= 9 then 2 elif floor <= 12 then 3 else 4
    depths |> List.fold (fun counts floor -> counts |> List.mapi (fun i count -> if i=bucket floor then count+1 else count)) [0;0;0;0;0]

let private clampVolume value = if Double.IsFinite value then max 0.0 (min 1.0 value) else 1.0

let encodeMetaProfile (profile: MetaProfile) =
    let difficulty = match profile.Settings.Difficulty with DifficultyMode.Easy->"easy" | DifficultyMode.Normal->"normal" | DifficultyMode.Hard->"hard"
    let deathKey = function DeathCause.Enemy kind->"enemy:"+kind|DeathCause.Trap->"trap"|DeathCause.Bomb->"bomb"
    let deaths=profile.Lifetime.DeathsByCause|>Map.toList|>List.map(fun(cause,count)->sprintf "{\"cause\":%s,\"count\":%d}" (JsonSerializer.Serialize(deathKey cause)) count)|>String.concat ","
    let strings values=values|>Seq.sort|>Seq.map JsonSerializer.Serialize|>String.concat ","
    let scores=profile.BestScoresBySeed|>Map.toList|>List.map(fun(seed,score)->sprintf "{\"seed\":\"%d\",\"score\":%d}" seed score)|>String.concat ","
    sprintf "{\"format\":\"hollow-depths.meta-profile\",\"version\":1,\"difficulty\":\"%s\",\"masterVolume\":%.6f,\"muted\":%s,\"screenShake\":%s,\"runsPlayed\":%d,\"deepestFloor\":%d,\"wins\":%d,\"totalKills\":%d,\"deathCounts\":[%s],\"depthHistory\":[%s],\"unlockedItems\":[%s],\"unlockedCharacters\":[%s],\"bestScoresBySeed\":[%s]}"
        difficulty profile.Settings.MasterVolume ((string profile.Settings.Muted).ToLowerInvariant())
        ((string profile.Settings.ScreenShake).ToLowerInvariant()) profile.Lifetime.RunsPlayed
        profile.Lifetime.DeepestFloor profile.Lifetime.Wins profile.Lifetime.TotalKills
        deaths (profile.Lifetime.DepthHistory |> List.map string |> String.concat ",")
        (strings profile.UnlockedItems) (strings profile.UnlockedCharacters) scores

let tryDecodeMetaProfile (payload:string) : Result<MetaProfile,string> =
    try
        use document=JsonDocument.Parse payload
        let root=document.RootElement
        let text (name:string) = root.GetProperty(name).GetString()
        let number (name:string) = root.GetProperty(name).GetInt32()
        if text "format" <> "hollow-depths.meta-profile" then Error "unsupported profile format"
        elif number "version" <> 1 then Error "unsupported profile version"
        else
            let difficulty =
                match text "difficulty" with
                | "easy" -> DifficultyMode.Easy | "hard" -> DifficultyMode.Hard | _ -> DifficultyMode.Normal
            let deathCause (value:string) =
                if value.StartsWith("enemy:",StringComparison.Ordinal) then DeathCause.Enemy(value.Substring(6))
                elif value="trap" then DeathCause.Trap else DeathCause.Bomb
            let deaths =
                root.GetProperty("deathCounts").EnumerateArray()
                |> Seq.map(fun item->deathCause(item.GetProperty("cause").GetString()),item.GetProperty("count").GetInt32()) |> Map.ofSeq
            let intList (name:string)=root.GetProperty(name).EnumerateArray()|>Seq.map(fun (item:JsonElement)->item.GetInt32())|>Seq.toList
            let stringSet (name:string)=root.GetProperty(name).EnumerateArray()|>Seq.map(fun (item:JsonElement)->item.GetString())|>Set.ofSeq
            let scores =
                match root.TryGetProperty("bestScoresBySeed") with
                | true, values -> values.EnumerateArray() |> Seq.map(fun item->UInt64.Parse(item.GetProperty("seed").GetString()),item.GetProperty("score").GetInt32()) |> Map.ofSeq
                | _ -> Map.empty
            Ok
                { Settings={Difficulty=difficulty;MasterVolume=root.GetProperty("masterVolume").GetDouble()|>clampVolume;Muted=root.GetProperty("muted").GetBoolean();ScreenShake=root.GetProperty("screenShake").GetBoolean()}
                  Lifetime={RunsPlayed=number "runsPlayed";DeepestFloor=number "deepestFloor";Wins=number "wins";TotalKills=number "totalKills";DeathsByCause=deaths;DepthHistory=intList "depthHistory"}
                  UnlockedItems=stringSet "unlockedItems";UnlockedCharacters=stringSet "unlockedCharacters";BestScoresBySeed=scores }
    with ex -> Error ex.Message

let profilePersistenceRequest profile =
    Persistence.save (Persistence.saveEnvelope 1 (SaveSlot "meta-profile") (encodeMetaProfile profile))

let profilePersistenceRequestsForTransition msg previous next =
    match msg with
    | ProfileLoaded _ -> []
    | _ when previous.Profile <> next.Profile ->
        [ profilePersistenceRequest next.Profile ]
    | _ -> []

let private finiteOr fallback value = if Double.IsFinite value then value else fallback

let recomputePlayerStats (items: PlayerItem list) =
    let modifiers = items |> List.collect (fun item -> item.Modifiers)
    let apply kind stat seed =
        modifiers
        |> List.filter (fun modifier' -> modifier'.Kind = kind && modifier'.Stat = stat)
        |> List.fold (fun value modifier' ->
            let amount = finiteOr 0.0 modifier'.Value
            match kind with Add -> value + amount | Mul -> value * (1.0 + amount)) seed
    let effective statId seed = seed |> apply Add statId |> apply Mul statId
    let integral statId seed = effective statId (float seed) |> Math.Round |> int
    let tearDelay = effective TearDelayStat 12.0 |> max 1.0
    { Damage = effective DamageStat basePlayerStats.Damage |> max 0.5
      FireRate = 30.0 / tearDelay |> max 0.7 |> min 15.0
      ShotSpeed = effective ShotSpeedStat basePlayerStats.ShotSpeed |> max 150.0 |> min 900.0
      Range = effective RangeStat basePlayerStats.Range |> max 0.4 |> min 4.0
      ShotRadius = effective ShotRadiusStat basePlayerStats.ShotRadius |> max 0.1
      Knockback = effective KnockbackStat basePlayerStats.Knockback |> max 0.0
      Multishot = integral MultishotStat basePlayerStats.Multishot |> max 1 |> min 12
      Pierce = integral PierceStat basePlayerStats.Pierce |> max 0
      Bounce = integral BounceStat basePlayerStats.Bounce |> max 0
      Homing = effective HomingStat basePlayerStats.Homing |> max 0.0
      SpeedMultiplier = effective SpeedMultiplierStat basePlayerStats.SpeedMultiplier |> max -0.5 |> min 1.25 }

let totalHalfHearts health =
    max 0 health.RedHalfHearts + max 0 health.SoulHalfHearts + max 0 health.BlackHalfHearts

let displayedHeartHalves health =
    min 24 (2 * (health.RedContainers |> max 0 |> min 12) + max 0 health.SoulHalfHearts + max 0 health.BlackHalfHearts)

let applyDamage halfHearts health =
    let mutable remaining = max 0 halfHearts
    let mutable black = max 0 health.BlackHalfHearts
    let mutable soul = max 0 health.SoulHalfHearts
    let mutable red = max 0 health.RedHalfHearts
    let mutable bursts = 0
    while remaining > 0 && black > 0 do
        black <- black - 1
        remaining <- remaining - 1
        if black % 2 = 0 then bursts <- bursts + 1
    let soulTaken = min remaining soul
    soul <- soul - soulTaken
    remaining <- remaining - soulTaken
    red <- max 0 (red - remaining)
    { health with BlackHalfHearts = black; SoulHalfHearts = soul; RedHalfHearts = red }, bursts

let addTemporaryHearts soul black health =
    let room = max 0 (24 - min 24 (2 * health.RedContainers) - health.SoulHalfHearts - health.BlackHalfHearts)
    let blackAdded = min room (max 0 black)
    let soulAdded = min (room - blackAdded) (max 0 soul)
    { health with
        BlackHalfHearts = health.BlackHalfHearts + blackAdded
        SoulHalfHearts = health.SoulHalfHearts + soulAdded }

let healRed amount health =
    { health with RedHalfHearts = min (2 * (health.RedContainers |> max 0 |> min 12)) (health.RedHalfHearts + max 0 amount) }

let addRedContainer health =
    let containers = min 12 (health.RedContainers + 1)
    { health with RedContainers = containers; RedHalfHearts = min (2 * containers) (health.RedHalfHearts + 2) }

let addCurrency amount current = min 99 (max 0 current + max 0 amount)

let private servedBall =
    { Pos = vec2 (playfieldWidth / 2.0) (playfieldHeight / 2.0)
      Velocity = vec2 5.0 3.0 }

let private emptyGamepad =
    { LeftStick = zero
      RightStick = zero
      RightTrigger = 0.0
      Buttons = Set.empty }

let emptyInputSnapshot: InputSnapshot =
    { Keys = Set.empty
      Commands = Set.empty
      MousePosition = None
      MousePrimaryDown = false
      Gamepad = emptyGamepad }

let emptyInputState =
    { Current = emptyInputSnapshot
      Previous = emptyInputSnapshot
      PressedThisTick = Set.empty }

let private emptyResolvedInput =
    { Move = zero
      Aim = zero
      AimSource = NoAim
      FireHeld = false
      PressedThisTick = Set.empty }

/// Derive independent deterministic layout and combat/drop streams from one serializable run seed.
/// The fixed split order is part of the replay contract.
let rngStreams seed =
    let runRng = Rng.ofSeed seed
    let struct (layoutRng, continuation) = Rng.split runRng
    let struct (dropRng, _) = Rng.split continuation
    layoutRng, dropRng

let initialModelForSeed seed =
    let layoutRng, dropRng = rngStreams seed
    let generated = FloorGeneration.generate seed 1
    { Ball = servedBall
      LeftPaddleY = (playfieldHeight - paddleHeight) / 2.0
      RightPaddleY = (playfieldHeight - paddleHeight) / 2.0
      PaddleHeight = paddleHeight
      LeftScore = 0
      RightScore = 0
      Playfield = vec2 playfieldWidth playfieldHeight
      SimAccumulator = 0.0
      SimStepCount = 0
      TickCount = 0
      RunSeed = seed
      LayoutRng = generated.LayoutRng
      DropRng = dropRng
      FloorIndex = 1
      Floor = generated.Floor
      LastInput = None
      Input = emptyInputState
      PlayerPosition = vec2 (playfieldWidth / 2.0) (playfieldHeight / 2.0)
      PlayerVelocity = zero
      PlayerStats = basePlayerStats
      PlayerItems = []
      PlayerHealth = { RedContainers = 3; RedHalfHearts = 6; SoulHalfHearts = 0; BlackHalfHearts = 0 }
      PlayerCurrency = { Coins = 0; Keys = 1; Bombs = 1 }
      PlayerLifeState = Alive
      PostHitInvulnTicks = 0
      HomingTargets = []
      EnemyBullets = []
      Bombs = []
      M5Enemies = []
      M5Boss = None
      M5ChoirMemberIds = Set.empty
      M5Room =
        { IsBoss=false; Cleared=true; Doors=[]; LiveEnemyIds=Set.empty
          Drop=None; Reward=None; Trapdoor=false }
      M5Obstacles = []
      M5ShopSlots = []
      M5ObstacleDrops = []
      M5ItemPool = generated.ItemPool
      M5AiDecisions = 0
      M5BulletEmissions = 0
      M5BossBulletEmissions = 0
      M5BossPatternEmissions = 0
      M5NextEntityId = 10000
      M5NextBulletId = 10000
      NextBombId = 1
      Facing = vec2 1.0 0.0
      LastResolvedInput = emptyResolvedInput
      FireCooldown = 0.0
      WasFiring = false
      ShotSpawns = []
      TotalShotSpawns = 0
      NextShotId = 1
      DodgeRollTicks = 0
      DodgeIFrameTicks = 0
      DodgeCooldownTicks = 0
      TotalWallQueries = 0
      TotalHomingQueries = 0
      TotalCombatCandidates = 0
      TotalSecretRevealCandidates = 0
      TotalDoorSensorQueries = 0
      TotalFloorPickupCandidates = 0
      BlackHeartBursts = 0
      EdgeActionCount = 0
      M6Particles = []
      M6NextParticleId = 1
      M6CameraTransition = None
      Profile = defaultMetaProfile
      RunStats = emptyRunStats
      ActiveDifficulty = None
      RunActive = false
      RunOutcome = None
      LastRunSummary = None
      StatScope = StatScope.ThisRun
      ActiveCharge = 2
      ActiveChargeMaximum = 6
      FloorNameTicks = 240
      AudioEvents = [] }

// `initialModel` is deliberately NOT defined here any more. M11: the state a player boots into must
// have the starting room LOADED — its doors, obstacles and fixtures derived from the floor graph
// through the same `loadM5Room` seam every other room uses. Hand-writing an empty `M5Room` here is
// what made the starting room a sealed box. The binding now lives immediately after `loadM5Room`.

/// Uniform centered logical-canvas transform used for world-to-screen presentation.
type WorldScreenTransform =
    { Scale: float
      OffsetVx: float
      OffsetVy: float }

let worldScreenTransform outputWidth outputHeight =
    if outputWidth <= 0.0 || outputHeight <= 0.0 then
        { Scale = 1.0; OffsetVx = 0.0; OffsetVy = 0.0 }
    else
        let scale = min (outputWidth / playfieldWidth) (outputHeight / playfieldHeight)
        { Scale = scale
          OffsetVx = (outputWidth - playfieldWidth * scale) / 2.0
          OffsetVy = (outputHeight - playfieldHeight * scale) / 2.0 }

let worldToScreen transform point =
    vec2
        (point.Vx * transform.Scale + transform.OffsetVx)
        (point.Vy * transform.Scale + transform.OffsetVy)

let screenToWorld transform point =
    if transform.Scale <= 0.0 then point
    else
        vec2
            ((point.Vx - transform.OffsetVx) / transform.Scale)
            ((point.Vy - transform.OffsetVy) / transform.Scale)

let keyName key = ViewerKeyboard.toKeyId key

let private clampPaddle model y =
    y |> max 0.0 |> min (model.Playfield.Vy - model.PaddleHeight)

let movePaddle side direction model =
    let delta =
        match direction with
        | PaddleUp -> -paddleSpeed
        | PaddleDown -> paddleSpeed

    match side with
    | LeftSide -> { model with LeftPaddleY = clampPaddle model (model.LeftPaddleY + delta) }
    | RightSide -> { model with RightPaddleY = clampPaddle model (model.RightPaddleY + delta) }

// Keyboard → paddle moves. W/S drive the left paddle; Up/Down the right paddle. Replace this
// mapping when you swap in your own game (EvidenceCommands.mapKey wraps it as ViewerInput).
//
// HOST INPUT BOUNDARY — this PLAY-INPUT mapping is KEYBOARD-ONLY (feature 139). `paddleForKey`
// resolves a `ViewerKey` to a paddle move; `ViewerKey` has NO mouse/pointer case, so a key press
// arrives here as `DispatchInput of ViewerKey * isDown` and maps to `ViewerInput` below. A
// mouse-aimed control scheme (e.g. twin-stick WASD + mouse aim) therefore CANNOT be wired at this
// site: there is no mouse to read here.
//
// The game's turnkey DEFAULT launch, however, now boots the generic game shell on the pointer-aware
// interactive host (#991/#1000): `InteractiveAppHost` driven by `Controls.Elmish.runInteractiveApp`
// (see EvidenceCommands.interactiveHost / Program.fs), the same host the `app`/controls family uses,
// which adds a `MapPointer` seam so the shell's clickable menu works. Live play keys still flow
// through the shell's raw-key seam into `paddleForKey` — so to give GAMEPLAY a mouse you read the
// pointer at the interactive host's `MapPointer`, not at this keyboard mapping. Decide your control
// scheme with that boundary in mind.
let private paddleForKey key =
    match key with
    | Letter 'W' -> Some(LeftSide, PaddleUp)
    | Letter 'S' -> Some(LeftSide, PaddleDown)
    | ArrowUp -> Some(RightSide, PaddleUp)
    | ArrowDown -> Some(RightSide, PaddleDown)
    | _ -> None

let private keyId key = ViewerKeyboard.toKeyId key
let private wKey = keyId (Letter 'W')
let private aKey = keyId (Letter 'A')
let private sKey = keyId (Letter 'S')
let private dKey = keyId (Letter 'D')
let private arrowUpKey = keyId ArrowUp
let private arrowDownKey = keyId ArrowDown
let private arrowLeftKey = keyId ArrowLeft
let private arrowRightKey = keyId ArrowRight
let private qKey = keyId (Letter 'Q')
let private fKey = keyId (Letter 'F')
/// The interact key. `EvidenceCommands.shellConfig` already binds `E` to the rebindable `active`
/// command; before M11 neither the key nor the command was read by anything, so no key a player could
/// press reached a door or a trapdoor.
let private eKey = keyId (Letter 'E')

let private axis negative positive keys =
    (if Set.contains positive keys then 1.0 else 0.0)
    - (if Set.contains negative keys then 1.0 else 0.0)

let normalizeOrZero vector =
    let magnitudeSquared = vector.Vx * vector.Vx + vector.Vy * vector.Vy
    if not (isFinite vector) || magnitudeSquared <= 1e-12 then zero
    else scale (1.0 / sqrt magnitudeSquared) vector

let magnitude vector =
    if not (isFinite vector) then 0.0
    else sqrt (vector.Vx * vector.Vx + vector.Vy * vector.Vy)

let clampMagnitude maximum vector =
    let maximum = if Double.IsFinite maximum then max 0.0 maximum else 0.0
    let length = magnitude vector
    if length <= maximum || length <= 1e-12 then vector
    else scale (maximum / length) vector

let approachVector maximumDelta target current =
    let delta = sub target current
    add current (clampMagnitude maximumDelta delta)

let effectiveMoveSpeed stats =
    let multiplier = if Double.IsFinite stats.SpeedMultiplier then stats.SpeedMultiplier else 0.0
    basePlayerSpeed * (1.0 + multiplier) |> max 120.0 |> min 540.0

let private activeStick vector =
    if not (isFinite vector) then zero
    elif vector.Vx * vector.Vx + vector.Vy * vector.Vy < 0.04 then zero
    else normalizeOrZero vector

let resolveInput playerPosition pressedThisTick (snapshot: InputSnapshot) =
    let commandAxis negative positive =
        (if Set.contains positive snapshot.Commands then 1.0 else 0.0)
        - (if Set.contains negative snapshot.Commands then 1.0 else 0.0)
    let keyboardMove =
        add
            (vec2 (axis aKey dKey snapshot.Keys) (axis wKey sKey snapshot.Keys))
            (vec2 (commandAxis "move-left" "move-right") (commandAxis "move-up" "move-down"))
        |> normalizeOrZero
    let gamepadMove = activeStick snapshot.Gamepad.LeftStick
    let move = add keyboardMove gamepadMove |> normalizeOrZero

    let arrow =
        add
            (vec2 (axis arrowLeftKey arrowRightKey snapshot.Keys) (axis arrowUpKey arrowDownKey snapshot.Keys))
            (vec2 (commandAxis "aim-left" "aim-right") (commandAxis "aim-up" "aim-down"))
        |> normalizeOrZero

    let gamepadAim = activeStick snapshot.Gamepad.RightStick
    let mouseAim =
        snapshot.MousePosition
        |> Option.map (fun cursor -> sub cursor playerPosition |> normalizeOrZero)
        |> Option.defaultValue zero

    let aim, source =
        if arrow <> zero then arrow, ArrowAim
        elif gamepadAim <> zero then gamepadAim, GamepadAim
        elif mouseAim <> zero then mouseAim, MouseAim
        else zero, NoAim

    let trigger =
        if Double.IsFinite snapshot.Gamepad.RightTrigger then snapshot.Gamepad.RightTrigger else 0.0

    { Move = move
      Aim = aim
      AimSource = source
      FireHeld = snapshot.MousePrimaryDown || arrow <> zero || gamepadAim <> zero || trigger >= 0.5
      PressedThisTick = pressedThisTick }

let withKey key isDown (snapshot: InputSnapshot) =
    { snapshot with
        Keys =
            if isDown then Set.add key snapshot.Keys
            else Set.remove key snapshot.Keys }

let withCommand command isDown (snapshot:InputSnapshot) =
    { snapshot with Commands = if isDown then Set.add command snapshot.Commands else Set.remove command snapshot.Commands }

let withPointer position primaryDown (snapshot: InputSnapshot) =
    { snapshot with
        MousePosition = if isFinite position then Some position else snapshot.MousePosition
        MousePrimaryDown = primaryDown |> Option.defaultValue snapshot.MousePrimaryDown }

let fireCadence = 1.0 / 2.5
let playerInputSpeed = 240.0
let shotSpeed = 420.0
let maxShotSpawnHistory = 40

let private dodgeKey = keyId Space

let private rotateDegrees degrees vector =
    let radians = degrees * Math.PI / 180.0
    let c, s = cos radians, sin radians
    vec2 (vector.Vx * c - vector.Vy * s) (vector.Vx * s + vector.Vy * c)

let private centeredDirections count aim =
    let count = count |> max 1 |> min 12
    if count = 1 then [ normalizeOrZero aim ]
    else
        [ for index in 0 .. count - 1 do
              let offset = -spreadDegrees / 2.0 + spreadDegrees * float index / float (count - 1)
              rotateDegrees offset aim |> normalizeOrZero ]

let spawnShots simStep nextId position playerVelocity aim stats =
    let speed = if Double.IsFinite stats.ShotSpeed then stats.ShotSpeed |> max 150.0 |> min 900.0 else 420.0
    let range = if Double.IsFinite stats.Range then stats.Range |> max 0.4 |> min 4.0 else 1.6
    let radius = if Double.IsFinite stats.ShotRadius then max 0.1 stats.ShotRadius else 5.0
    let fireRate = if Double.IsFinite stats.FireRate then stats.FireRate |> max 0.7 |> min 15.0 else 2.5
    centeredDirections stats.Multishot aim
    |> List.mapi (fun index direction ->
        { Id = nextId + index
          Position = position
          Direction = direction
          Velocity = add (scale speed direction) (scale shotVelocityInheritance playerVelocity)
          Damage = if Double.IsFinite stats.Damage then max 0.5 stats.Damage else 3.5
          FireRate = fireRate
          Speed = speed
          Range = range
          Radius = radius
          Knockback = if Double.IsFinite stats.Knockback then max 0.0 stats.Knockback else 0.0
          Pierce = max 0 stats.Pierce
          HitsRemaining = max 0 stats.Pierce + 1
          BouncesRemaining = max 0 stats.Bounce
          Homing = if Double.IsFinite stats.Homing then max 0.0 stats.Homing else 0.0
          AgeTicks = 0
          MaxAgeTicks = int (floor (range / fixedDt + 1e-9))
          DistanceTravelled = 0.0
          HitEnemyIds = Set.empty
          SimStep = simStep })

let private nearestTarget (shot: ShotSpawn) (targets: HomingTarget list) =
    targets
    |> List.filter (fun target -> isFinite target.Position)
    |> List.sortBy (fun target ->
        let delta = sub target.Position shot.Position
        delta.Vx * delta.Vx + delta.Vy * delta.Vy, target.Id)
    |> List.tryHead

let private steerShot (targets: HomingTarget list) (shot: ShotSpawn) =
    if shot.Homing <= 0.0 then shot
    else
        match nearestTarget shot targets with
        | None -> shot
        | Some target ->
            let desired = sub target.Position shot.Position |> normalizeOrZero
            let current = normalizeOrZero shot.Velocity
            if desired = zero || current = zero then shot
            else
                let cross = current.Vx * desired.Vy - current.Vy * desired.Vx
                let dot = current.Vx * desired.Vx + current.Vy * desired.Vy |> max -1.0 |> min 1.0
                let signed = atan2 cross dot
                let cap = shot.Homing * 2.0 * Math.PI * fixedDt
                let turn = signed |> max (-cap) |> min cap
                let speed = magnitude shot.Velocity
                { shot with Velocity = rotateDegrees (turn * 180.0 / Math.PI) current |> scale speed }

let private expandedRect radius (wall: Rect) : Rect =
    { X = wall.X - radius
      Y = wall.Y - radius
      Width = wall.Width + 2.0 * radius
      Height = wall.Height + 2.0 * radius }

let private nearestWallHit walls (shot: ShotSpawn) nextPosition =
    walls
    |> List.mapi (fun index wall ->
        FS.GG.Game.Core.Geometry.segmentAabbHit (toSimPoint shot.Position) (toSimPoint nextPosition) (expandedRect shot.Radius wall)
        |> Option.map (fun hit -> hit.T, index, hit))
    |> List.choose id
    |> List.sortBy (fun (t, index, _) -> t, index)
    |> List.tryHead
    |> Option.map (fun (_, _, hit) -> hit)

let stepShots (roomBounds: Rect) (walls: Rect list) (targets: HomingTarget list) (shots: ShotSpawn list) =
    let mutable wallQueries = 0
    let mutable homingQueries = 0
    let stepped =
        shots
        |> List.choose (fun original ->
            let shot = steerShot targets original
            if shot.Homing > 0.0 then homingQueries <- homingQueries + targets.Length
            let age = shot.AgeTicks + 1
            if shot.HitsRemaining <= 0 || age > shot.MaxAgeTicks || not (isFinite shot.Position && isFinite shot.Velocity) then None
            else
                let next = add shot.Position (scale fixedDt shot.Velocity)
                wallQueries <- wallQueries + walls.Length
                match nearestWallHit walls shot next with
                | Some hit when shot.BouncesRemaining <= 0 -> None
                | Some hit ->
                    let normal = ofSimPoint hit.Normal
                    let reflected = sub shot.Velocity (scale (2.0 * (shot.Velocity.Vx * normal.Vx + shot.Velocity.Vy * normal.Vy)) normal)
                    Some { shot with Position = ofSimPoint hit.Point; Velocity = reflected; AgeTicks = age; BouncesRemaining = shot.BouncesRemaining - 1; DistanceTravelled = shot.DistanceTravelled + magnitude (sub (ofSimPoint hit.Point) shot.Position) }
                | None ->
                    let centre = { Center = toSimPoint next; Radius = shot.Radius }
                    let inside = Collision.clampCircleInside roomBounds centre
                    let leftRoom = inside.Center <> centre.Center
                    if leftRoom && shot.BouncesRemaining <= 0 then None
                    elif leftRoom then
                        let hitX = inside.Center.X <> centre.Center.X
                        let hitY = inside.Center.Y <> centre.Center.Y
                        let velocity = vec2 (if hitX then -shot.Velocity.Vx else shot.Velocity.Vx) (if hitY then -shot.Velocity.Vy else shot.Velocity.Vy)
                        Some { shot with Position = ofSimPoint inside.Center; Velocity = velocity; AgeTicks = age; BouncesRemaining = shot.BouncesRemaining - 1; DistanceTravelled = shot.DistanceTravelled + magnitude (sub (ofSimPoint inside.Center) shot.Position) }
                    else
                        Some { shot with Position = next; AgeTicks = age; DistanceTravelled = shot.DistanceTravelled + magnitude (sub next shot.Position) })
    stepped, wallQueries, homingQueries

let private circlesOverlap aPosition aRadius bPosition bRadius =
    FS.GG.Game.Core.Geometry.circleContact
        { Center = toSimPoint aPosition; Radius = aRadius }
        { Center = toSimPoint bPosition; Radius = bRadius }
    |> Option.isSome

let private addFloorDamage floor dealt taken (stats: RunStats) =
    let oldDealt,oldTaken = Map.tryFind floor stats.DamageByFloor |> Option.defaultValue (0.0,0.0)
    { stats with
        DamageDealt=stats.DamageDealt+dealt
        DamageTaken=stats.DamageTaken+taken
        DamageByFloor=Map.add floor (oldDealt+dealt,oldTaken+taken) stats.DamageByFloor }

let private withAudioEvent event model =
    { model with AudioEvents = model.AudioEvents @ [ event ] }

let private takePlayerHit damage source model =
    if damage <= 0 || model.PlayerLifeState = Dead || model.DodgeIFrameTicks > 0 || model.PostHitInvulnTicks > 0 then model
    else
        let health, bursts = applyDamage damage model.PlayerHealth
        let away = sub model.PlayerPosition source |> normalizeOrZero
        let lost = float (totalHalfHearts model.PlayerHealth - totalHalfHearts health)
        { model with
            PlayerHealth = health
            PlayerVelocity = add model.PlayerVelocity (scale 90.0 away)
            PostHitInvulnTicks = int ((activeScaling model).PostHitInvulnSeconds / fixedDt + 0.5)
            BlackHeartBursts = model.BlackHeartBursts + bursts
            RunStats = addFloorDamage model.FloorIndex 0.0 lost model.RunStats }
        |> withAudioEvent AudioEvent.PlayerHit

// Board item #20 removed `purchaseShopSlot` here. `purchaseM5ShopSlot` is the product's only shop
// reducer; this one had no production dispatch site and a second shop-slot type behind it.

type DescentCarry =
    { Items: PlayerItem list
      Stats: PlayerStats
      Health: Health
      Currency: Currency }

let descentCarry model =
    { Items = model.PlayerItems; Stats = model.PlayerStats; Health = model.PlayerHealth; Currency = model.PlayerCurrency }

let damageM5Boss damage model =
    match model.M5Boss with
    | None -> model
    | Some boss when boss.Kind=Rogue3.Entities.BossKind.HollowChoir -> model
    | Some boss when boss.HitPoints-damage>0.0 ->
        {model with M5Boss=Some{boss with HitPoints=boss.HitPoints-damage};RunStats=addFloorDamage model.FloorIndex (max 0.0 damage) 0.0 model.RunStats}
    | Some boss ->
        let room=Rogue3.Entities.bossCleared model.M5Room.Reward model.M5Room
        let health=
            if (activeScaling model).PostBossHeal then
                {model.PlayerHealth with RedHalfHearts=min (model.PlayerHealth.RedContainers*2) (model.PlayerHealth.RedHalfHearts+2)}
            else model.PlayerHealth
        {model with M5Boss=None;M5Room=room;Floor=FloorGeneration.clearBoss model.Floor.CurrentRoom model.Floor
                    PlayerHealth=health
                    RunStats={addFloorDamage model.FloorIndex (max 0.0 (min boss.HitPoints damage)) 0.0 model.RunStats with
                                BossKills=model.RunStats.BossKills+1;FloorsCleared=max model.RunStats.FloorsCleared model.FloorIndex}}

let private resolveShotCombat model =
    // Board item #20: this resolves shots against the ONE actor list. It used to resolve them
    // against the legacy `Enemies` projection and then copy the hit points back onto `M5Enemies`,
    // and it used to REBUILD `Enemies` from a live filter — which is precisely the §14.21 mechanism:
    // an actor that reached zero vanished from the legacy list here while surviving in `M5Enemies`
    // until the next step. Zero-hit-point actors are now KEPT until `stepM5Entities`'s cleanup, which
    // is the only thing that rolls the drop, credits the kill, splits a grub and clears the room.
    // Dropping them here instead would destroy the drop roll.
    let liveEnemies = model.M5Enemies |> List.filter (fun enemy -> enemy.HitPoints > 0.0)
    let maxRadius = liveEnemies |> List.map (fun enemy -> max 0.0 (actorRadius enemy)) |> List.fold max 0.0
    let grid = SpatialGrid.build spatialCellSize [ for enemy in liveEnemies -> toSimPoint enemy.Position, enemy ]
    let mutable enemies = liveEnemies |> List.map (fun enemy -> enemy.Id, enemy) |> Map.ofList
    let mutable candidates = 0
    let mutable dealt = 0.0
    let mutable hitCount = 0
    let mutable bossModel = model
    let shots =
        model.ShotSpawns
        |> List.choose (fun shot ->
            let nearby = SpatialGrid.queryRadius (toSimPoint shot.Position) (shot.Radius + maxRadius) grid
            candidates <- candidates + nearby.Length
            let hits =
                nearby
                |> List.filter (fun enemy ->
                    not (Set.contains enemy.Id shot.HitEnemyIds)
                    && circlesOverlap shot.Position shot.Radius enemy.Position (actorRadius enemy))
                |> List.sortBy (fun enemy -> enemy.Id)
                |> List.truncate shot.HitsRemaining
            for enemy in hits do
                match Map.tryFind enemy.Id enemies with
                | Some current when current.HitPoints > 0.0 ->
                    hitCount <- hitCount + 1
                    let impulse = normalizeOrZero shot.Velocity |> scale shot.Knockback
                    let applied=max 0.0 (min current.HitPoints shot.Damage)
                    dealt <- dealt+applied
                    enemies <- Map.add enemy.Id
                        { current with
                            HitPoints = max 0.0 (current.HitPoints - shot.Damage)
                            Velocity = add current.Velocity impulse
                            HitFlashTicks = hitFlashTicks } enemies
                | _ -> ()
            let hitIds = (shot.HitEnemyIds, hits) ||> List.fold (fun ids enemy -> Set.add enemy.Id ids)
            let remainingAfterEnemies = shot.HitsRemaining - hits.Length
            let bossHit =
                match bossModel.M5Boss with
                | Some boss when remainingAfterEnemies > 0
                                 && not (Set.contains boss.Id shot.HitEnemyIds)
                                 && circlesOverlap shot.Position shot.Radius boss.Position 44.0
                                 && boss.Kind <> Rogue3.Entities.BossKind.HollowChoir ->
                    bossModel <- damageM5Boss shot.Damage bossModel
                    hitCount <- hitCount + 1
                    true
                | _ -> false
            let hitIds =
                match bossHit, model.M5Boss with
                | true, Some boss -> Set.add boss.Id hitIds
                | _ -> hitIds
            let remaining = remainingAfterEnemies - (if bossHit then 1 else 0)
            if remaining <= 0 then None
            else Some { shot with HitsRemaining = remaining; HitEnemyIds = hitIds })
    { bossModel with
        // Order is the actor list's own, not a Map's: `M5Enemies` is authoritative and its order is
        // what the drop stream and the AI step read.
        M5Enemies =
            model.M5Enemies
            |> List.map (fun actor -> Map.tryFind actor.Id enemies |> Option.defaultValue actor)
        ShotSpawns = shots
        RunStats=addFloorDamage model.FloorIndex dealt 0.0 bossModel.RunStats
        TotalCombatCandidates = model.TotalCombatCandidates + candidates
        AudioEvents = model.AudioEvents @ List.replicate hitCount AudioEvent.ShotHit }

let private resolveBombs model =
    let aged = model.Bombs |> List.map (fun bomb -> { bomb with FuseTicks = bomb.FuseTicks - 1 })
    let mutable pending = aged |> List.filter (fun bomb -> bomb.FuseTicks <= 0) |> List.map (fun bomb -> bomb.Id) |> Set.ofList
    let mutable exploded = Set.empty
    while not (Set.isEmpty pending) do
        let id = Set.minElement pending
        pending <- Set.remove id pending
        if not (Set.contains id exploded) then
            exploded <- Set.add id exploded
            let source = aged |> List.find (fun bomb -> bomb.Id = id)
            for other in aged do
                if not (Set.contains other.Id exploded) && circlesOverlap source.Position bombRadius other.Position 0.1 then
                    pending <- Set.add other.Id pending
    let mutable result = model
    for id in exploded |> Set.toList |> List.sort do
        let bomb = aged |> List.find (fun candidate -> candidate.Id = id)
        let enemies =
            result.M5Enemies
            |> List.map (fun enemy ->
                if enemy.HitPoints > 0.0 && circlesOverlap bomb.Position bombRadius enemy.Position (actorRadius enemy) then
                    { enemy with HitPoints = max 0.0 (enemy.HitPoints - 40.0); HitFlashTicks = hitFlashTicks }
                else enemy)
        result <- { result with M5Enemies = enemies } |> takePlayerHit 2 bomb.Position
        // §14.14: a blast that reaches the wall shared with a hidden secret carves its door inside
        // THIS step. `revealSecret` moves the door records, the hidden flag, the graph adjacency and
        // the pending set as one value, so no observer can see a door without its adjacency.
        let candidates = FloorGeneration.pendingSecretsFrom result.Floor.CurrentRoom result.Floor
        let revealedFloor =
            candidates
            |> List.fold
                (fun floor (adjacent, secret) ->
                    match FloorGeneration.roomDirection adjacent secret floor with
                    | Some direction when magnitude (sub bomb.Position (wallMidpoint direction)) <= bombRadius ->
                        FloorGeneration.revealSecret adjacent secret floor
                    | _ -> floor)
                result.Floor
        result <-
            { result with
                Floor = revealedFloor
                TotalSecretRevealCandidates = result.TotalSecretRevealCandidates + candidates.Length }
        for obstacle in result.M5Obstacles do
            if circlesOverlap bomb.Position bombRadius obstacle.Position 20.0 then
                let remaining,drop,rng=Rogue3.Entities.destroyObstacle 40 result.DropRng obstacle
                let typed=result.M5Obstacles|>List.filter(fun value->value.Id<>obstacle.Id)|>fun others->remaining|>Option.map(fun value->value::others)|>Option.defaultValue others
                let floor=if remaining.IsNone then FloorGeneration.recordDestroyedObstacle result.Floor.CurrentRoom obstacle.Id result.Floor else result.Floor
                result <- {result with M5Obstacles=typed;DropRng=rng;Floor=floor;M5ObstacleDrops=drop|>Option.map(fun value->result.M5ObstacleDrops@[{Id=obstacle.Id;Room=result.Floor.CurrentRoom;Kind=value;Position=obstacle.Position}])|>Option.defaultValue result.M5ObstacleDrops}
    { result with
        Bombs = aged |> List.filter (fun bomb -> not (Set.contains bomb.Id exploded))
        AudioEvents = result.AudioEvents @ List.replicate exploded.Count AudioEvent.BombExploded }

let private resolveEnemyDamage model =
    let bulletGrid = SpatialGrid.build spatialCellSize [ for bullet in model.EnemyBullets -> toSimPoint bullet.Position, bullet ]
    let maxBulletRadius = model.EnemyBullets |> List.map (fun bullet -> max 0.0 bullet.Radius) |> List.fold max 0.0
    let bullets = SpatialGrid.queryRadius (toSimPoint model.PlayerPosition) (playerRadius + maxBulletRadius) bulletGrid
    let mutable result = model
    let mutable consumed = Set.empty
    for bullet in bullets |> List.sortBy (fun bullet -> bullet.Id) do
        if circlesOverlap result.PlayerPosition playerRadius bullet.Position bullet.Radius then
            let before = result.PlayerHealth
            result <- takePlayerHit bullet.Damage bullet.Position result
            if result.PlayerHealth <> before then consumed <- Set.add bullet.Id consumed
    let enemyGrid = SpatialGrid.build spatialCellSize [ for enemy in result.M5Enemies do if enemy.HitPoints > 0.0 then toSimPoint enemy.Position, enemy ]
    let maxEnemyRadius = result.M5Enemies |> List.map (fun enemy -> max 0.0 (actorRadius enemy)) |> List.fold max 0.0
    let contacts = SpatialGrid.queryRadius (toSimPoint result.PlayerPosition) (playerRadius + maxEnemyRadius) enemyGrid |> List.sortBy (fun enemy -> enemy.Id)
    let mutable contactTicks = Map.empty
    for enemy in contacts do
        let ready = enemy.LastContactTick |> Option.forall (fun tick -> result.SimStepCount + 1 - tick >= contactRetickTicks)
        if ready && circlesOverlap result.PlayerPosition playerRadius enemy.Position (actorRadius enemy) then
            let before = result.PlayerHealth
            result <- takePlayerHit (actorContactDamage enemy) enemy.Position result
            if result.PlayerHealth <> before then contactTicks <- Map.add enemy.Id (result.SimStepCount + 1) contactTicks
    { result with
        EnemyBullets = result.EnemyBullets |> List.filter (fun bullet -> not (Set.contains bullet.Id consumed))
        M5Enemies = result.M5Enemies |> List.map (fun enemy ->
            { enemy with
                LastContactTick = Map.tryFind enemy.Id contactTicks |> Option.orElse enemy.LastContactTick
                HitFlashTicks = max 0 (enemy.HitFlashTicks - 1) })
        TotalCombatCandidates = result.TotalCombatCandidates + bullets.Length + contacts.Length }

let private resolveCombat model =
    let burstsBefore = model.BlackHeartBursts
    let aliveBefore = model.M5Enemies |> List.filter (fun enemy -> enemy.HitPoints > 0.0) |> List.map _.Id |> Set.ofList
    let lifeBefore = model.PlayerLifeState
    let enemyBullets =
        model.EnemyBullets
        |> List.choose(fun bullet->
            let velocity =
                if bullet.Homing<=0.0 then bullet.Velocity else
                let desired=sub model.PlayerPosition bullet.Position|>normalizeOrZero
                let blended=add (scale (1.0-min 1.0 (bullet.Homing*fixedDt)) (normalizeOrZero bullet.Velocity)) (scale (min 1.0 (bullet.Homing*fixedDt)) desired)|>normalizeOrZero
                scale (magnitude bullet.Velocity) blended
            let next=add bullet.Position (scale fixedDt velocity)
            let age=bullet.AgeTicks+1
            let obstacleHit =
                model.M5Obstacles
                |> List.exists(fun obstacle->Rogue3.Entities.blocksShots obstacle.Kind && circlesOverlap next bullet.Radius obstacle.Position 20.0)
            if age>480 || obstacleHit || next.Vx<0.0 || next.Vx>playfieldWidth || next.Vy<0.0 || next.Vy>playfieldHeight then None
            else Some{bullet with Position=next;Velocity=velocity;AgeTicks=age})
    let model = {model with EnemyBullets=enemyBullets} |> resolveShotCombat |> resolveBombs |> resolveEnemyDamage
    let burstsThisStep = model.BlackHeartBursts - burstsBefore
    let enemies =
        if burstsThisStep > 0 then
            model.M5Enemies |> List.map (fun enemy -> { enemy with HitPoints = max 0.0 (enemy.HitPoints - 10.0 * float burstsThisStep) })
        else model.M5Enemies
    let resolved =
        { model with
            M5Enemies = enemies
            PlayerLifeState = if totalHalfHearts model.PlayerHealth = 0 then Dead else model.PlayerLifeState }
    let aliveAfter = resolved.M5Enemies |> List.filter (fun enemy -> enemy.HitPoints > 0.0) |> List.map _.Id |> Set.ofList
    let deathCount = Set.difference aliveBefore aliveAfter |> Set.count
    let events =
        resolved.AudioEvents
        @ List.replicate deathCount AudioEvent.EnemyDied
        @ (if lifeBefore = Alive && resolved.PlayerLifeState = Dead then [ AudioEvent.PlayerDied ] else [])
    { resolved with AudioEvents = events }

let private m5Kind = function
    | FloorGeneration.Grub -> Rogue3.Entities.EnemyKind.Grub
    | FloorGeneration.Maggot -> Rogue3.Entities.EnemyKind.Maggot
    | FloorGeneration.Spitter -> Rogue3.Entities.EnemyKind.Spitter
    | FloorGeneration.Fly -> Rogue3.Entities.EnemyKind.Fly
    | FloorGeneration.Charger -> Rogue3.Entities.EnemyKind.Charger
    | FloorGeneration.Turret -> Rogue3.Entities.EnemyKind.Turret
    | FloorGeneration.Caster -> Rogue3.Entities.EnemyKind.Caster
    | FloorGeneration.Brute -> Rogue3.Entities.EnemyKind.Brute

let private roomPoint (cell:Cell) = vec2 (80.0+float cell.Col*40.0) (80.0+float cell.Row*40.0)

let loadM5Room roomId model =
    match Map.tryFind roomId model.Floor.Rooms with
    | None -> model
    | Some room ->
        let scaling=activeScaling model
        let scaleActor (actor:Rogue3.Entities.EnemyActor) =
            let normal=Rogue3.Entities.hpScale model.FloorIndex
            {actor with HitPoints=actor.HitPoints/normal*difficultyHpMultiplier model.FloorIndex scaling}
        // A cleared room stays cleared: nothing repopulates it, so its clear drop cannot be rolled a
        // second time and returning through a door finds the room as it was left (§14.15).
        let enemies =
            (if room.Cleared then [] else room.Interior.EnemyAnchors)
            |> List.mapi(fun index anchor -> Rogue3.Entities.spawn model.FloorIndex (roomId*100+index+1) (m5Kind anchor.Kind) (roomPoint anchor.Cell))
            |> List.map scaleActor
            |> fun baseEnemies ->
                if room.RoomType=FloorGeneration.Combat && not room.Cleared && scaling.ExtraElitePerCombatRoom>0 then
                    [for index in 1..scaling.ExtraElitePerCombatRoom ->
                        Rogue3.Entities.spawn model.FloorIndex (roomId*100+80+index) Rogue3.Entities.EnemyKind.Brute (vec2 (900.+float index*32.) 320.) |> scaleActor]
                    @ baseEnemies
                else baseEnemies
        let obstacleKinds =
            [|Rogue3.Entities.ObstacleKind.Rock;Rogue3.Entities.ObstacleKind.TintedRock;Rogue3.Entities.ObstacleKind.Pot;Rogue3.Entities.ObstacleKind.Spikes;Rogue3.Entities.ObstacleKind.Pit|]
        let typedObstacles =
            obstacleKinds
            |> Array.toList
            |> List.mapi(fun index kind -> index, kind)
            |> List.filter(fun (index, _) -> not (Set.contains (roomId*100+index) room.DestroyedObstacles))
            |> List.map(fun (index, kind) ->
                let position =
                    room.Interior.Obstacles
                    |> List.tryItem index
                    |> Option.map roomPoint
                    |> Option.defaultValue (vec2 (220.+float index*150.) (180.+float(index%2)*300.))
                Rogue3.Entities.obstacle kind (roomId*100+index) |> Rogue3.Entities.obstacleAt position)
        let shop = room.Fixtures |> List.tryPick(function FloorGeneration.ShopStock slots->Some slots |_->None) |> Option.defaultValue []
        let reward = room.Fixtures |> List.tryPick(function FloorGeneration.BossReward item|FloorGeneration.ItemPedestal item->Some item |_->None)
        let isBoss=room.RoomType=FloorGeneration.Boss
        let isBoss=room.RoomType=FloorGeneration.Boss
        let bossKind =
            if model.FloorIndex=1 then Rogue3.Entities.BossKind.Gnawer
            elif model.FloorIndex=2 then Rogue3.Entities.BossKind.HollowChoir
            else Rogue3.Entities.BossKind.Maw
        let choirMembers =
            if isBoss && not room.Cleared && bossKind=Rogue3.Entities.BossKind.HollowChoir then
                [ for index in 0..2 ->
                    Rogue3.Entities.spawn model.FloorIndex (roomId*100+50+index) Rogue3.Entities.EnemyKind.Caster (vec2 (520.+float index*120.) 400.) |> scaleActor ]
            else []
        let allEnemies=enemies@choirMembers
        let roomState : Rogue3.Entities.CombatRoom =
            // `Doors` here is the DERIVED COMBAT-LOCK PROJECTION of `room.Doors` (§M11 one-door
            // model): one entry per floor-graph door, in the same order, carrying only whether combat
            // has sealed the room. Direction and door state live on `room.Doors` and nowhere else,
            // and `Render` zips the two lists by index.
            { Rogue3.Entities.CombatRoom.IsBoss=isBoss
              Rogue3.Entities.CombatRoom.Cleared=room.Cleared
              Rogue3.Entities.CombatRoom.Doors=room.Doors|>List.map(fun _->Rogue3.Entities.DoorState.Open)
              Rogue3.Entities.CombatRoom.LiveEnemyIds=allEnemies|>List.map _.Id|>Set.ofList
              Rogue3.Entities.CombatRoom.Drop=None
              // Board item #47: the reward is durable FLOOR state, exactly like the trapdoor above.
              // This used to read `if isBoss then reward else None`, which silently DISCARDED every
              // `ItemPedestal` — the treasure room's whole reason to exist — because a treasure room
              // is not a boss room. `FloorGeneration` drew the pedestal item from the shared pool and
              // marked it Placed, so the item was consumed from the run and then thrown away. Both
              // fixtures now surface; `collectRoomReward` decides WHEN each may be taken.
              Rogue3.Entities.CombatRoom.Reward=reward
              // M11: the trapdoor is durable FLOOR state, so a room that records the fixture presents
              // it every time it is entered — not only in the session whose boss died.
              Rogue3.Entities.CombatRoom.Trapdoor=(room.Fixtures |> List.contains FloorGeneration.Trapdoor) }
            |> Rogue3.Entities.enterRoom (allEnemies|>List.map _.Id)
        let boss =
            if isBoss && not room.Cleared then
                let value=Rogue3.Entities.spawnBoss (roomId*100) bossKind (vec2 640. 280.)
                Some{value with HitPoints=value.HitPoints*difficultyHpMultiplier model.FloorIndex scaling}
            else None
        { model with Floor={model.Floor with CurrentRoom=roomId};M5Enemies=allEnemies;M5Boss=boss;M5ChoirMemberIds=choirMembers|>List.map _.Id|>Set.ofList;M5Room=roomState
                     // M13: uncollected drops SURVIVE a room change. Clearing them here destroyed the
                     // reward permanently, because `recordDestroyedObstacle` is durable floor state and
                     // the smashed pot never returns to re-roll. They are kept keyed by room and
                     // filtered by `floorPickupsHere`; `DescendFloor` is what discards them.
                     M5Obstacles=typedObstacles;M5ShopSlots=shop
                     EnemyBullets=[];ShotSpawns=[] }

/// The state a player actually boots into for `seed`: the generated floor with its START room
/// LOADED. Before M11 the boot model hand-wrote `M5Room` with `Doors=[]` and `Trapdoor=false`, so the
/// first room a player ever saw had no exits by construction — the room was a sealed box and the
/// renderer was telling the truth about it.
let bootModelForSeed seed = initialModelForSeed seed |> loadM5Room 0

let initialModel = bootModelForSeed 0xC0FFEEUL

let damageM5Enemy enemyId damage model =
    match model.M5Enemies |> List.tryFind(fun actor->actor.Id=enemyId) with
    | None -> model
    | Some actor when actor.HitPoints-damage>0.0 ->
        let hp=actor.HitPoints-damage
        {model with M5Enemies=model.M5Enemies|>List.map(fun a->if a.Id=enemyId then {a with HitPoints=hp} else a)
                    RunStats=addFloorDamage model.FloorIndex (max 0.0 damage) 0.0 model.RunStats}
    | Some actor ->
        let children=Rogue3.Entities.grubSplit model.FloorIndex model.M5NextEntityId actor
        let survivors=model.M5Enemies|>List.filter(fun a->a.Id<>enemyId)
        let childIds=children|>List.map _.Id|>Set.ofList
        let live=Set.union (Set.remove enemyId model.M5Room.LiveEnemyIds) childIds
        let room,rng=
            if model.M5Room.IsBoss || not(Set.isEmpty childIds) then {model.M5Room with LiveEnemyIds=live},model.DropRng
            else Rogue3.Entities.enemyDiedWithNothingWeight (activeScaling model).DropNothingWeight enemyId model.DropRng model.M5Room
        let choirDeath=Set.contains enemyId model.M5ChoirMemberIds
        let boss =
            if choirDeath then model.M5Boss|>Option.map(fun value->{value with ChoirKillTicks=(model.SimStepCount::value.ChoirKillTicks)|>List.truncate 3})
            else model.M5Boss
        let choirDefeated =
            boss
            |> Option.exists(fun value->value.Kind=Rogue3.Entities.BossKind.HollowChoir && value.ChoirKillTicks.Length=3 && not(Rogue3.Entities.choirRevives value.ChoirKillTicks))
        let room = if choirDefeated then Rogue3.Entities.bossCleared room.Reward room else {room with LiveEnemyIds=Set.union room.LiveEnemyIds childIds}
        let kills = model.RunStats.KillsByType |> Map.change actor.Kind (fun count->Some(1 + Option.defaultValue 0 count))
        let stats = addFloorDamage model.FloorIndex (max 0.0 (min actor.HitPoints damage)) 0.0 model.RunStats
        {model with M5Enemies=survivors@children
                    M5Boss=(if choirDefeated then None else boss);M5ChoirMemberIds=Set.remove enemyId model.M5ChoirMemberIds
                    M5Room=room
                    // Room-clear is durable floor state, so a later visit rebuilds the room already
                    // cleared and never rolls its clear drop again (§14.5, §14.15).
                    Floor=
                        (if choirDefeated then FloorGeneration.clearBoss model.Floor.CurrentRoom model.Floor else model.Floor)
                        |> fun floor -> if room.Cleared then FloorGeneration.recordRoomCleared model.Floor.CurrentRoom floor else floor
                    DropRng=rng;M5NextEntityId=model.M5NextEntityId+children.Length
                    RunStats={stats with KillsByType=kills}}

// Board item #47 moved `purchaseM5ShopSlot` DOWN, next to `applyFloorPickup` and `grantItem`. It
// stood here, above both, and could therefore hand over neither the item nor the consumable it had
// just charged for. The move is the fix's precondition, not a tidy-up.

let damageM5Obstacle obstacleId damage model =
    match model.M5Obstacles|>List.tryFind(fun obstacle->obstacle.Id=obstacleId) with
    | None -> model
    | Some obstacle ->
        let remaining,drop,rng=Rogue3.Entities.destroyObstacle damage model.DropRng obstacle
        let obstacles=model.M5Obstacles|>List.filter(fun value->value.Id<>obstacleId) |> fun others->remaining|>Option.map(fun value->value::others)|>Option.defaultValue others
        let floor=if remaining.IsNone then FloorGeneration.recordDestroyedObstacle model.Floor.CurrentRoom obstacleId model.Floor else model.Floor
        {model with M5Obstacles=obstacles;DropRng=rng;Floor=floor;M5ObstacleDrops=drop|>Option.map(fun value->model.M5ObstacleDrops@[{Id=obstacle.Id;Room=model.Floor.CurrentRoom;Kind=value;Position=obstacle.Position}])|>Option.defaultValue model.M5ObstacleDrops}

let private stepM5Entities model =
    // §14.21 — "a dead actor emits no later attack". Resolve deaths from the ACTOR list rather than
    // from the legacy `Enemies` projection: shot resolution drops zero-hit-point entries from
    // `Enemies` at the start of the next step, so an actor that reached zero could survive in
    // `M5Enemies` unseen by this cleanup and keep taking turns. Sorted by id so the drop-stream draw
    // order for simultaneous deaths is unchanged.
    let model =
        model.M5Enemies
        |> List.filter(fun actor->actor.HitPoints<=0.0)
        |> List.map _.Id
        |> List.sort
        |> List.fold(fun current enemyId->damageM5Enemy enemyId Double.MaxValue current) model
    let mutable rng = model.DropRng
    let mutable actionsByActor : (Vec2 * Rogue3.Entities.EnemyAction list) list = []
    let stepped =
        model.M5Enemies
        |> List.sortBy (fun enemy -> enemy.Id)
        |> List.map (fun enemy ->
            let result =
                Rogue3.Entities.stepEnemy
                    { FloorIndex=model.FloorIndex; Player=model.PlayerPosition
                      WallHit=(enemy.Position.Vx<=60.0 || enemy.Position.Vx>=1220.0 || enemy.Position.Vy<=60.0 || enemy.Position.Vy>=660.0)
                      PlayerHit=(magnitude(sub enemy.Position model.PlayerPosition)<=29.0); DropRng=rng }
                    enemy
            let result =
                let p=result.Actor.Position
                if p.Vx<60.0 || p.Vx>1220.0 || p.Vy<60.0 || p.Vy>660.0 then
                    {result with Actor={result.Actor with Position=clamp (vec2 60. 60.) (vec2 1220. 660.) p;State=Rogue3.Entities.EnemyState.ChargerRecover(Rogue3.Entities.ticks 0.7)}}
                else result
            let movement = if enemy.Kind=Rogue3.Entities.EnemyKind.Fly then Rogue3.Entities.MovementClass.Flying else Rogue3.Entities.MovementClass.Grounded
            let blocked =
                model.M5Obstacles
                |> List.exists(fun obstacle->Rogue3.Entities.blocksMovement movement obstacle.Kind && circlesOverlap result.Actor.Position (Rogue3.Entities.definition enemy.Kind).Radius obstacle.Position 20.0)
            let result =
                if blocked then
                    let state =
                        match result.Actor.State with
                        | Rogue3.Entities.EnemyState.ChargerDash _ -> Rogue3.Entities.EnemyState.ChargerRecover(Rogue3.Entities.ticks 0.7)
                        | other -> other
                    {result with Actor={result.Actor with Position=enemy.Position;State=state}}
                else result
            rng <- result.DropRng
            actionsByActor <- (result.Actor.Position,result.Actions)::actionsByActor
            result.Actor)
    let mutable nextBullet=model.M5NextBulletId
    let createBullet position direction speed damage homing radius =
        let bullet={Id=nextBullet;Position=position;Velocity=scale speed (normalizeOrZero direction);Radius=radius;Damage=damage;Homing=homing;AgeTicks=0}
        nextBullet<-nextBullet+1
        bullet
    let radial position count arc offset speed damage homing radius gap =
        [for index in 0..count-1 do
            if gap<>Some index then
                let angle = if count<=1 then offset else offset - arc/2.0 + float index*(arc/float count)
                let radians=angle*Math.PI/180.0
                yield createBullet position (vec2 (cos radians) (sin radians)) speed damage homing radius]
    let bullets =
        [ for position,actions in List.rev actionsByActor do
            for action in actions do
                match action with
                | Rogue3.Entities.EnemyAction.FireAimed speed -> yield createBullet position (sub model.PlayerPosition position) speed 1 0.0 3.0
                | Rogue3.Entities.EnemyAction.FireBurst(count,offset,speed) -> yield! radial position count 360.0 offset speed 1 0.0 3.0 None
                | Rogue3.Entities.EnemyAction.FireRing(count,speed) -> yield! radial position count 360.0 0.0 speed 1 0.0 3.0 None
                | _ -> () ]
    let bossResult=
        model.M5Boss
        |> Option.map(Rogue3.Entities.stepBoss model.PlayerPosition (model.SimStepCount+1))
        |> Option.map(fun result->
            let charge=result.Actions|>List.tryPick(function Rogue3.Entities.BossAction.Charge direction->Some direction|_->None)
            match charge with None->result | Some direction->{result with Boss={result.Boss with Position=add result.Boss.Position (scale (320.0*fixedDt) direction)}})
    let bossSpawned =
        match bossResult with
        | None -> []
        | Some result ->
            let maggotCount=result.Actions|>List.sumBy(function Rogue3.Entities.BossAction.SpawnMaggots count->count|_->0)
            let reviveCount=if result.Actions|>List.contains Rogue3.Entities.BossAction.ReviveChoir then 3 else 0
            [ for index in 0..maggotCount-1 ->
                Rogue3.Entities.spawn model.FloorIndex (model.M5NextEntityId+index) Rogue3.Entities.EnemyKind.Maggot (add result.Boss.Position (vec2 (float(index*28-14)) 0.))
              for index in 0..reviveCount-1 ->
                Rogue3.Entities.spawn model.FloorIndex (model.M5NextEntityId+maggotCount+index) Rogue3.Entities.EnemyKind.Caster (add result.Boss.Position (vec2 (float(index*120-120)) 120.)) ]
    let revivedChoirIds =
        bossSpawned
        |> List.filter(fun actor->actor.Kind=Rogue3.Entities.EnemyKind.Caster && bossResult|>Option.exists(fun result->result.Actions|>List.contains Rogue3.Entities.BossAction.ReviveChoir))
        |> List.map _.Id |> Set.ofList
    let bossBullets =
        match bossResult with
        | Some result ->
            [for action in result.Actions do
                match action with
                | Rogue3.Entities.BossAction.Emit(pattern,offset) ->
                    yield! radial result.Boss.Position pattern.Count pattern.ArcDegrees offset (pattern.Speed*Rogue3.Entities.bulletSpeedScale model.FloorIndex) (if result.Boss.Phase>=3 then 2 else 1) pattern.Homing 4.0 pattern.GapIndex
                | _ -> ()]
        | None -> []
    let nextModel =
        { model with
            M5Enemies=stepped@bossSpawned
            M5Boss=bossResult|>Option.map _.Boss
            M5ChoirMemberIds=Set.union model.M5ChoirMemberIds revivedChoirIds
            DropRng=rng
            M5AiDecisions=model.M5AiDecisions+stepped.Length
            M5BulletEmissions=model.M5BulletEmissions+bullets.Length+bossBullets.Length
            M5BossBulletEmissions=model.M5BossBulletEmissions+bossBullets.Length
            M5BossPatternEmissions=model.M5BossPatternEmissions+(bossResult|>Option.map(fun result->result.Actions|>List.sumBy(function Rogue3.Entities.BossAction.Emit _->1|_->0))|>Option.defaultValue 0)
            M5NextBulletId=nextBullet
            M5NextEntityId=model.M5NextEntityId+bossSpawned.Length
            M5Room={model.M5Room with LiveEnemyIds=Set.union model.M5Room.LiveEnemyIds (bossSpawned|>List.map _.Id|>Set.ofList)}
            EnemyBullets=bullets@bossBullets@model.EnemyBullets }
    let enemyShocks =
        actionsByActor
        |> List.collect(fun(position,actions)->actions|>List.choose(function Rogue3.Entities.EnemyAction.Shockwave(radius,_,damage,_)->Some(position,radius,damage)|_->None))
    let bossShocks =
        bossResult
        |> Option.toList
        |> List.collect(fun result->result.Actions|>List.choose(function Rogue3.Entities.BossAction.GroundPound(radius,_,damage,_)->Some(result.Boss.Position,radius,damage)|_->None))
    (nextModel,enemyShocks@bossShocks)
    ||> List.fold(fun current (source,radius,damage)->if magnitude(sub current.PlayerPosition source)<=radius then takePlayerHit damage source current else current)

/// Apply one collected floor pickup. Currency goes through the shared 99 cap; hearts go through the
/// same `healRed`/`addTemporaryHearts` the shop and the post-boss heal use, so a pickup can never
/// exceed a container total or the 24-half-heart display cap by a different route.
let applyFloorPickup (kind: Rogue3.Entities.PickupKind) model =
    match kind with
    // The run stat records what was actually BANKED, not the face value. Crediting the face value at
    // the 99 cap inflated `RunStats.CoinsCollected`, which feeds `runScore` -- a coin that overflowed
    // the cap is waste, and the product treats cap overflow as waste everywhere else.
    | Rogue3.Entities.PickupKind.Coin1
    | Rogue3.Entities.PickupKind.Coin3 ->
        let face = if kind = Rogue3.Entities.PickupKind.Coin3 then 3 else 1
        let banked = addCurrency face model.PlayerCurrency.Coins
        { model with PlayerCurrency = { model.PlayerCurrency with Coins = banked }
                     RunStats = { model.RunStats with CoinsCollected = model.RunStats.CoinsCollected + (banked - model.PlayerCurrency.Coins) } }
    | Rogue3.Entities.PickupKind.Key ->
        { model with PlayerCurrency = { model.PlayerCurrency with Keys = addCurrency 1 model.PlayerCurrency.Keys } }
    | Rogue3.Entities.PickupKind.Bomb ->
        { model with PlayerCurrency = { model.PlayerCurrency with Bombs = addCurrency 1 model.PlayerCurrency.Bombs } }
    | Rogue3.Entities.PickupKind.HalfRedHeart ->
        { model with PlayerHealth = healRed 1 model.PlayerHealth }
    | Rogue3.Entities.PickupKind.SoulHeart ->
        { model with PlayerHealth = addTemporaryHearts 2 0 model.PlayerHealth }
    | Rogue3.Entities.PickupKind.Nothing -> model

// ------------------------------------------------------------------------------------------------
// Board item #47 — turning a generated item into a player item.
//
// `Rogue3.Entities.ItemDefinition` is CONTENT: an id, a quality tier and pool tags. `StatModifier`
// is PLAYER state. Nothing in the product joined the two, so every route that "awarded an item"
// awarded a value with no destination: the shop debited coins and dropped the offer on the floor,
// the treasure pedestal was discarded at room load, and the boss reward was drawn but not takeable.
//
// The mapping lives in `Model` rather than on `ItemDefinition` because `Entities` is compiled BEFORE
// `Model` (see Rogue3.fsproj) and cannot name `Stat`. Moving `Stat`/`StatModifier` down into
// `Entities` would re-point every stat call site in the product for no gain; a total function from
// content to modifiers is the same contract with a smaller blast radius.
// ------------------------------------------------------------------------------------------------

// NOT named `add`/`mul`: `Vec2.add` is in scope for the whole of this file and a plain `add` here
// would shadow it for every later definition.
let private addMod stat value = { Stat = stat; Kind = Add; Value = value }
let private mulMod stat value = { Stat = stat; Kind = Mul; Value = value }

/// What an item DOES. Total by construction: an id with no authored entry still resolves to a
/// quality-scaled damage bonus, so an item added to the pool later can never be a silent no-op —
/// which is the exact failure this item exists to close, one level down.
///
/// `TearDelayStat` is INVERTED: `recomputePlayerStats` derives `FireRate = 30 / tearDelay`, so a
/// NEGATIVE tear-delay modifier fires faster and a positive one fires slower.
let itemModifiers (item: Rogue3.Entities.ItemDefinition) : StatModifier list =
    match item.Id with
    | "coal-heart" -> [ addMod DamageStat 1.0; addMod KnockbackStat 10.0 ]
    | "cracked-lens" -> [ addMod MultishotStat 1.0; mulMod DamageStat -0.25 ]
    | "iron-teeth" -> [ addMod DamageStat 2.0; addMod TearDelayStat 2.0 ]
    | "void-map" -> [ addMod RangeStat 0.5; addMod ShotSpeedStat 60.0 ]
    | "maggot-crown" -> [ addMod PierceStat 1.0; addMod ShotRadiusStat 2.0 ]
    | "choir-bell" -> [ addMod TearDelayStat -2.0; addMod HomingStat 0.3 ]
    | _ -> [ addMod DamageStat (0.5 + 0.5 * float (max 0 item.Quality)) ]

/// The item a generated definition becomes once a player owns it.
let playerItemOf (item: Rogue3.Entities.ItemDefinition) : PlayerItem =
    { Id = item.Id; Modifiers = itemModifiers item }

/// THE ONLY WRITER OF `Model.PlayerItems`, and the only place `RunStats.ItemsFound` is incremented.
///
/// Both fields move together, in one expression, so the invariant the board item asks for —
/// `RunStats.ItemsFound` can never disagree with `PlayerItems.Length` for items acquired in a run —
/// holds because every acquisition goes through here, not because the type system forbids the
/// alternative. Be precise about that: `git grep -n "PlayerItems *=" -- src` and the same for
/// `ItemsFound *=` each return exactly two hits, this function and the boot model, and the invariant
/// rests on that staying true. A future `{ model with PlayerItems = … }` elsewhere would break it
/// silently. Deriving `ItemsFound` from `PlayerItems.Length` at read time would make it structural;
/// that is not done here only because `RunStats` is snapshotted whole into the run summary.
///
/// Stats are recomputed from the WHOLE list, never patched incrementally, so acquisition order
/// cannot drift the result.
///
/// The `ItemGranted` audio event is drained only on `Tick` (`AudioCues.forTransition`), and
/// `advanceSim` clears `AudioEvents` at the head of each tick — so on a DIRECT dispatch of
/// `InteractM5Shop` this event is discarded unplayed, which is why `m5ShopPickupCues` still cues the
/// item there. On the production route (#55) the purchase happens inside the tick and the event
/// survives to its end, so that cue steps aside instead.
let grantItem (item: Rogue3.Entities.ItemDefinition) model =
    let items = model.PlayerItems @ [ playerItemOf item ]
    { model with
        PlayerItems = items
        PlayerStats = recomputePlayerStats items
        RunStats = { model.RunStats with ItemsFound = model.RunStats.ItemsFound + 1 }
        AudioEvents = model.AudioEvents @ [ AudioEvent.ItemGranted ] }

/// Write the shop's remaining stock back into durable FLOOR state.
///
/// Board item #55, and not optional once a player can actually buy. `loadM5Room` reads a room's
/// stock out of `FloorGeneration.ShopStock` every time the room is entered, and nothing used to
/// write the emptied offers back — so leaving a shop and returning restored every slot it had sold.
/// While `InteractM5Shop` had no production dispatch site that was unreachable; wiring the interact
/// route makes it an unbounded item engine, so the two land together. The rule is exactly
/// `withoutRewardFixture`'s and `FloorGeneration.recordDestroyedObstacle`'s (§14.15): what the player
/// took comes off the floor record in the same step it comes off the plinth.
let private withShopStock roomId (slots: Rogue3.Entities.ShopSlot list) (floor: FloorGeneration.Floor) =
    match Map.tryFind roomId floor.Rooms with
    | None -> floor
    | Some room ->
        // REWRITE ONLY, never insert. A room with no `ShopStock` fixture keeps none, because
        // `loadM5Room` reads stock straight out of this list and a room the generator never furnished
        // must not start presenting one. `List.map` gives that for free — an added `List.exists`
        // guard in front of it changed nothing observable, which is how it was found and removed.
        let fixtures =
            room.Fixtures
            |> List.map (function
                | FloorGeneration.ShopStock _ -> FloorGeneration.ShopStock slots
                | other -> other)
        { floor with Rooms = Map.add roomId { room with Fixtures = fixtures } floor.Rooms }

/// Buy a shop slot: pay for it, AND receive what was paid for.
///
/// Board item #47: this used to debit currency, empty the offer and bump `ItemsFound`, and stop
/// there — for BOTH offer kinds. An `Item` offer granted no item and recomputed no stat; a
/// `Consumable` offer granted no heart, key or bomb either, and did not even bump a counter. The
/// consumable half routes through the SAME `applyFloorPickup` a floor drop uses, so a bought heart
/// obeys the container cap and a bought coin obeys the 99 cap by the one shared rule.
let purchaseM5ShopSlot slotId model =
    match model.M5ShopSlots|>List.tryFind(fun slot->slot.Id=slotId) with
    | None -> model
    | Some slot ->
        let coins,keys,updated,ok=Rogue3.Entities.purchase model.PlayerCurrency.Coins model.PlayerCurrency.Keys slot
        if not ok then model else
        let remaining = model.M5ShopSlots|>List.map(fun item->if item.Id=slotId then updated else item)
        let paid =
            {model with PlayerCurrency={model.PlayerCurrency with Coins=coins;Keys=keys}
                        M5ShopSlots=remaining
                        Floor=withShopStock model.Floor.CurrentRoom remaining model.Floor}
        match slot.Offer with
        | Rogue3.Entities.ShopOffer.Item item -> grantItem item paid
        | Rogue3.Entities.ShopOffer.Consumable kind ->
            // BOUGHT, not FOUND. `applyFloorPickup` credits `RunStats.CoinsCollected` because a coin
            // lying on the floor is income, and `runScore` pays `CoinsCollected * 5` for it. A coin
            // the player just PAID for is not income: the shop's pool-exhausted fallback offer is
            // `Coin3` priced at 3, so crediting it would mint 15 score per slot for a net-zero
            // transaction. Take the effect, leave the collection accounting where it was.
            let stocked = applyFloorPickup kind paid
            { stocked with RunStats = { stocked.RunStats with CoinsCollected = paid.RunStats.CoinsCollected } }
        | Rogue3.Entities.ShopOffer.Empty -> paid

/// M13: walk onto a pickup and it is yours.
///
/// Before this, `M5ObstacleDrops` was write-only: smashing a pot appended a `PickupKind` that the
/// renderer drew in a fixed row and no reducer ever consumed, so the drop table, the drop RNG stream
/// and the drop visual all existed for a reward a player could not take. The scan is one circle test
/// per live pickup, counted into `TotalFloorPickupCandidates` so its cost is measured rather than
/// argued, and a collected pickup is removed in the same step it is applied — so a player standing
/// still on the spot collects exactly once.
let collectFloorPickups (model: Model) =
    let here = floorPickupsHere model
    if List.isEmpty here then model
    else
        let taken =
            here
            |> List.filter (fun pickup -> circlesOverlap model.PlayerPosition playerRadius pickup.Position floorPickupRadius)
        let takenIds = taken |> List.map _.Id |> Set.ofList
        let scanned =
            { model with
                M5ObstacleDrops =
                    model.M5ObstacleDrops
                    |> List.filter (fun pickup -> not (pickup.Room = model.Floor.CurrentRoom && Set.contains pickup.Id takenIds))
                // Only the pickups in THIS room are tested, which is what the driver's ScaleSource says.
                TotalFloorPickupCandidates = model.TotalFloorPickupCandidates + here.Length }
        (scanned, taken) ||> List.fold (fun current pickup -> applyFloorPickup pickup.Kind current)

// ------------------------------------------------------------------------------------------------
// Board item #47 — taking the room's reward.
//
// The treasure pedestal and the boss reward reach the player by the same route M13 gave the obstacle
// drop: walk onto it. Before this they had NO route at all — `M5Room.Reward` was rendered and never
// consumed, the exact "write-only reward a player cannot take" shape `collectFloorPickups` above was
// written to close for pots.
// ------------------------------------------------------------------------------------------------

/// How close the player's centre must come to the reward plinth's centre to take it. Wider than
/// `floorPickupRadius` because the plinth is a drawn 26-unit-tall fixture, not a loose coin.
let roomRewardRadius = 20.0

/// Where the room's reward stands.
///
/// The renderer places the reward at the fixture slot AFTER the shop stock
/// (`Render.renderedElementsIn`), so collection is tested at the point the player can actually see.
/// Both sides derive from `placeRoomFixtures`; `M13RoomTransitionWorldStateTests` pins the renderer
/// to it and `M14ItemGrantTests` pins this function to it, so the two cannot drift apart silently.
let roomRewardPosition (model: Model) =
    placeRoomFixtures model.M5Obstacles (model.M5ShopSlots.Length + 1)
    |> List.tryItem model.M5ShopSlots.Length

/// A pedestal may be taken on sight. A BOSS reward may not be taken until the boss is down —
/// otherwise a player walks past the sealed-in boss, grabs the prize and leaves.
let roomRewardCollectable (model: Model) =
    model.M5Room.Reward.IsSome && (not model.M5Room.IsBoss || model.M5Room.Cleared)

/// Drop the reward fixture from durable floor state, so re-entering the room does not re-grant it.
/// The same durability rule as `FloorGeneration.recordDestroyedObstacle` (§14.15).
let private withoutRewardFixture roomId (floor: FloorGeneration.Floor) =
    match Map.tryFind roomId floor.Rooms with
    | None -> floor
    | Some room ->
        let fixtures =
            room.Fixtures
            |> List.filter (function
                | FloorGeneration.ItemPedestal _
                | FloorGeneration.BossReward _ -> false
                | _ -> true)
        { floor with Rooms = Map.add roomId { room with Fixtures = fixtures } floor.Rooms }

/// Walk onto the pedestal and the item is yours: appended to `PlayerItems`, counted once, stats
/// recomputed, and removed from BOTH the live room and the floor record in the same step — so a
/// player standing still on the plinth collects exactly once, and so does one who leaves and returns.
let collectRoomReward (model: Model) =
    match model.M5Room.Reward with
    | Some reward when roomRewardCollectable model ->
        match roomRewardPosition model with
        | Some at when circlesOverlap model.PlayerPosition playerRadius at roomRewardRadius ->
            { model with
                M5Room = { model.M5Room with Reward = None }
                Floor = withoutRewardFixture model.Floor.CurrentRoom model.Floor }
            |> grantItem reward
        | _ -> model
    | _ -> model

// ------------------------------------------------------------------------------------------------
// Board item #55 — standing at the stock and pressing interact.
//
// `#47` made `purchaseM5ShopSlot` hand over what it charges for. It did not give a player any way to
// ASK for it: `InteractM5Shop` had a declaration, a handler and an audio cue, and zero production
// constructions. This is the same class of defect as M11's doorway (`TraverseDoor` reachable only
// from a test), M12's cue ids, and the trapdoor descent — a correct reducer behind a door nobody can
// open — so the fix is deliberately the SAME SHAPE as the descent below it in `playerRoomIntentsIn`:
// a proximity predicate over the placed fixture, an interact edge, and a raised production `Msg`.
// It RAISES `InteractM5Shop`; it does not re-implement the purchase.
// ------------------------------------------------------------------------------------------------

/// How close the player's centre must come to a shop plinth's centre to be AT that slot.
///
/// The same distance as `roomRewardRadius`, and by construction rather than by coincidence: shop
/// stock and the reward pedestal are the same drawn plinth, placed by the same `placeRoomFixtures`
/// call, so two different reach distances would mean two fixtures that look identical behave
/// differently under the player's feet.
let shopSlotRadius = roomRewardRadius

/// Where this room's shop stock stands, in slot order.
///
/// `Render.renderedElementsIn` places slot `index` at `placeRoomFixtures model.M5Obstacles n |> item
/// index` for an `n` that also counts the reward plinth. `placeRoomFixtures` is a PREFIX function —
/// it accepts candidates in order and takes the first `count` — so the first `M5ShopSlots.Length`
/// positions are the same list whether or not a reward is also being placed. Asking for exactly the
/// slot count here therefore yields the renderer's own positions, and `M14ItemGrantTests` pins that
/// against `Render.renderedElements` rather than against a restatement of this formula.
let shopSlotPositions (model: Model) =
    placeRoomFixtures model.M5Obstacles model.M5ShopSlots.Length

/// True when `slot` would actually be sold to this player right now.
///
/// Delegated to `Entities.purchase` rather than restated. The affordability rule is three clauses
/// (empty offer, key-locked without a key, priced above the purse) and a second copy of it is a
/// second thing to keep in step — the renderer would promise a purchase the reducer then refuses.
let shopSlotAffordable (model: Model) (slot: Rogue3.Entities.ShopSlot) =
    let _, _, _, ok = Rogue3.Entities.purchase model.PlayerCurrency.Coins model.PlayerCurrency.Keys slot
    ok

/// The stocked shop slot the player is standing at, with the position it is drawn at.
///
/// An EMPTIED slot is skipped: a bare plinth is not something to press interact at, and letting it
/// answer here would shadow a stocked neighbour placed close by. Nearest-first, so two slots whose
/// reach circles overlap resolve to the one the player is actually closest to rather than to
/// whichever happens to come first in `M5ShopSlots`.
let shopSlotUnderPlayer (model: Model) : (Rogue3.Entities.ShopSlot * Vec2) option =
    if List.isEmpty model.M5ShopSlots then None
    else
        let positions = shopSlotPositions model
        // `List.tryItem`, not `List.zip`: the renderer indexes the placement list the same way, and a
        // total lookup here cannot turn a placement shortfall into an exception inside a fixed step.
        model.M5ShopSlots
        |> List.indexed
        |> List.choose (fun (index, slot) -> positions |> List.tryItem index |> Option.map (fun at -> slot, at))
        |> List.filter (fun (slot, _) -> slot.Offer <> Rogue3.Entities.ShopOffer.Empty)
        |> List.filter (fun (_, at) -> circlesOverlap model.PlayerPosition playerRadius at shopSlotRadius)
        |> List.sortBy (fun (_, at) -> magnitude (sub model.PlayerPosition at))
        |> List.tryHead

let private stepInput pressedThisTick (model: Model) =
    let resolved = resolveInput model.PlayerPosition pressedThisTick model.Input.Current
    let commandPressed command = Set.contains command model.Input.Current.Commands && not(Set.contains command model.Input.Previous.Commands)
    let dodgeStarted = model.PlayerLifeState = Alive && (Set.contains dodgeKey pressedThisTick || commandPressed "dodge") && model.DodgeCooldownTicks = 0
    let moveSpeed = effectiveMoveSpeed model.PlayerStats
    let targetVelocity = scale moveSpeed resolved.Move
    let rate = if resolved.Move = zero then playerFriction else playerAcceleration
    let controlDelta =
        if model.DodgeRollTicks > 0 then rollSpeed * fixedDt / 0.45
        else rate * fixedDt
    let controlledVelocity =
        approachVector controlDelta targetVelocity model.PlayerVelocity
        |> fun velocity -> if model.DodgeRollTicks > 0 then velocity else clampMagnitude moveSpeed velocity
    let rollDirection = if resolved.Move <> zero then resolved.Move else normalizeOrZero model.Facing
    let playerVelocity = if dodgeStarted then scale rollSpeed rollDirection else controlledVelocity
    let displacement = scale fixedDt playerVelocity
    let playerCircle = { Center = toSimPoint model.PlayerPosition; Radius = playerRadius }
    let roomBounds: Rect = { X = 0.0; Y = 0.0; Width = model.Playfield.Vx; Height = model.Playfield.Vy }
    // M13: the drawn stone band is a collider. `roomWallSlabs` is the SAME value `Render.roomWallsScene`
    // fills, so the wall a player can see is the wall a player stops at. Only the PLAYER sweeps them —
    // shots keep their existing `shotWalls` set and their playfield bounce, so no projectile behaviour
    // moves with this change.
    let wallSlabs = roomWallSlabs model
    // Board item #20: derived here, once per step, rather than read from a stored `Obstacles` cache
    // four reducers had to remember to refresh. Same elements, same order, no staleness possible.
    let obstacleRects = blockingObstacleRects model.M5Obstacles
    let movedPlayer = Collision.sweepCircle (Some roomBounds) (wallSlabs @ obstacleRects) playerCircle (toSimPoint displacement)
    let playerPosition = ofSimPoint movedPlayer.Center
    let fireAim = if resolved.Aim = zero then normalizeOrZero model.Facing else resolved.Aim
    let iFramesActive = dodgeStarted || model.DodgeIFrameTicks > 0
    let cadence = 1.0 / (model.PlayerStats.FireRate |> max 0.7 |> min 15.0)

    let shouldSpawn, nextCooldown =
        if model.PlayerLifeState = Dead || iFramesActive || not resolved.FireHeld || fireAim = zero then
            false, 0.0
        elif not model.WasFiring then
            true, max 0.0 (cadence - fixedDt)
        elif model.FireCooldown <= fixedDt + 1e-12 then
            true, max 0.0 (model.FireCooldown + cadence - fixedDt)
        else
            false, model.FireCooldown - fixedDt

    let spawned =
        if shouldSpawn then spawnShots (model.SimStepCount + 1) model.NextShotId playerPosition playerVelocity fireAim model.PlayerStats
        else []
    let shotSpawns =
        spawned @ model.ShotSpawns
        |> List.truncate maxShotSpawnHistory

    let shotPassThrough =
        model.M5Obstacles
        |> List.filter(fun obstacle->not(Rogue3.Entities.blocksShots obstacle.Kind))
        |> List.map(fun obstacle->toSimRect obstacle.Position obstacleExtent obstacleExtent)
        |> Set.ofList
    let shotWalls=obstacleRects|>List.filter(fun wall->not(Set.contains wall shotPassThrough))
    let steppedShots, wallQueries, homingQueries =
        stepShots roomBounds shotWalls model.HomingTargets shotSpawns

    let bombPressed = Set.contains qKey pressedThisTick || Set.contains fKey pressedThisTick || commandPressed "bomb"
    let bombs, currency, nextBombId =
        if bombPressed && model.PlayerCurrency.Bombs > 0 && model.PlayerLifeState = Alive then
            { Id = model.NextBombId; Position = playerPosition; FuseTicks = bombFuseTicks } :: model.Bombs,
            { model.PlayerCurrency with Bombs = model.PlayerCurrency.Bombs - 1 },
            model.NextBombId + 1
        else model.Bombs, model.PlayerCurrency, model.NextBombId

    let steppedModel =
        { model with
            PlayerPosition = playerPosition
            PlayerVelocity = playerVelocity
            Facing = if resolved.Aim = zero then model.Facing else resolved.Aim
            LastResolvedInput = resolved
            FireCooldown = nextCooldown
            WasFiring = not iFramesActive && resolved.FireHeld && fireAim <> zero
            ShotSpawns = steppedShots
            TotalShotSpawns = model.TotalShotSpawns + spawned.Length
            Bombs = bombs
            PlayerCurrency = currency
            NextBombId = nextBombId
            NextShotId = model.NextShotId + spawned.Length
            DodgeRollTicks = if dodgeStarted then rollDurationTicks - 1 else max 0 (model.DodgeRollTicks - 1)
            DodgeIFrameTicks = if dodgeStarted then dodgeIFrameTicks else model.DodgeIFrameTicks
            DodgeCooldownTicks = if dodgeStarted then dodgeCooldownTicks - 1 else max 0 (model.DodgeCooldownTicks - 1)
            PostHitInvulnTicks = max 0 (model.PostHitInvulnTicks - 1)
            // Each player axis performs one swept cast, then slideCircle's X and Y contact folds.
            // M13 adds the room's own wall slabs to that sweep under the SAME `6 *` accounting, so the
            // counter still describes the casts the player actually performs.
            TotalWallQueries = model.TotalWallQueries + wallQueries + 6 * (obstacleRects.Length + wallSlabs.Length)
            TotalHomingQueries = model.TotalHomingQueries + homingQueries
            EdgeActionCount = model.EdgeActionCount + Set.count pressedThisTick
            AudioEvents =
                model.AudioEvents
                @ (if shouldSpawn then [ AudioEvent.ShotFired ] else [])
                @ (if dodgeStarted then [ AudioEvent.DodgeRolled ] else []) }
    let spiked =
        model.M5Obstacles
        |> List.filter(fun obstacle->Rogue3.Entities.spikeDamage obstacle.Kind>0 && circlesOverlap steppedModel.PlayerPosition playerRadius obstacle.Position 20.0)
        |> List.fold(fun current obstacle->takePlayerHit (Rogue3.Entities.spikeDamage obstacle.Kind) obstacle.Position current) steppedModel
    collectFloorPickups spiked |> collectRoomReward

// Pure fixed step: integrate the ball by one step, bounce off the top/bottom walls and the paddles,
// score and re-serve on a miss. Positions/velocities are `Vec2`, advanced with `add`/`scale`; the
// ball always stays inside the playfield after the step. This is your `stepSim` — edit it freely.
let m6MaxParticles = 600
let m6CameraDurationTicks = 42 // 0.35 s * 120 Hz

let private stepM6Presentation model =
    let particles =
        model.M6Particles
        |> List.choose (fun particle ->
            let age = particle.AgeTicks + 1
            if age >= particle.LifetimeTicks then None
            else
                Some
                    { particle with
                        Position = add particle.Position (scale fixedDt particle.Velocity)
                        AgeTicks = age })
    let transition =
        model.M6CameraTransition
        |> Option.bind (fun camera ->
            let elapsed = camera.ElapsedTicks + 1
            if elapsed >= m6CameraDurationTicks then None
            else Some { camera with ElapsedTicks = elapsed })
    { model with M6Particles = particles; M6CameraTransition = transition }

let private spawnM6Particles count origin tint model =
    let requested = max 0 count
    let spawned =
        [ for offset in 0 .. requested - 1 do
              let id = model.M6NextParticleId + offset
              let angle = float (id % 16) * Math.PI / 8.0
              let speed = 40.0 + float (id % 5) * 12.0
              yield
                  { Id = id
                    Position = origin
                    Velocity = vec2 (cos angle * speed) (sin angle * speed)
                    LifetimeTicks = 60 + id % 60
                    AgeTicks = 0
                    Radius = 2.0 + float (id % 3)
                    Shape = if id % 2 = 0 then ParticleShape.Circle else ParticleShape.Quad
                    Tint = tint } ]
    { model with
        M6Particles = (model.M6Particles @ spawned) |> List.rev |> List.truncate m6MaxParticles |> List.rev
        M6NextParticleId = model.M6NextParticleId + requested }

let private stepSimWithInput pressedThisTick model =
    let model = stepInput pressedThisTick model |> resolveCombat |> stepM5Entities |> stepM6Presentation
    let model = { model with DodgeIFrameTicks = max 0 (model.DodgeIFrameTicks - 1) }
    let ball = model.Ball
    let next = add ball.Pos ball.Velocity // one unit step (dt folded into velocity units)

    let velocityY, clampedY =
        if next.Vy < ballRadius then -ball.Velocity.Vy, ballRadius
        elif next.Vy > model.Playfield.Vy - ballRadius then -ball.Velocity.Vy, model.Playfield.Vy - ballRadius
        else ball.Velocity.Vy, next.Vy

    let withinLeftPaddle = clampedY >= model.LeftPaddleY && clampedY <= model.LeftPaddleY + model.PaddleHeight
    let withinRightPaddle = clampedY >= model.RightPaddleY && clampedY <= model.RightPaddleY + model.PaddleHeight

    let stepped =
      if next.Vx < leftPaddleX + paddleThickness + ballRadius then
        if withinLeftPaddle then
            { model with
                Ball =
                    { Pos = vec2 (leftPaddleX + paddleThickness + ballRadius) clampedY
                      Velocity = vec2 (abs ball.Velocity.Vx) velocityY } }
        else
            { model with
                RightScore = model.RightScore + 1
                Ball = servedBall }
      elif next.Vx > rightPaddleX - ballRadius then
        if withinRightPaddle then
            { model with
                Ball =
                    { Pos = vec2 (rightPaddleX - ballRadius) clampedY
                      Velocity = vec2 (-(abs ball.Velocity.Vx)) velocityY } }
        else
            { model with
                LeftScore = model.LeftScore + 1
                Ball = servedBall }
      else
        { model with
            Ball =
                { ball with
                    Pos = vec2 next.Vx clampedY
                    Velocity = vec2 ball.Velocity.Vx velocityY } }

    { stepped with SimStepCount = model.SimStepCount + 1 }

let stepSim model = stepSimWithInput Set.empty model

/// True on the rising edge of the interact input, from either the raw `E` key or the rebindable
/// `active` command the shell routes.
///
/// `isFirstStep` is load-bearing. `advanceSim` rotates `Input.Previous` only AFTER the whole step
/// loop, so the command comparison stays true on every step of a multi-step host frame; the raw-key
/// arm is already gated because `advanceSim` passes an empty pressed-set after the first step. Both
/// arms must agree, or one host frame would raise the same edge up to five times.
let private interactPressed isFirstStep pressedThisTick (model: Model) =
    Set.contains eKey pressedThisTick
    || (isFirstStep
        && Set.contains "active" model.Input.Current.Commands
        && not (Set.contains "active" model.Input.Previous.Commands))

/// True when the current room's derived combat lock has sealed the doorway at `index`.
let private doorwaySealed index (model: Model) =
    match List.tryItem index model.M5Room.Doors with
    | None
    | Some Rogue3.Entities.DoorState.Open -> false
    | Some _ -> true

/// True when `roomId` records the trapdoor fixture AND the loaded room agrees.
let trapdoorPresent (model: Model) =
    model.M5Room.Trapdoor
    && (match Map.tryFind model.Floor.CurrentRoom model.Floor.Rooms with
        | Some room -> room.Fixtures |> List.contains FloorGeneration.Trapdoor
        | None -> false)

/// True when the player may descend: the room depicts a trapdoor and the player is standing on it.
let canDescend (model: Model) = trapdoorPresent model && trapdoorContains model.PlayerPosition

// ------------------------------------------------------------------------------------------------
// M11: the missing link. This is what turns "the player walked into a doorway" or "the player pressed
// interact on a trapdoor" into a production `Msg`.
//
// It RAISES the same messages a test or a journey raises — it does not re-implement the transition.
// `advanceSim` folds them through `update`, so there is exactly one traversal transition in the
// product, `Replay` needs no new entry kind, and a crossing replays from `KeyChanged` + `Tick` alone.
// ------------------------------------------------------------------------------------------------
let playerRoomIntentsIn isFirstStep pressedThisTick (model: Model) : Model * Msg list =
    let doors =
        match Map.tryFind model.Floor.CurrentRoom model.Floor.Rooms with
        | Some room -> room.Doors
        | None -> []

    let scanned = { model with TotalDoorSensorQueries = model.TotalDoorSensorQueries + doors.Length }

    let doorIntents =
        doors
        |> List.indexed
        |> List.tryFind (fun (_, door) -> doorwaySensorContains door.Direction model.PlayerPosition)
        |> Option.map (fun (index, door) ->
            if doorwaySealed index model then []
            else
                match door.State with
                | FloorGeneration.Open
                | FloorGeneration.BossDoor -> [ TraverseDoor door.ToRoom ]
                // Walking into a key door with a key spends it and opens the pair. Crossing happens on
                // a later step, because the traversal arm above only accepts an already-usable door.
                | FloorGeneration.LockedKey when model.PlayerCurrency.Keys > 0 -> [ UnlockDoor door.ToRoom ]
                | FloorGeneration.LockedKey
                | FloorGeneration.HiddenWall -> [])
        |> Option.defaultValue []

    // ONE interact edge per step, read once and shared. Reading it twice would be the same answer
    // today and a trap tomorrow: the two branches below must be mutually exclusive, and they can only
    // be reasoned about as exclusive if they are looking at the same event.
    let interacting = interactPressed isFirstStep pressedThisTick model

    // Board item #55. A shop slot is bought on INTERACT, not on walk-on. Walk-on is right for a coin
    // (`collectFloorPickups`) and for the reward plinth (`collectRoomReward`) because neither costs
    // anything; a slot debits the purse, so pathing across a shop must not bankrupt a player who
    // never asked to buy. The scan runs only on the interact edge, which is also why it needs no
    // `Total…Queries` counter of its own: it is not a per-tick cost the way the door sensor is.
    let shopAtPlayer = if interacting then shopSlotUnderPlayer model else None

    let shopIntents =
        shopAtPlayer |> Option.map (fun (slot, _) -> [ InteractM5Shop slot.Id ]) |> Option.defaultValue []

    /// True when the press the player just made will actually TRANSACT, rather than being refused.
    ///
    /// `shopSlotUnderPlayer` deliberately answers for a slot the player cannot afford — the prompt
    /// has to be able to say `NEED 13c`, which is half of this item's acceptance — so "a slot is
    /// under the player" and "this press buys something" are different questions, and only the
    /// second one may take the press away from another consumer of the same button.
    let purchaseTransacts =
        shopAtPlayer |> Option.map (fun (slot, _) -> shopSlotAffordable model slot) |> Option.defaultValue false

    let descentIntents =
        // `DescendFloor` replaces every room-local collection but does not load a room, so the route
        // follows it with the production room-entry message. Both are guarded reducers.
        //
        // A TRANSACTING SHOP PRESS WINS A TIE; A REFUSED ONE DOES NOT. `placementAccepts` rejects any
        // fixture position inside `trapdoorContains`, so stock is never placed ON the hatch — but
        // `shopSlotRadius` plus `playerRadius` reaches past the plinth, so one press can satisfy both
        // predicates at the margin. Descending is a one-way trip that abandons the room's remaining
        // stock; buying is local and repeatable, and the player can press again to descend. Resolving
        // the tie the other way would make a shop built beside a trapdoor unbuyable, which is the
        // defect this item exists to close.
        //
        // The gate is `purchaseTransacts` and NOT `List.isEmpty shopIntents`, which is what it said
        // first. A slot the player cannot afford is still sensed — it must be, or the refusal prompt
        // could not be drawn — so gating on the mere presence of a shop intent handed the press to a
        // purchase that then refused it and returned the model unchanged. Standing on a hatch beside
        // stock too expensive to buy, the player pressed interact forever and neither bought nor
        // descended: a SOFT-LOCK, and the comment above used to justify the gate with "the player can
        // press again to descend", which was false in exactly that case. A refused press now falls
        // through to the descent, so the button always does something.
        if interacting && not purchaseTransacts && canDescend model then [ DescendFloor; EnterM5Room 0 ]
        else []

    scanned, doorIntents @ shopIntents @ descentIntents

let playerRoomIntents pressedThisTick model = playerRoomIntentsIn true pressedThisTick model

// Fixed-timestep advance: fold the host's real elapsed `dt` into the carried accumulator, drain the
// whole number of `simInterval` steps out of it, and run `stepSim` that many times. `FixedStep.drain`
// is a pure FS.GG.Game.Core primitive (no wall-clock read), so a scripted `dt` sequence replays
// byte-identically. This is the accumulator + stepSim pattern — the shape most games want on Tick.
let private advanceSim (dispatch: Msg -> Model -> Model) dtSeconds (model: Model) =
    let struct (steps, accumulator) =
        FixedStep.drainWith maxFrameTime fixedDt dtSeconds model.SimAccumulator
    let currentKeys = Set.union model.Input.Current.Keys model.Input.Current.Gamepad.Buttons
    let previousKeys = Set.union model.Input.Previous.Keys model.Input.Previous.Gamepad.Buttons
    let pressedThisTick = Set.difference currentKeys previousKeys

    let stepped,executedSteps =
        // mutable: a single unaliased accumulator over a fixed step count is plainer than a fold here.
        let mutable m = { model with AudioEvents = [] }
        let mutable executed = 0
        let mutable terminalStep = false
        let hadFinalBoss = model.FloorIndex=6 && model.M5Boss.IsSome
        for stepIndex in 1..steps do
            if not terminalStep then
                let pressed = if stepIndex = 1 then pressedThisTick else Set.empty
                m <- stepSimWithInput pressed m
                // Apply this step's player intents BEFORE the next step, so a crossing relocates the
                // player immediately and the same doorway cannot fire twice inside one host frame.
                let scanned, intents = playerRoomIntentsIn (stepIndex = 1) pressed m
                m <- intents |> List.fold (fun state message -> dispatch message state) scanned
                executed <- executed+1
                terminalStep <-
                    (m.PlayerLifeState=Dead || totalHalfHearts m.PlayerHealth=0)
                    || (hadFinalBoss && m.M5Boss.IsNone)
        m,executed

    { stepped with
        SimAccumulator = accumulator
        TickCount = model.TickCount + 1
        RunStats =
            if model.RunActive then
                { stepped.RunStats with RunSeconds = stepped.RunStats.RunSeconds + float executedSteps * fixedDt }
            else stepped.RunStats
        FloorNameTicks = max 0 (stepped.FloorNameTicks - executedSteps)
        Input =
            if executedSteps = 0 then model.Input
            else
                { model.Input with
                    Previous = model.Input.Current
                    PressedThisTick = pressedThisTick } }

let runScore (stats:RunStats) =
    let kills=stats.KillsByType|>Map.values|>Seq.sum
    let noHitFloors=
        [1..max 0 stats.FloorsCleared]
        |> List.filter(fun floor->Map.tryFind floor stats.DamageByFloor|>Option.map snd|>Option.defaultValue 0.0<=0.0)
        |> List.length
    stats.FloorsCleared*1000 + stats.BossKills*2000 + kills*10 + stats.CoinsCollected*5
    + stats.ItemsFound*250 + max 0 (30000-int(floor stats.RunSeconds)*20) + noHitFloors*1500

let private evaluateUnlocks won stats profile =
    [ if stats.DepthReached>=3 && not(Set.contains "cracked-lens" profile.UnlockedItems) then "cracked-lens"
      if stats.BossKills>=3 && not(Set.contains "glass-cannon" profile.UnlockedItems) then "glass-cannon"
      if won && not(Set.contains "abyssal-crown" profile.UnlockedItems) then "abyssal-crown" ]

let finishRun won cause model =
    if not model.RunActive || model.RunOutcome.IsSome then model
    else
        let stats={model.RunStats with DeathCause=cause}
        let kills=stats.KillsByType|>Map.values|>Seq.sum
        let score=runScore stats
        let unlocks=evaluateUnlocks won stats model.Profile
        let deaths =
            match cause with
            | None -> model.Profile.Lifetime.DeathsByCause
            | Some value -> Map.change value (fun old->Some(1+Option.defaultValue 0 old)) model.Profile.Lifetime.DeathsByCause
        let lifetime=model.Profile.Lifetime
        let completed=
            {lifetime with RunsPlayed=lifetime.RunsPlayed+1;DeepestFloor=max lifetime.DeepestFloor stats.DepthReached
                           Wins=lifetime.Wins+(if won then 1 else 0);TotalKills=lifetime.TotalKills+kills
                           DeathsByCause=deaths;DepthHistory=lifetime.DepthHistory@[stats.DepthReached]}
        let profile=
            {model.Profile with Lifetime=completed
                                UnlockedItems=Set.union model.Profile.UnlockedItems (Set.ofList unlocks)
                                BestScoresBySeed=Map.change model.RunSeed (fun old->Some(max score (Option.defaultValue 0 old))) model.Profile.BestScoresBySeed}
        let noHitFloors=
            [1..max 0 stats.FloorsCleared]
            |> List.filter(fun floor->Map.tryFind floor stats.DamageByFloor|>Option.map snd|>Option.defaultValue 0.0<=0.0)|>List.length
        let outcome=if won then RunOutcome.Victory else RunOutcome.GameOver
        let summary={Outcome=outcome;Seed=model.RunSeed;FloorsCleared=stats.FloorsCleared;BossKills=stats.BossKills
                     EnemyKills=kills;CoinsCollected=stats.CoinsCollected;ItemsCollected=stats.ItemsFound
                     RunSeconds=stats.RunSeconds;NoHitFloors=noHitFloors;Score=score;UnlocksEarned=unlocks;Stats=stats}
        let discarded=initialModelForSeed model.RunSeed
        {discarded with Profile=profile;RunActive=false;RunOutcome=Some outcome;LastRunSummary=Some summary
                        RunStats=emptyRunStats;AudioEvents=model.AudioEvents}

let private finishDeathIfNeeded model =
    if model.RunActive && (model.PlayerLifeState=Dead || totalHalfHearts model.PlayerHealth=0) then
        finishRun false (model.RunStats.DeathCause |> Option.orElse(Some(DeathCause.Enemy "unknown"))) model
    else model

let init () : Model * AdapterCommand<Msg> = initialModel, Cmd.none

// `rec` because M11's fixed step raises production messages and `advanceSim` folds them through this
// same function. That is the point: there is ONE traversal transition, and the route a player takes
// reaches it by dispatching the very message the acceptance tests dispatch.
let rec update msg model : Model * AdapterCommand<Msg> =
    match msg with
    // Identity (issue #458). `Started` ANNOUNCES the initial state; it does not build it —
    // `initialModel` already did. Its whole job is to give the cue seam a transition to look at, so
    // keep this a no-op and put what you want to happen at startup in `AudioCues.forTransition`.
    | Started -> model, Cmd.none
    | Tick _ when model.RunOutcome.IsSome -> model, Cmd.none
    | Tick dtSeconds ->
        let advanced=advanceSim (fun message state -> update message state |> fst) dtSeconds model |> finishDeathIfNeeded
        let terminal=
            if advanced.RunActive && model.FloorIndex=6 && model.M5Boss.IsSome && advanced.M5Boss.IsNone then
                finishRun true None advanced
            else advanced
        terminal,Cmd.none
    | MovePaddle(side, direction) -> movePaddle side direction model, Cmd.none
    | ViewerInput(key, isDown) ->
        let moved =
            if isDown then
                match paddleForKey key with
                | Some(side, direction) -> movePaddle side direction model
                | None -> model
            else
                model

        { moved with
            LastInput = Some key
            Input = { moved.Input with Current = withKey (keyName key) isDown moved.Input.Current } }, Cmd.none
    | KeyChanged(key, isDown) ->
        { model with Input = { model.Input with Current = withKey key isDown model.Input.Current } }, Cmd.none
    | CommandChanged(command,isDown) ->
        { model with Input = { model.Input with Current = withCommand command isDown model.Input.Current } }, Cmd.none
    | PointerChanged(position, primaryDown) ->
        { model with Input = { model.Input with Current = withPointer position primaryDown model.Input.Current } }, Cmd.none
    | InputChanged snapshot ->
        { model with Input = { model.Input with Current = snapshot } }, Cmd.none
    | RevealSecret(adjacentRoom, secretRoom) ->
        { model with Floor = FloorGeneration.revealSecret adjacentRoom secretRoom model.Floor }, Cmd.none
    | UnlockDoor roomId ->
        // The key is spent only when the reciprocal pair actually transitioned, so pressing Interact
        // at an already-open door, at a non-key door, or with no key leaves currency untouched.
        if model.PlayerCurrency.Keys <= 0 then model, Cmd.none
        else
            let floor, unlocked = FloorGeneration.tryUnlockDoor model.Floor.CurrentRoom roomId model.Floor
            if unlocked then
                { model with
                    Floor = floor
                    PlayerCurrency = { model.PlayerCurrency with Keys = model.PlayerCurrency.Keys - 1 } }, Cmd.none
            else model, Cmd.none
    | TraverseDoor roomId ->
        let floor, travelled = FloorGeneration.tryTraverseDoor roomId model.Floor
        match travelled with
        | None -> model, Cmd.none
        | Some direction ->
            // Land just inside the RECIPROCAL doorway: leaving east arrives at the destination's
            // west wall, one player radius clear of it so the arrival cannot re-trigger the crossing.
            let opposite =
                match direction with
                | FloorGeneration.North -> FloorGeneration.South
                | FloorGeneration.East -> FloorGeneration.West
                | FloorGeneration.South -> FloorGeneration.North
                | FloorGeneration.West -> FloorGeneration.East
            let wall = wallMidpoint opposite
            let inward =
                match opposite with
                | FloorGeneration.North -> vec2 0.0 doorwayClearance
                | FloorGeneration.East -> vec2 -doorwayClearance 0.0
                | FloorGeneration.South -> vec2 0.0 -doorwayClearance
                | FloorGeneration.West -> vec2 doorwayClearance 0.0
            let slide =
                match direction with
                | FloorGeneration.North -> RoomSlideDirection.North
                | FloorGeneration.East -> RoomSlideDirection.East
                | FloorGeneration.South -> RoomSlideDirection.South
                | FloorGeneration.West -> RoomSlideDirection.West
            // M13: the slide is started, and it is watchable because the transition now names the room
            // being LEFT. M11 suppressed this: `Render.cameraOffset` begins one full room away (M6's
            // contract, asserted in M6RenderingEnemySymbologyTests) and nothing drew the departed room,
            // so every crossing would have shown 0.35 s of empty screen. `Render` now draws that room's
            // shell one playfield back along the slide axis, so the offset moves two rooms rather than
            // vacating the screen. The departed id is read BEFORE `loadM5Room` rewrites `CurrentRoom`.
            let departed = model.Floor.CurrentRoom

            loadM5Room
                roomId
                { model with
                    Floor = floor
                    M6CameraTransition = Some { Direction = slide; ElapsedTicks = 0; FromRoom = departed }
                    PlayerPosition = add wall inward }, Cmd.none
    | BossCleared roomId ->
        { model with Floor = FloorGeneration.clearBoss roomId model.Floor }, Cmd.none
    // §M11: a descent is guarded by the state it DEPICTS. Before this the reducer descended
    // unconditionally from anywhere — which is why level progression's journeys passed from a
    // trapdoor-less starting room.
    | DescendFloor when not (canDescend model) -> model, Cmd.none
    | DescendFloor ->
        let nextIndex = model.FloorIndex + 1
        let generated = FloorGeneration.generateWithPool model.RunSeed nextIndex model.M5ItemPool
        { model with
            FloorIndex = nextIndex
            Floor = generated.Floor
            LayoutRng = generated.LayoutRng
            M5ItemPool = generated.ItemPool
            M5Enemies = []
            M5Boss = None
            M5ChoirMemberIds = Set.empty
            M5Obstacles = []
            M5ShopSlots = []
            // M13: a descent discards every room-local carry-over. Drops belong to rooms on the floor
            // being left; room ids are REUSED across floors, so keeping either of these would leave a
            // pickup or a departed-room shell resolving to a different room of the same number.
            M5ObstacleDrops = []
            // Board item #47: the reward is room-local for the same reason, and it is now COLLECTABLE,
            // so a stale one is worse than a cosmetic leftover — a fixed step between this message and
            // the `EnterM5Room 0` that production always pairs with it could grant the departed
            // floor's item at the new floor's plinth position. `playerRoomIntentsIn` raises the pair
            // with no step in between, but `PerformanceEvidence` dispatches `DescendFloor` alone, so
            // this does not rest on the pairing holding everywhere.
            M5Room = { model.M5Room with Reward = None }
            M6CameraTransition = None
            ShotSpawns = []
            HomingTargets = []
            EnemyBullets = []
            Bombs = []
            RunStats = { model.RunStats with DepthReached=max model.RunStats.DepthReached nextIndex }
            FloorNameTicks = 240
            PlayerPosition = vec2 (playfieldWidth / 2.0) (playfieldHeight / 2.0) }, Cmd.none
    | EnterM5Room roomId -> loadM5Room roomId model, Cmd.none
    | DamageM5Enemy(enemyId,damage) ->
        let next=damageM5Enemy enemyId damage model
        if model.FloorIndex=6 && model.M5Boss.IsSome && next.M5Boss.IsNone then finishRun true None next,Cmd.none else next,Cmd.none
    | DamageM5Boss damage ->
        let next=damageM5Boss damage model
        if model.FloorIndex=6 && model.M5Boss.IsSome && next.M5Boss.IsNone then finishRun true None next,Cmd.none else next,Cmd.none
    | InteractM5Shop slotId -> purchaseM5ShopSlot slotId model, Cmd.none
    | DamageM5Obstacle(obstacleId,damage) -> damageM5Obstacle obstacleId damage model,Cmd.none
    | SpawnM6Particles(count, origin, tint) -> spawnM6Particles count origin tint model, Cmd.none
    | BeginM6RoomTransition direction ->
        // The evidence-only entry point. It has no crossing to read a departed room from, so it names
        // the room the model is standing in — the same room a crossing would have departed.
        { model with M6CameraTransition = Some { Direction = direction; ElapsedTicks = 0; FromRoom = model.Floor.CurrentRoom } }, Cmd.none
    | StartRun seed ->
        let scaling = difficultyScaling model.Profile.Settings.Difficulty
        // M11: start the run in a LOADED start room, through the same seam every other room uses.
        let started = bootModelForSeed seed
        { started with
            Profile=model.Profile
            ActiveDifficulty=Some scaling
            RunActive=true
            RunOutcome=None
            LastRunSummary=model.LastRunSummary
            RunStats={ emptyRunStats with Character=model.RunStats.Character }
            PlayerHealth=
                { started.PlayerHealth with
                    RedContainers=started.PlayerHealth.RedContainers + scaling.ExtraStartingContainers
                    RedHalfHearts=started.PlayerHealth.RedHalfHearts + 2*scaling.ExtraStartingContainers } }, Cmd.none
    | SetDifficulty difficulty ->
        { model with Profile={ model.Profile with Settings={ model.Profile.Settings with Difficulty=difficulty } } }, Cmd.none
    | SetMasterVolume volume ->
        { model with Profile={ model.Profile with Settings={ model.Profile.Settings with MasterVolume=clampVolume volume } } }, Cmd.none
    | SetMuted muted ->
        { model with Profile={ model.Profile with Settings={ model.Profile.Settings with Muted=muted } } }, Cmd.none
    | SetScreenShake enabled ->
        { model with Profile={ model.Profile with Settings={ model.Profile.Settings with ScreenShake=enabled } } }, Cmd.none
    | SetStatScope scope -> { model with StatScope=scope }, Cmd.none
    // Board item #47 removed the `RecordItemFound` handler here. `ItemsFound` is now incremented in
    // exactly one place, `grantItem`, together with the append to `PlayerItems`.
    | RecordCoinsCollected count ->
        let count=max 0 count
        {model with RunStats={model.RunStats with CoinsCollected=model.RunStats.CoinsCollected+count}},Cmd.none
    | CompleteRunStats(won,cause) -> finishRun won cause model,Cmd.none
    | ProfileLoaded profile -> {model with Profile=profile},Cmd.none
    | NoOp -> model, Cmd.none

let subscriptions _ : AdapterSubscription<Msg> list = Sub.none
