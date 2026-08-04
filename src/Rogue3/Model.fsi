// The DECLARED public surface of this module (EHotwagner/rogue3#96, constitution principle III:
// "Public Surface Is Declared, Not Incidental"). A binding absent from this file is private to
// the implementation and is NOT product API -- adding one here is a contracted (Tier 1) change.
module Rogue3.Model

type Ball =
    {
      Pos: Geometry.Vec2
      Velocity: Geometry.Vec2
    }

type PaddleSide =
    | LeftSide
    | RightSide

type PaddleDirection =
    | PaddleUp
    | PaddleDown

/// One controller poll captured by the host edge. Values remain raw in [-1,1]; resolution applies
/// deadzones and normalization inside the pure fixed-step transition.
type GamepadSnapshot =
    {
      LeftStick: Geometry.Vec2
      RightStick: Geometry.Vec2
      RightTrigger: float
      Buttons: Set<FS.GG.UI.KeyboardInput.KeyId>
    }

/// Device state sampled independently from simulation. `InputChanged` replaces this whole value;
/// keyboard and pointer messages update the same shape one field at a time.
type InputSnapshot =
    {
      Keys: Set<FS.GG.UI.KeyboardInput.KeyId>
      Commands: Set<string>
      MousePosition: Geometry.Vec2 option
      MousePrimaryDown: bool
      Gamepad: GamepadSnapshot
    }

/// The current/previous pair is the replay contract. `PressedThisTick` is derived only when a Tick
/// actually drains a fixed step, then current becomes previous after all drained steps complete.
type InputState =
    {
      Current: InputSnapshot
      Previous: InputSnapshot
      PressedThisTick: Set<FS.GG.UI.KeyboardInput.KeyId>
    }

type AimSource =
    | NoAim
    | ArrowAim
    | MouseAim
    | GamepadAim

type ResolvedInput =
    {
      Move: Geometry.Vec2
      Aim: Geometry.Vec2
      AimSource: AimSource
      FireHeld: bool
      PressedThisTick: Set<FS.GG.UI.KeyboardInput.KeyId>
    }

type PlayerStats =
    {
      Damage: float
      FireRate: float
      ShotSpeed: float
      Range: float
      ShotRadius: float
      Knockback: float
      Multishot: int
      Pierce: int
      Bounce: int
      Homing: float
      SpeedMultiplier: float
    }

type Stat =
    | DamageStat
    | TearDelayStat
    | ShotSpeedStat
    | RangeStat
    | SpeedMultiplierStat
    | MultishotStat
    | ShotRadiusStat
    | KnockbackStat
    | PierceStat
    | BounceStat
    | HomingStat

type ModifierKind =
    | Add
    | Mul

type StatModifier =
    {
      Stat: Stat
      Kind: ModifierKind
      Value: float
    }

type PlayerItem =
    {
      Id: string
      Modifiers: StatModifier list
    }

type Health =
    {
      RedContainers: int
      RedHalfHearts: int
      SoulHalfHearts: int
      BlackHalfHearts: int
    }

type Currency =
    {
      Coins: int
      Keys: int
      Bombs: int
    }

type EnemyBullet =
    {
      Id: int
      Position: Geometry.Vec2
      Velocity: Geometry.Vec2
      Radius: float
      Damage: int
      Homing: float
      AgeTicks: int
    }

type Bomb =
    {
      Id: int
      Position: Geometry.Vec2
      FuseTicks: int
    }

type PlayerLifeState =
    | Alive
    | Dead

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
type DifficultyMode =
    | Easy
    | Normal
    | Hard

type DifficultyScaling =
    {
      EnemyHpScale: float
      PostHitInvulnSeconds: float
      DropNothingWeight: int
      ExtraStartingContainers: int
      ExtraElitePerCombatRoom: int
      PostBossHeal: bool
    }

type GameSettings =
    {
      Difficulty: DifficultyMode
      MasterVolume: float
      Muted: bool
      ScreenShake: bool
    }

[<RequireQualifiedAccess>]
type DeathCause =
    | Enemy of string
    | Trap
    | Bomb

[<RequireQualifiedAccess>]
type StatScope =
    | ThisRun
    | Lifetime

