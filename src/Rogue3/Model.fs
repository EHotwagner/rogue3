module Rogue3.Model

open System
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
open FS.GG.Game.Core // FixedStep.drain — the fixed-timestep accumulator drain (ADR-0022 P5: moved from FS.GG.UI.Canvas to the FS.GG.Game.Core bottom layer)
open FS.GG.UI.Controls.Elmish
open FS.GG.UI.Controls.Elmish.Authoring // Cmd.none / Sub.none (Elmish-convention no-ops for `[]`)
open Rogue3.Geometry // Vec2 + vec2/add/scale/clamp/toPoint/toRect (collision-safe positions)

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

type Enemy =
    { Id: int
      Position: Vec2
      Velocity: Vec2
      Radius: float
      HitPoints: float
      ContactDamage: int
      LastContactTick: int option
      HitFlashTicks: int }

type EnemyBullet =
    { Id: int
      Position: Vec2
      Radius: float
      Damage: int }

type Bomb =
    { Id: int
      Position: Vec2
      FuseTicks: int }

type ShopCost = CoinCost of int | KeyCost of int

type ShopSlot =
    { Id: int
      Item: PlayerItem
      Cost: ShopCost }

type PlayerLifeState = Alive | Dead

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
      Obstacles: Rect list
      HomingTargets: HomingTarget list
      Enemies: Enemy list
      EnemyBullets: EnemyBullet list
      Bombs: Bomb list
      ShopSlots: ShopSlot list
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
      BlackHeartBursts: int
      EdgeActionCount: int }

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
    | PointerChanged of position: Vec2 * primaryDown: bool option
    | InputChanged of InputSnapshot
    | InteractShop of slotId: int
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
      LayoutRng = layoutRng
      DropRng = dropRng
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
      Obstacles = []
      HomingTargets = []
      Enemies = []
      EnemyBullets = []
      Bombs = []
      ShopSlots = []
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
      BlackHeartBursts = 0
      EdgeActionCount = 0 }

let initialModel = initialModelForSeed 0xC0FFEEUL

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
    let keyboardMove = vec2 (axis aKey dKey snapshot.Keys) (axis wKey sKey snapshot.Keys)
    let gamepadMove = activeStick snapshot.Gamepad.LeftStick
    let move = add keyboardMove gamepadMove |> normalizeOrZero

    let arrow =
        vec2
            (axis arrowLeftKey arrowRightKey snapshot.Keys)
            (axis arrowUpKey arrowDownKey snapshot.Keys)
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

let private takePlayerHit damage source model =
    if damage <= 0 || model.PlayerLifeState = Dead || model.DodgeIFrameTicks > 0 || model.PostHitInvulnTicks > 0 then model
    else
        let health, bursts = applyDamage damage model.PlayerHealth
        let away = sub model.PlayerPosition source |> normalizeOrZero
        { model with
            PlayerHealth = health
            PlayerVelocity = add model.PlayerVelocity (scale 90.0 away)
            PostHitInvulnTicks = postHitInvulnTicks
            BlackHeartBursts = model.BlackHeartBursts + bursts }

let purchaseShopSlot slotId model =
    match model.ShopSlots |> List.tryFind (fun slot -> slot.Id = slotId) with
    | None -> model, false
    | Some slot ->
        let affordable, currency =
            match slot.Cost with
            | CoinCost cost when model.PlayerCurrency.Coins >= cost ->
                true, { model.PlayerCurrency with Coins = model.PlayerCurrency.Coins - cost }
            | KeyCost cost when model.PlayerCurrency.Keys >= cost ->
                true, { model.PlayerCurrency with Keys = model.PlayerCurrency.Keys - cost }
            | _ -> false, model.PlayerCurrency
        if not affordable then model, false
        else
            let items = model.PlayerItems @ [ slot.Item ]
            { model with
                PlayerCurrency = currency
                PlayerItems = items
                PlayerStats = recomputePlayerStats items
                ShopSlots = model.ShopSlots |> List.filter (fun candidate -> candidate.Id <> slotId) }, true

type DescentCarry =
    { Items: PlayerItem list
      Stats: PlayerStats
      Health: Health
      Currency: Currency }

let descentCarry model =
    { Items = model.PlayerItems; Stats = model.PlayerStats; Health = model.PlayerHealth; Currency = model.PlayerCurrency }

