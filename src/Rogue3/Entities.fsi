// The DECLARED public surface of this module (EHotwagner/rogue3#96, constitution principle III:
// "Public Surface Is Declared, Not Incidental"). A binding absent from this file is private to
// the implementation and is NOT product API -- adding one here is a contracted (Tier 1) change.
module Rogue3.Entities

[<RequireQualifiedAccess>]
type EnemyKind =
    | Grub
    | Maggot
    | Spitter
    | Fly
    | Charger
    | Turret
    | Caster
    | Brute

[<RequireQualifiedAccess>]
type MovementClass =
    | Grounded
    | Flying

type EnemyDefinition =
    {
      Kind: EnemyKind
      Radius: float
      HitPoints: float
      Speed: float
      DashSpeed: float option
      Threat: int
      ContactDamage: int
      Movement: MovementClass
    }

val definition: _arg1: EnemyKind -> EnemyDefinition

val roster: EnemyKind list

val hpScale: floorIndex: int -> float

val bulletSpeedScale: floorIndex: int -> float

val enemyBulletSpeed: floorIndex: int -> float

val threatBudget: floorIndex: int -> int

val scaledDefinition: floorIndex: int -> kind: EnemyKind -> EnemyDefinition

[<RequireQualifiedAccess>]
type EnemyState =
    | Seeking of ticks: int
    | HopPause of ticksLeft: int
    | SpitterCooldown of ticksLeft: int
    | SpitterTelegraph of ticksLeft: int
    | Orbit of ticks: int
    | Dive of direction: Geometry.Vec2 * ticksLeft: int
    | ReturnToOrbit of ticks: int
    | ChargerIdle
    | ChargerWindUp of direction: Geometry.Vec2 * ticksLeft: int
    | ChargerDash of direction: Geometry.Vec2 * ticksLeft: int
    | ChargerRecover of ticksLeft: int
    | TurretCooldown of ticksLeft: int * volley: int
    | CasterCooldown of ticksLeft: int
    | CasterArrival of ticksLeft: int
    | BruteCooldown of ticksLeft: int
    | BruteTelegraph of ticksLeft: int
    | BruteShockwave of ticksLeft: int

/// The product's ONE representation of a live enemy (board item #20).
///
/// `Velocity`, `LastContactTick` and `HitFlashTicks` moved here from the pre-M5 `Model.Enemy`
/// record when that second generation was removed. They are the three facts that record was
/// carrying and this one had nowhere to put; `Radius` and `ContactDamage` did NOT move, because
/// both are total functions of `Kind` through `definition` and storing them is what let a fixture
/// build a 64-unit enemy no floor can spawn.
///
/// `Velocity` is written by shot knockback in `Model.resolveShotCombat` and read by NO integrator:
/// `stepEnemy` advances an actor by `speed * direction`, never by a stored velocity, and `Render`
/// never reads it. It is carried across unchanged rather than deleted because
/// `M3CombatHealthCurrencyTests` asserts knockback is recorded; that it is recorded and never
/// applied is filed at root cause (EHotwagner/rogue3#43), not resolved under cover of a refactor.
/// `HitFlashTicks` is in the same condition: asserted by that file, drawn by nothing.
type EnemyActor =
    {
      Id: int
      Kind: EnemyKind
      Position: Geometry.Vec2
      Anchor: Geometry.Vec2
      HitPoints: float
      State: EnemyState
      SplitEligible: bool
      Velocity: Geometry.Vec2
      LastContactTick: int option
      HitFlashTicks: int
    }

[<RequireQualifiedAccess>]
type EnemyAction =
    | FireAimed of speed: float
    | FireBurst of count: int * angleOffsetDeg: float * speed: float
    | FireRing of count: int * speed: float
    | Teleport of destination: Geometry.Vec2
    | Shockwave of
      maxRadius: float * durationTicks: int * damage: int * knockback: float

type EnemyStepContext =
    {
      FloorIndex: int
      Player: Geometry.Vec2
      WallHit: bool
      PlayerHit: bool
      DropRng: FS.GG.Game.Core.Rng
    }

type EnemyStep =
    {
      Actor: EnemyActor
      Actions: EnemyAction list
      DropRng: FS.GG.Game.Core.Rng
    }

val ticks: seconds: float -> int

val spawn:
  floorIndex: int ->
    id: int -> kind: EnemyKind -> pos: Geometry.Vec2 -> EnemyActor

val stepEnemy: context: EnemyStepContext -> actor: EnemyActor -> EnemyStep

val grubSplit:
  floorIndex: int -> nextId: int -> actor: EnemyActor -> EnemyActor list

type BulletPattern =
    {
      Count: int
      ArcDegrees: float
      Speed: float
      SpinDegreesPerVolley: float
      CadenceTicks: int
      Homing: float
      GapIndex: int option
    }