type RunStats =
    {
      DepthReached: int
      FloorsCleared: int
      BossKills: int
      KillsByType: Map<Entities.EnemyKind,int>
      ItemsFound: int
      CoinsCollected: int
      RunSeconds: float
      DamageDealt: float
      DamageTaken: float
      DamageByFloor: Map<int,(float * float)>
      DeathCause: DeathCause option
      Character: string
    }

type LifetimeStats =
    {
      RunsPlayed: int
      DeepestFloor: int
      Wins: int
      TotalKills: int
      DeathsByCause: Map<DeathCause,int>
      DepthHistory: int list
    }

type MetaProfile =
    {
      Settings: GameSettings
      Lifetime: LifetimeStats
      UnlockedItems: Set<string>
      UnlockedCharacters: Set<string>
      BestScoresBySeed: Map<uint64,int>
    }

[<RequireQualifiedAccess>]
type RunOutcome =
    | GameOver
    | Victory

type RunSummary =
    {
      Outcome: RunOutcome
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
      Stats: RunStats
    }

[<RequireQualifiedAccess>]
type ParticleShape =
    | Circle
    | Quad

[<RequireQualifiedAccess>]
type ParticleTint =
    | Death
    | Muzzle
    | Explosion

type M6Particle =
    {
      Id: int
      Position: Geometry.Vec2
      Velocity: Geometry.Vec2
      LifetimeTicks: int
      AgeTicks: int
      Radius: float
      Shape: ParticleShape
      Tint: ParticleTint
    }

[<RequireQualifiedAccess>]
type RoomSlideDirection =
    | North
    | East
    | South
    | West

/// A room crossing in flight.
///
/// `FromRoom` is M13's addition and it is what makes the slide watchable. `Render.cameraOffset`
/// translates the entered room a full playfield away at `remaining = 1.0`, so without knowing which
/// room was LEFT the renderer has nothing to put in the space that offset vacates — which is why M11
/// suppressed the slide rather than ship 0.35 s of empty screen. The renderer draws that room's shell
/// one playfield back along the slide axis, so a crossing reads as one room leaving and one arriving.
type M6CameraTransition =
    {
      Direction: RoomSlideDirection
      ElapsedTicks: int
      FromRoom: int
    }

/// A pickup lying on the floor of the room, at a world position a player can walk to.
///
/// Before M13 `Model.ObstacleDrops` was a bare `PickupKind list`: a smashed pot recorded WHAT it
/// dropped and nothing about WHERE, so the renderer drew the drops as an indexed strip at fixed
/// coordinates and nothing could be collected. `Id` is the destroyed obstacle's id and `Position` is
/// where it stood, so the coin lies where the pot was.
type FloorPickup =
    {
      Id: int

      /// The room this pickup is lying in. Without it a drop was destroyed by the next room change:
      /// `loadM5Room` cleared the list outright while `recordDestroyedObstacle` is durable floor
      /// state, so smashing a pot and stepping through a door before collecting lost the reward
      /// permanently and the pot never came back to re-roll it.
      Room: int
      Kind: Entities.PickupKind
      Position: Geometry.Vec2
    }
type HomingTarget =
    {
      Id: int
      Position: Geometry.Vec2
    }

/// A live M2 projectile. Integer age/hit budgets make termination exact at the fixed-step boundary.
type ShotSpawn =
    {
      Id: int
      Position: Geometry.Vec2
      Direction: Geometry.Vec2
      Velocity: Geometry.Vec2
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
      SimStep: int
    }