let private resolveShotCombat model =
    let liveEnemies = model.Enemies |> List.filter (fun enemy -> enemy.HitPoints > 0.0)
    let maxRadius = liveEnemies |> List.map (fun enemy -> max 0.0 enemy.Radius) |> List.fold max 0.0
    let grid = SpatialGrid.build spatialCellSize [ for enemy in liveEnemies -> toSimPoint enemy.Position, enemy ]
    let mutable enemies = liveEnemies |> List.map (fun enemy -> enemy.Id, enemy) |> Map.ofList
    let mutable candidates = 0
    let shots =
        model.ShotSpawns
        |> List.choose (fun shot ->
            let nearby = SpatialGrid.queryRadius (toSimPoint shot.Position) (shot.Radius + maxRadius) grid
            candidates <- candidates + nearby.Length
            let hits =
                nearby
                |> List.filter (fun enemy ->
                    not (Set.contains enemy.Id shot.HitEnemyIds)
                    && circlesOverlap shot.Position shot.Radius enemy.Position enemy.Radius)
                |> List.sortBy (fun enemy -> enemy.Id)
                |> List.truncate shot.HitsRemaining
            for enemy in hits do
                match Map.tryFind enemy.Id enemies with
                | Some current when current.HitPoints > 0.0 ->
                    let impulse = normalizeOrZero shot.Velocity |> scale shot.Knockback
                    enemies <- Map.add enemy.Id
                        { current with
                            HitPoints = max 0.0 (current.HitPoints - shot.Damage)
                            Velocity = add current.Velocity impulse
                            HitFlashTicks = hitFlashTicks } enemies
                | _ -> ()
            let hitIds = (shot.HitEnemyIds, hits) ||> List.fold (fun ids enemy -> Set.add enemy.Id ids)
            let remaining = shot.HitsRemaining - hits.Length
            if remaining <= 0 then None
            else Some { shot with HitsRemaining = remaining; HitEnemyIds = hitIds })
    { model with
        Enemies = enemies |> Map.toList |> List.map snd
        ShotSpawns = shots
        TotalCombatCandidates = model.TotalCombatCandidates + candidates }

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
            result.Enemies
            |> List.map (fun enemy ->
                if enemy.HitPoints > 0.0 && circlesOverlap bomb.Position bombRadius enemy.Position enemy.Radius then
                    { enemy with HitPoints = max 0.0 (enemy.HitPoints - 40.0); HitFlashTicks = hitFlashTicks }
                else enemy)
        result <- { result with Enemies = enemies } |> takePlayerHit 2 bomb.Position
    { result with Bombs = aged |> List.filter (fun bomb -> not (Set.contains bomb.Id exploded)) }

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
    let enemyGrid = SpatialGrid.build spatialCellSize [ for enemy in result.Enemies do if enemy.HitPoints > 0.0 then toSimPoint enemy.Position, enemy ]
    let maxEnemyRadius = result.Enemies |> List.map (fun enemy -> max 0.0 enemy.Radius) |> List.fold max 0.0
    let contacts = SpatialGrid.queryRadius (toSimPoint result.PlayerPosition) (playerRadius + maxEnemyRadius) enemyGrid |> List.sortBy (fun enemy -> enemy.Id)
    let mutable contactTicks = Map.empty
    for enemy in contacts do
        let ready = enemy.LastContactTick |> Option.forall (fun tick -> result.SimStepCount + 1 - tick >= contactRetickTicks)
        if ready && circlesOverlap result.PlayerPosition playerRadius enemy.Position enemy.Radius then
            let before = result.PlayerHealth
            result <- takePlayerHit enemy.ContactDamage enemy.Position result
            if result.PlayerHealth <> before then contactTicks <- Map.add enemy.Id (result.SimStepCount + 1) contactTicks
    { result with
        EnemyBullets = result.EnemyBullets |> List.filter (fun bullet -> not (Set.contains bullet.Id consumed))
        Enemies = result.Enemies |> List.map (fun enemy ->
            { enemy with
                LastContactTick = Map.tryFind enemy.Id contactTicks |> Option.orElse enemy.LastContactTick
                HitFlashTicks = max 0 (enemy.HitFlashTicks - 1) })
        TotalCombatCandidates = result.TotalCombatCandidates + bullets.Length + contacts.Length }

let private resolveCombat model =
    let burstsBefore = model.BlackHeartBursts
    let model = model |> resolveShotCombat |> resolveBombs |> resolveEnemyDamage
    let burstsThisStep = model.BlackHeartBursts - burstsBefore
    let enemies =
        if burstsThisStep > 0 then
            model.Enemies |> List.map (fun enemy -> { enemy with HitPoints = max 0.0 (enemy.HitPoints - 10.0 * float burstsThisStep) })
        else model.Enemies
    { model with
        Enemies = enemies
        PlayerLifeState = if totalHalfHearts model.PlayerHealth = 0 then Dead else model.PlayerLifeState }