[<RequireQualifiedAccess>]
type BossKind =
    | Gnawer
    | HollowChoir
    | Maw

type BossDefinition =
    {
      Kind: BossKind
      BaseHitPoints: float
      PhaseThresholds: float list
      Patterns: BulletPattern list
    }

val bossDefinition: _arg1: BossKind -> BossDefinition

val bossPhase: kind: BossKind -> hitPoints: float -> int

val choirRevives: killTicks: int list -> bool

type BossActor =
    {
      Id: int
      Kind: BossKind
      Position: Geometry.Vec2
      HitPoints: float
      Phase: int
      PatternTicksLeft: int
      Volley: int
      ChoirKillTicks: int list
    }

[<RequireQualifiedAccess>]
type BossAction =
    | Emit of BulletPattern * angleOffsetDeg: float
    | Charge of direction: Geometry.Vec2
    | SpawnMaggots of count: int
    | GroundPound of
      maxRadius: float * durationTicks: int * damage: int * knockback: float
    | ReviveChoir

type BossStep =
    {
      Boss: BossActor
      Actions: BossAction list
    }

val spawnBoss: id: int -> kind: BossKind -> position: Geometry.Vec2 -> BossActor

val stepBoss: player: Geometry.Vec2 -> tick: 'a -> actor: BossActor -> BossStep

[<RequireQualifiedAccess>]
type PickupKind =
    | Nothing
    | Coin1
    | Coin3
    | HalfRedHeart
    | Key
    | Bomb
    | SoulHeart

type WeightedDrop =
    {
      Kind: PickupKind
      Weight: int
    }

val roomClearTable: WeightedDrop list

val potTable: WeightedDrop list

val tintedRockTable: WeightedDrop list

val rollDrop:
  table: WeightedDrop list ->
    rng: FS.GG.Game.Core.Rng -> PickupKind * FS.GG.Game.Core.Rng

[<RequireQualifiedAccess>]
type ObstacleKind =
    | Rock
    | TintedRock
    | Pot
    | Spikes
    | Pit

type Obstacle =
    {
      Id: int
      Kind: ObstacleKind
      Position: Geometry.Vec2
      HitPoints: int option
      DropClaimed: bool
    }

val obstacle: kind: ObstacleKind -> id: int -> Obstacle

val obstacleAt: position: Geometry.Vec2 -> value: Obstacle -> Obstacle

val blocksMovement: movement: MovementClass -> kind: ObstacleKind -> bool

val blocksShots: _arg1: ObstacleKind -> bool

val spikeDamage: kind: ObstacleKind -> int

val destroyObstacle:
  damage: int ->
    rng: FS.GG.Game.Core.Rng ->
    obstacle: Obstacle ->
    Obstacle option * PickupKind option * FS.GG.Game.Core.Rng

type ItemDefinition =
    {
      Id: string
      Quality: int
      Tags: Set<string>
    }

type ItemPool =
    {
      Available: ItemDefinition list
      Placed: Set<string>
    }

val baseItems: ItemDefinition list

val itemPool: unlocked: ItemDefinition list -> ItemPool

val drawItem:
  tag: string ->
    rng: FS.GG.Game.Core.Rng ->
    pool: ItemPool -> ItemDefinition option * FS.GG.Game.Core.Rng * ItemPool

[<RequireQualifiedAccess>]
type ShopOffer =
    | Empty
    | Item of ItemDefinition
    | Consumable of PickupKind

type ShopSlot =
    {
      Id: int
      Offer: ShopOffer
      Price: int
      KeyLocked: bool
    }

val generateShop:
  rng: FS.GG.Game.Core.Rng ->
    pool: ItemPool -> ShopSlot list * FS.GG.Game.Core.Rng * ItemPool

val purchase:
  coins: int -> keys: int -> slot: ShopSlot -> int * int * ShopSlot * bool

[<RequireQualifiedAccess>]
type DoorState =
    | Open
    | LockedClear
    | BossSealed

type CombatRoom =
    {
      IsBoss: bool
      Cleared: bool
      Doors: DoorState list
      LiveEnemyIds: Set<int>
      Drop: PickupKind option
      Reward: ItemDefinition option
      Trapdoor: bool
    }

val enterRoom: enemyIds: int list -> room: CombatRoom -> CombatRoom

val enemyDied:
  enemyId: int ->
    rng: FS.GG.Game.Core.Rng ->
    room: CombatRoom -> CombatRoom * FS.GG.Game.Core.Rng

val enemyDiedWithNothingWeight:
  nothingWeight: int ->
    enemyId: int ->
    rng: FS.GG.Game.Core.Rng ->
    room: CombatRoom -> CombatRoom * FS.GG.Game.Core.Rng

val bossCleared: reward: ItemDefinition option -> room: CombatRoom -> CombatRoom