/// The deterministic cost counters the performance workloads read, and nothing else does.
///
/// Board item #60 moved these seven fields off `Model` and into one sub-record. They are pure
/// instrumentation: every one of them is a monotonic tally of work a fixed step performed, written
/// by the reducer that performs the work and read only to compute a per-frame delta for a cost
/// driver. No gameplay branch reads one, and none may start to — a counter a rule depends on stops
/// being free to change when the measurement changes.
///
/// There is a second reason the boundary is worth drawing. `Determinism.encode` walks the WHOLE
/// `Model`, so these tallies sit inside the value the replay golden compares; keeping them in one
/// named place makes it visible that the golden carries instrumentation as well as state.
type InstrumentationCounters =
    {

      /// Shots emitted, counted past the retained `ShotSpawns` history so the monotonic total
      /// outlives the bounded list. Drives `simulation.shot-spawn`.
      TotalShotSpawns: int
      /// Swept-cast and slide-contact wall queries performed by the player and by shots.
      /// Drives `collision.shot-wall-queries`.
      TotalWallQueries: int
      /// Candidate targets considered by homing shots. Drives
      /// `simulation.homing-target-considerations`.
      TotalHomingQueries: int
      /// Broad-phase combat candidates: shot-versus-enemy, bullet-versus-player and contact tests.
      /// Drives `collision.combat-candidates`.
      TotalCombatCandidates: int
      /// Pending secret/adjacent pairs examined by the §14.14 blast scan. Drives
      /// `simulation.secret-reveal-candidates`.
      TotalSecretRevealCandidates: int
      /// Doorway sensors examined by the M11 fixed-step door scan, bounded by the four walls of a
      /// room. Drives `simulation.door-sensor-candidates`.
      TotalDoorSensorQueries: int
      /// Player-versus-floor-pickup overlap tests performed by the M13 fixed-step collection scan,
      /// bounded by the destructible obstacles a room carries. Drives
      /// `simulation.floor-pickup-candidates`.
      TotalFloorPickupCandidates: int
    }
val zeroInstrumentation: InstrumentationCounters

type Model =
    {
      Ball: Ball
      LeftPaddleY: float
      RightPaddleY: float
      PaddleHeight: float
      LeftScore: int
      RightScore: int
      Playfield: Geometry.Vec2
      SimAccumulator: float
      SimStepCount: int
      TickCount: int
      RunSeed: uint64
      LayoutRng: FS.GG.Game.Core.Rng
      DropRng: FS.GG.Game.Core.Rng
      FloorIndex: int
      Floor: FloorGeneration.Floor
      LastInput: FS.GG.UI.KeyboardInput.ViewerKey option
      Input: InputState
      PlayerPosition: Geometry.Vec2
      PlayerVelocity: Geometry.Vec2
      PlayerStats: PlayerStats
      PlayerItems: PlayerItem list
      PlayerHealth: Health
      PlayerCurrency: Currency
      PlayerLifeState: PlayerLifeState
      PostHitInvulnTicks: int
      HomingTargets: HomingTarget list
      EnemyBullets: EnemyBullet list
      Bombs: Bomb list
      Enemies: Entities.EnemyActor list
      Boss: Entities.BossActor option
      ChoirMemberIds: Set<int>
      Room: Entities.CombatRoom
      Obstacles: Entities.Obstacle list
      ShopSlots: Entities.ShopSlot list
      ObstacleDrops: FloorPickup list
      ItemPool: Entities.ItemPool
      AiDecisions: int
      BulletEmissions: int
      BossBulletEmissions: int
      BossPatternEmissions: int
      NextEntityId: int
      NextBulletId: int
      NextBombId: int
      Facing: Geometry.Vec2
      LastResolvedInput: ResolvedInput
      FireCooldown: float
      WasFiring: bool
      ShotSpawns: ShotSpawn list
      NextShotId: int
      DodgeRollTicks: int
      DodgeIFrameTicks: int
      DodgeCooldownTicks: int

      /// Board item #60: the seven deterministic cost counters, in one sub-record. Nothing outside
      /// `PerformanceEvidence` and the tests that pin those measurements may read this.
      Instrumentation: InstrumentationCounters
      BlackHeartBursts: int
      EdgeActionCount: int
      Particles: M6Particle list
      NextParticleId: int
      CameraTransition: M6CameraTransition option
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
      AudioEvents: AudioEvent list
    }
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
    | ViewerInput of FS.GG.UI.KeyboardInput.ViewerKey * isDown: bool
    | KeyChanged of FS.GG.UI.KeyboardInput.KeyId * isDown: bool
    | CommandChanged of command: string * isDown: bool
    | PointerChanged of position: Geometry.Vec2 * primaryDown: bool option
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
    | EnterM5Room of roomId: int
    | DamageM5Enemy of enemyId: int * damage: float
    | DamageM5Boss of damage: float
    | InteractM5Shop of slotId: int
    | DamageM5Obstacle of obstacleId: int * damage: int
    | SpawnM6Particles of
      count: int * origin: Geometry.Vec2 * tint: ParticleTint
    | BeginM6RoomTransition of RoomSlideDirection
    | StartRun of seed: uint64
    | SetDifficulty of DifficultyMode
    | SetMasterVolume of float
    | SetMuted of bool
    | SetScreenShake of bool
    | SetStatScope of StatScope
    | RecordCoinsCollected of int
    | CompleteRunStats of won: bool * cause: DeathCause option
    | ProfileLoaded of MetaProfile
    | NoOp