let private stepInput pressedThisTick (model: Model) =
    let resolved = resolveInput model.PlayerPosition pressedThisTick model.Input.Current
    let dodgeStarted = model.PlayerLifeState = Alive && Set.contains dodgeKey pressedThisTick && model.DodgeCooldownTicks = 0
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
    let movedPlayer = Collision.sweepCircle (Some roomBounds) model.Obstacles playerCircle (toSimPoint displacement)
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

    let steppedShots, wallQueries, homingQueries =
        stepShots roomBounds model.Obstacles model.HomingTargets shotSpawns

    let bombPressed = Set.contains qKey pressedThisTick || Set.contains fKey pressedThisTick
    let bombs, currency, nextBombId =
        if bombPressed && model.PlayerCurrency.Bombs > 0 && model.PlayerLifeState = Alive then
            { Id = model.NextBombId; Position = playerPosition; FuseTicks = bombFuseTicks } :: model.Bombs,
            { model.PlayerCurrency with Bombs = model.PlayerCurrency.Bombs - 1 },
            model.NextBombId + 1
        else model.Bombs, model.PlayerCurrency, model.NextBombId

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
        TotalWallQueries = model.TotalWallQueries + wallQueries + 6 * model.Obstacles.Length
        TotalHomingQueries = model.TotalHomingQueries + homingQueries
        EdgeActionCount = model.EdgeActionCount + Set.count pressedThisTick }

// Pure fixed step: integrate the ball by one step, bounce off the top/bottom walls and the paddles,
// score and re-serve on a miss. Positions/velocities are `Vec2`, advanced with `add`/`scale`; the
// ball always stays inside the playfield after the step. This is your `stepSim` — edit it freely.
let private stepSimWithInput pressedThisTick model =
    let model = stepInput pressedThisTick model |> resolveCombat
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

// Fixed-timestep advance: fold the host's real elapsed `dt` into the carried accumulator, drain the
// whole number of `simInterval` steps out of it, and run `stepSim` that many times. `FixedStep.drain`
// is a pure FS.GG.Game.Core primitive (no wall-clock read), so a scripted `dt` sequence replays
// byte-identically. This is the accumulator + stepSim pattern — the shape most games want on Tick.
let private advanceSim dtSeconds (model: Model) =
    let struct (steps, accumulator) =
        FixedStep.drainWith maxFrameTime fixedDt dtSeconds model.SimAccumulator
    let currentKeys = Set.union model.Input.Current.Keys model.Input.Current.Gamepad.Buttons
    let previousKeys = Set.union model.Input.Previous.Keys model.Input.Previous.Gamepad.Buttons
    let pressedThisTick = Set.difference currentKeys previousKeys

    let stepped =
        // mutable: a single unaliased accumulator over a fixed step count is plainer than a fold here.
        let mutable m = model
        for stepIndex in 1..steps do
            m <- stepSimWithInput (if stepIndex = 1 then pressedThisTick else Set.empty) m
        m

    { stepped with
        SimAccumulator = accumulator
        TickCount = model.TickCount + 1
        Input =
            if steps = 0 then model.Input
            else
                { model.Input with
                    Previous = model.Input.Current
                    PressedThisTick = pressedThisTick } }

let init () : Model * AdapterCommand<Msg> = initialModel, Cmd.none

let update msg model : Model * AdapterCommand<Msg> =
    match msg with
    // Identity (issue #458). `Started` ANNOUNCES the initial state; it does not build it —
    // `initialModel` already did. Its whole job is to give the cue seam a transition to look at, so
    // keep this a no-op and put what you want to happen at startup in `AudioCues.forTransition`.
    | Started -> model, Cmd.none
    | Tick dtSeconds -> advanceSim dtSeconds model, Cmd.none
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
    | PointerChanged(position, primaryDown) ->
        { model with Input = { model.Input with Current = withPointer position primaryDown model.Input.Current } }, Cmd.none
    | InputChanged snapshot ->
        { model with Input = { model.Input with Current = snapshot } }, Cmd.none
    | InteractShop slotId -> purchaseShopSlot slotId model |> fst, Cmd.none
    | NoOp -> model, Cmd.none

let subscriptions _ : AdapterSubscription<Msg> list = Sub.none