type GeneratedLayoutValidationFailureClass =
    | MissingLayoutFacts
    | OverlappingLayoutBounds

type GeneratedLayoutValidationResult =
    {
      Accepted: bool
      FailureClass: GeneratedLayoutValidationFailureClass option
      Diagnostics: string list
    }

/// Hollow Depths' stable logical coordinate system. The host letterboxes this canvas onto the
/// physical output; simulation and view never observe the window dimensions.
val playfieldWidth: float

val playfieldHeight: float

/// One fixed simulation step is 1/120 s (Hollow Depths §7.3 / §13).
val fixedDt: float

/// A host frame may advance no more than five fixed steps. `FixedStep.drainWith` receives this as
/// a maximum accepted frame-time budget, preventing a tab-out or stall from creating catch-up debt.
val maxSteps: int

val playerRadius: float

val rollSpeed: float

val rollDurationTicks: int

val dodgeIFrameTicks: int

val dodgeCooldownTicks: int

val shotVelocityInheritance: float

val postHitInvulnTicks: int

val bombFuseTicks: int

val bombRadius: float

/// The player's static collider set for a room: the SINGLE description of which obstacles block
/// movement, and the reason board item #20's `Model.Obstacles` field no longer exists.
///
/// This expression used to be copy-pasted at four assignment sites that each refreshed a stored
/// `Obstacles: Rect list` cache — `resolveBombs`, `loadM5Room`, `damageM5Obstacle` and the boot
/// initialiser. A cache can be stale and a function cannot, so the field went and this stayed.
/// Callers: the player sweep and the `shotWalls` filter in `stepInput`, and the `TotalWallQueries`
/// accounting that counts what those two iterate.
val blockingObstacleRects:
  obstacles: Entities.Obstacle list -> Geometry.SimRect list

/// An actor's collision radius. A function of `Kind`, never stored per instance — storing it is what
/// let the maximum-content fixture build a 64-unit enemy no floor can spawn (board item #20).
val actorRadius: actor: Entities.EnemyActor -> float

/// An actor's contact damage. A function of `Kind`, for the same reason as `actorRadius`.
val actorContactDamage: actor: Entities.EnemyActor -> int

/// Midpoint of the room wall a door in `direction` is carved through, in logical room coordinates.
/// A blast reaches that wall when its radius covers this point, which is the §14.14 trigger and the
/// landing point a traversal arrives at from the opposite side.
val wallMidpoint: direction: FloorGeneration.DoorDirection -> Geometry.Vec2

/// Half the width of a doorway opening along its wall, in logical room units.
val doorwayHalfSpan: float

/// How far into the room a doorway sensor reaches. Strictly less than `doorwayClearance`.
val doorwaySensorDepth: float

/// How far inside the destination a crossing lands the player, measured from the wall it entered
/// through. The player radius plus a margin, so the arrival never overlaps the wall it came through.
val doorwayClearance: float

/// Wall-normal distance from `position` to the wall `direction` names, and the lateral offset from
/// that wall's midpoint. Together they place a point relative to one doorway.
val doorwayOffsets:
  direction: FloorGeneration.DoorDirection ->
    position: Geometry.Vec2 -> float * float

/// True when `position` is standing in the doorway on the wall `direction` names.
val doorwaySensorContains:
  direction: FloorGeneration.DoorDirection -> position: Geometry.Vec2 -> bool

/// Thickness of the drawn stone band that frames a room, in logical room units.
val wallThickness: float

/// The four wall slabs of a room whose doors occupy `directions`, with a gap at each of those walls.
///
/// These are SIMULATION rects (`FS.GG.Game.Core.Rect`) because that is what `Collision.sweepCircle`
/// speaks and the player sweep is the consumer that must not be able to disagree with the picture.
/// `Render` converts them into the scene vocabulary, which is a field-for-field copy.
val roomWallSlabsFor:
  directions: Set<FloorGeneration.DoorDirection> -> FS.GG.Game.Core.Rect list

/// The directions in which `roomId` carries a PASSABLE doorway on the floor graph.
///
/// `HiddenWall` is excluded, and that exclusion is the whole point. A hidden wall is DRAWN as
/// `Scene.filledRectangle ... stone` in the same colour as the band, and its own renderer comment
/// says "It reads as WALL, not door" -- so opening a collider gap there let the player stand a full
/// 24 units inside solid-looking stone, which is exactly the defect this milestone's wall row exists
/// to close. Nothing needs the gap: a secret is revealed by a bomb blast testing `wallMidpoint`
/// against `bombRadius`, not by the player reaching the wall.
val roomDoorDirections:
  roomId: int ->
    floor: FloorGeneration.Floor -> Set<FloorGeneration.DoorDirection>

/// The wall slabs of the room the player is standing in — drawn by `Render.roomWallsScene` and swept
/// by the player in the same fixed step.
val roomWallSlabs: model: Model -> FS.GG.Game.Core.Rect list

/// How far past the wall, into the room, a door's threshold is drawn. A placed pickup or pedestal
/// must clear this too, or it would sit under a door panel.
val doorApron: float

/// The floor pickups lying in the room the player is standing in. Drops persist across a crossing,
/// so everything that draws or collects them asks this rather than reading the whole list.
val floorPickupsHere: model: Model -> FloorPickup list

/// The drawn trapdoor sits at the centre of the room it belongs to, so it reads as a floor feature
/// a player walks onto rather than a decoration parked near the HUD. Rendering and the `DescendFloor`
/// guard consume this one record, so the fixture a player sees is the fixture the guard tests.
val trapdoorHalfWidth: float

val trapdoorHalfHeight: float

val trapdoorCenter: Geometry.Vec2

/// True when the player standing at `position` is on the trapdoor.
val trapdoorContains: position: Geometry.Vec2 -> bool

/// How far a placed fixture keeps from an obstacle's centre. An obstacle occupies a 40x40 AABB, so
/// half its diagonal is ~28.3; 46 leaves a visible gap rather than a touching edge.
val obstacleClearance: float

/// True when `position` is inside the stone band, or inside a doorway opening plus the apron the door
/// panel is drawn across. Either would put a fixture under the room shell.
val insideRoomShell: position: Geometry.Vec2 -> bool

/// True when a fixture may stand at `position` in the room `obstacles` furnishes.
val placementAccepts:
  obstacles: Entities.Obstacle list ->
    taken: Geometry.Vec2 list -> position: Geometry.Vec2 -> bool

/// The first `count` accepted candidate positions for a room furnished with `obstacles`.
///
/// Deterministic and total: if the candidate list is exhausted the remaining fixtures fall back to
/// the last accepted position offset along the row, so a pathologically furnished room still places
/// its stock somewhere inside the shell rather than throwing or vanishing.
val placeRoomFixtures:
  obstacles: Entities.Obstacle list -> count: int -> Geometry.Vec2 list

val basePlayerStats: PlayerStats

val difficultyScaling: _arg1: DifficultyMode -> DifficultyScaling

val emptyRunStats: RunStats

val defaultMetaProfile: MetaProfile

val winRatePct: lifetime: LifetimeStats -> float

val depthHistogram: depths: int list -> int list

val encodeMetaProfile: profile: MetaProfile -> string

val tryDecodeMetaProfile: payload: string -> Result<MetaProfile,string>

val profilePersistenceRequestsForTransition:
  msg: Msg ->
    previous: Model -> next: Model -> FS.GG.UI.Canvas.PersistenceEffect list

val recomputePlayerStats: items: PlayerItem list -> PlayerStats

val totalHalfHearts: health: Health -> int

val displayedHeartHalves: health: Health -> int

val applyDamage: halfHearts: int -> health: Health -> Health * int

val addTemporaryHearts: soul: int -> black: int -> health: Health -> Health

val healRed: amount: int -> health: Health -> Health

val addRedContainer: health: Health -> Health

val addCurrency: amount: int -> current: int -> int

val emptyInputSnapshot: InputSnapshot

val initialModelForSeed: seed: uint64 -> Model

/// Uniform centered logical-canvas transform used for world-to-screen presentation.
type WorldScreenTransform =
    {
      Scale: float
      OffsetVx: float
      OffsetVy: float
    }

val worldScreenTransform:
  outputWidth: float -> outputHeight: float -> WorldScreenTransform

val worldToScreen:
  transform: WorldScreenTransform -> point: Geometry.Vec2 -> Geometry.Vec2

val screenToWorld:
  transform: WorldScreenTransform -> point: Geometry.Vec2 -> Geometry.Vec2

val keyName:
  key: FS.GG.UI.KeyboardInput.ViewerKey -> FS.GG.UI.KeyboardInput.KeyId

val movePaddle:
  side: PaddleSide -> direction: PaddleDirection -> model: Model -> Model

val normalizeOrZero: vector: Geometry.Vec2 -> Geometry.Vec2

val magnitude: vector: Geometry.Vec2 -> float

val resolveInput:
  playerPosition: Geometry.Vec2 ->
    pressedThisTick: Set<FS.GG.UI.KeyboardInput.KeyId> ->
    snapshot: InputSnapshot -> ResolvedInput

val shotSpeed: float

val maxShotSpawnHistory: int

val spawnShots:
  simStep: int ->
    nextId: int ->
    position: Geometry.Vec2 ->
    playerVelocity: Geometry.Vec2 ->
    aim: Geometry.Vec2 -> stats: PlayerStats -> ShotSpawn list

val stepShots:
  roomBounds: FS.GG.Game.Core.Rect ->
    walls: FS.GG.Game.Core.Rect list ->
    targets: HomingTarget list ->
    shots: ShotSpawn list -> ShotSpawn list * int * int

type DescentCarry =
    {
      Items: PlayerItem list
      Stats: PlayerStats
      Health: Health
      Currency: Currency
    }

val descentCarry: model: Model -> DescentCarry

val damageM5Boss: damage: float -> model: Model -> Model

val loadM5Room: roomId: int -> model: Model -> Model

val initialModel: Model

/// Apply one collected floor pickup. Currency goes through the shared 99 cap; hearts go through the
/// same `healRed`/`addTemporaryHearts` the shop and the post-boss heal use, so a pickup can never
/// exceed a container total or the 24-half-heart display cap by a different route.
val applyFloorPickup: kind: Entities.PickupKind -> model: Model -> Model

/// What an item DOES. Total by construction: an id with no authored entry still resolves to a
/// quality-scaled damage bonus, so an item added to the pool later can never be a silent no-op —
/// which is the exact failure this item exists to close, one level down.
///
/// `TearDelayStat` is INVERTED: `recomputePlayerStats` derives `FireRate = 30 / tearDelay`, so a
/// NEGATIVE tear-delay modifier fires faster and a positive one fires slower.
val itemModifiers: item: Entities.ItemDefinition -> StatModifier list

/// The item a generated definition becomes once a player owns it.
val playerItemOf: item: Entities.ItemDefinition -> PlayerItem

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
val grantItem: item: Entities.ItemDefinition -> model: Model -> Model

val purchaseM5ShopSlot: slotId: int -> model: Model -> Model

/// M13: walk onto a pickup and it is yours.
///
/// Before this, `ObstacleDrops` was write-only: smashing a pot appended a `PickupKind` that the
/// renderer drew in a fixed row and no reducer ever consumed, so the drop table, the drop RNG stream
/// and the drop visual all existed for a reward a player could not take. The scan is one circle test
/// per live pickup, counted into `TotalFloorPickupCandidates` so its cost is measured rather than
/// argued, and a collected pickup is removed in the same step it is applied — so a player standing
/// still on the spot collects exactly once.
val collectFloorPickups: model: Model -> Model

/// How close the player's centre must come to the reward plinth's centre to take it. Wider than
/// `floorPickupRadius` because the plinth is a drawn 26-unit-tall fixture, not a loose coin.
val roomRewardRadius: float

/// Where the room's reward stands.
///
/// The renderer places the reward at the fixture slot AFTER the shop stock
/// (`Render.renderedElementsIn`), so collection is tested at the point the player can actually see.
/// Both sides derive from `placeRoomFixtures`; `M13RoomTransitionWorldStateTests` pins the renderer
/// to it and `M14ItemGrantTests` pins this function to it, so the two cannot drift apart silently.
val roomRewardPosition: model: Model -> Geometry.Vec2 option

/// A pedestal may be taken on sight. A BOSS reward may not be taken until the boss is down —
/// otherwise a player walks past the sealed-in boss, grabs the prize and leaves.
val roomRewardCollectable: model: Model -> bool

/// Walk onto the pedestal and the item is yours: appended to `PlayerItems`, counted once, stats
/// recomputed, and removed from BOTH the live room and the floor record in the same step — so a
/// player standing still on the plinth collects exactly once, and so does one who leaves and returns.
val collectRoomReward: model: Model -> Model

/// How close the player's centre must come to a shop plinth's centre to be AT that slot.
///
/// The same distance as `roomRewardRadius`, and by construction rather than by coincidence: shop
/// stock and the reward pedestal are the same drawn plinth, placed by the same `placeRoomFixtures`
/// call, so two different reach distances would mean two fixtures that look identical behave
/// differently under the player's feet.
val shopSlotRadius: float

/// Where this room's shop stock stands, in slot order.
///
/// `Render.renderedElementsIn` places slot `index` at `placeRoomFixtures model.Obstacles n |> item
/// index` for an `n` that also counts the reward plinth. `placeRoomFixtures` is a PREFIX function —
/// it accepts candidates in order and takes the first `count` — so the first `ShopSlots.Length`
/// positions are the same list whether or not a reward is also being placed. Asking for exactly the
/// slot count here therefore yields the renderer's own positions, and `M14ItemGrantTests` pins that
/// against `Render.renderedElements` rather than against a restatement of this formula.
val shopSlotPositions: model: Model -> Geometry.Vec2 list

/// True when `slot` would actually be sold to this player right now.
///
/// Delegated to `purchaseOutcome` — the reducer itself — rather than restated. The rule is four
/// clauses (empty offer, key-locked without a key, priced above the purse, and an effect that cannot
/// land) and a second copy of it is a second thing to keep in step: the renderer would promise a
/// purchase the reducer then refuses, or hide one it would have made. This is a decision the product
/// draws a halo from, so it asks the question by running it.
val shopSlotAffordable: model: Model -> slot: Entities.ShopSlot -> bool

/// What the prompt should SAY when the shop will not sell `slot`, or `None` when it will.
///
/// Split from the decision above rather than folded into it: `shopSlotAffordable` answers whether,
/// this answers why, and only the first one may gate a reducer. The final arm is the one the cap
/// defect needed — a player refused for `NEED 6c` while holding ninety-nine coins is being told
/// something false, and "the refusal is legible" is half of this board item's acceptance.
val shopSlotRefusal: model: Model -> slot: Entities.ShopSlot -> string option

/// The stocked shop slot the player is standing at, with the position it is drawn at.
///
/// An EMPTIED slot is skipped: a bare plinth is not something to press interact at, and letting it
/// answer here would shadow a stocked neighbour placed close by. Nearest-first, so two slots whose
/// reach circles overlap resolve to the one the player is actually closest to rather than to
/// whichever happens to come first in `ShopSlots`.
val shopSlotUnderPlayer:
  model: Model -> (Entities.ShopSlot * Geometry.Vec2) option

val m6MaxParticles: int

val m6CameraDurationTicks: int

val stepSim: model: Model -> Model

/// True when `roomId` records the trapdoor fixture AND the loaded room agrees.
val trapdoorPresent: model: Model -> bool

/// True when the player may descend: the room depicts a trapdoor and the player is standing on it.
val canDescend: model: Model -> bool

val playerRoomIntentsIn:
  isFirstStep: bool ->
    pressedThisTick: Set<FS.GG.UI.KeyboardInput.KeyId> ->
    model: Model -> Model * Msg list

val runScore: stats: RunStats -> int

val finishRun: won: bool -> cause: DeathCause option -> model: Model -> Model

val init: unit -> Model * FS.GG.UI.Controls.Elmish.AdapterCommand<Msg>

val update:
  msg: Msg ->
    model: Model -> Model * FS.GG.UI.Controls.Elmish.AdapterCommand<Msg>

val subscriptions: 'a -> FS.GG.UI.Controls.Elmish.AdapterSubscription<Msg> list
