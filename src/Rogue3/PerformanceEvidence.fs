module Rogue3.PerformanceEvidence

open System
open System.Diagnostics
open System.Globalization
open System.IO
open System.Security.Cryptography
open System.Text
open System.Text.Json
open System.Text.RegularExpressions
open System.Xml.Linq
open Fsgg.Schemas
open FS.GG.Game.Harness
open FS.GG.UI.KeyboardInput
open FS.GG.UI.Scene
open Rogue3.Model
open Rogue3.Geometry
open Rogue3.View

// ------------------------------------------------------------------------------------------------
// M11: the product's own player-action vocabulary for production journeys.
//
// `JourneyEvent` is a closed nine-case DU owned by FS.GG.Game.Harness, so a `CrossDoor` case cannot
// be added to it. Its action slot is the `'menu` TYPE PARAMETER, which this product used to
// instantiate with `unit` — a type inhabited by exactly one value, and therefore a vocabulary that
// can express nothing. That is why the missing door wiring was inexpressible: no journey event could
// name "cross a door", so no `JourneyDispatch.Unbound` row could ever report it.
//
// Naming an action here does NOT wire it. A scenario that does not bind an action returns
// `JourneyDispatch.Unbound`, and the runner turns that into a failed receipt naming the action.
// ------------------------------------------------------------------------------------------------
[<RequireQualifiedAccess>]
type PlayerAction =
    /// Walk through the doorway on the named wall of the current room.
    | CrossDoor of FloorGeneration.DoorDirection
    /// Spend a key on the `LockedKey` door on the named wall of the current room.
    | UnlockKeyDoor of FloorGeneration.DoorDirection
    /// Use the trapdoor the current room depicts.
    | UseTrapdoor
    /// The M6 particle burst the maximum-content fixture drives.
    | BurstParticles

type WorkloadClass =
    | NormalPlay
    | Stress
    | Throughput
    | LiveCompositor

type Budget =
    { P95Ms: float
      P99Ms: float
      MaximumSceneNodes: int
      MaximumShotSpawnHistory: int
      AllowSustainedCatchUp: bool }

/// The one rogue3-authored performance policy. This is the published Contracts 7.x shape used by
/// SDD, not a template-local mirror. Workload identities and executable definition digests are
/// projected into the completed value below after the workload rows are declared.
let private performanceIntentSeed: PerformanceIntentDeclaration =
    { Id = "PI-GENERATED-GAME"
      Disposition = "active"
      TargetFps = 60
      WorkloadIds = []
      WorkloadDefinitionDigests = []
      MaximumExpectedScale = "20 generated floor rooms plus 40 live player shots, 30 combat actors spanning all eight kinds clustered on the player, 120 enemy bullets, 60 M5 decisions/frame, nine placed obstacles covering all five kinds of which 8 block movement and 7 also block shots, three deterministic shop slots, 740 wall primitives, 2,100 combat candidates, 2,400 homing considerations, multishot 3, 600 pooled particles, 8 enemy-kind symbols, 11 ordered layers, one active camera transition carrying a departed-room shell, four directional doorways per room with one room-wall shell of up to eight solid slabs the player also sweeps, a hidden wall staying unbroken and eight doorway-sensor examinations per frame, six positioned floor pickups scanned twice per frame, and all five pre-M6 visuals. Board item #20 removed the pre-M5 world-state generation, so the free-floating static-AABB list and the 30-enemy legacy list are gone and their scale is carried by the M5 obstacles and actors that replaced them"
      MaxP95Ms = 16.67m
      MaxP99Ms = 25.0m
      MaxCatchUpFrames = 0
      StructuralCostBudgets = [ "scene-nodes<=4096"; "shot-spawn-history<=40"; "combat-candidates<=2520" ]
      RequiredCapability = "bounded-headless-update-and-scene-route"
      LiveCompositorRequired = false
      DeferralIssue = None
      EvidenceRefs = [ "readiness/performance-evidence.json" ]
      Rationale = Some "Generated normal-play declaration; live-compositor evidence remains a separate workload." }

let private maximumSceneNodes =
    performanceIntentSeed.StructuralCostBudgets
    |> List.tryPick (fun entry ->
        match entry.Split("<=", StringSplitOptions.TrimEntries) with
        | [| "scene-nodes"; value |] ->
            match Int32.TryParse value with
            | true, parsed when parsed > 0 -> Some parsed
            | _ -> None
        | _ -> None)
    |> Option.defaultWith (fun () ->
        invalidOp "performance intent must declare structuralCostBudgets entry 'scene-nodes<=<positive integer>'")

let private maximumShotSpawnHistory =
    performanceIntentSeed.StructuralCostBudgets
    |> List.tryPick (fun entry ->
        match entry.Split("<=", StringSplitOptions.TrimEntries) with
        | [| "shot-spawn-history"; value |] ->
            match Int32.TryParse value with
            | true, parsed when parsed > 0 -> Some parsed
            | _ -> None
        | _ -> None)
    |> Option.defaultWith (fun () ->
        invalidOp "performance intent must declare structuralCostBudgets entry 'shot-spawn-history<=<positive integer>'")

/// A deliberate acknowledgement that a representative workload is rogue3-authored.
///
/// Start in `Placeholder`, run `PerformanceEvidence`, then copy the emitted `definitionDigest`
/// into `Authored` only after replacing the starter state/message route. The digest covers the
/// authored definition and measurement policy. Changing either invalidates the acknowledgement
/// and fails closed until the new digest is reviewed and copied.
type WorkloadAuthorship =
    | Placeholder of requiredWork: string
    | Authored of definitionDigest: string

type WorkloadProvenance =
    | RunnerIssuedJourney of JourneyReceipt
    | SyntheticConstructed of reason: string

type CompositionClaim =
    | CompleteComposition
    | ComponentOnlySupplemental of reason: string

type RoutedStimulus =
    { Events: int
      PointerEvents: int
      RawInputSamples: int }

type CapabilityMetric =
    | Observed of value: int
    | Unsupported of reason: string

type CostDriverCategory =
    | Simulation
    | AiPathfindingPerception
    | Input
    | SceneRender
    | UiControl
    | EffectsParticles
    | PersistenceEffectResult
    | HostPresentation

type CostDriverDisposition =
    | RequiredIn of workloadIds: string list
    | MeasuredInUi of routeIds: string list
    | NonPerformance of reason: string

type PerformanceCostDriver =
    { Id: string
      Category: CostDriverCategory
      ScaleSource: string
      MaximumExpected: int
      VisualElement: string option
      Disposition: CostDriverDisposition }

let private m6AdditionalVisualCostDrivers =
    let required id element =
        { Id = id
          Category = SceneRender
          ScaleSource = $"GameplayVisualInventory.{element} production renderer binding"
          MaximumExpected = 1
          VisualElement = Some element
          Disposition = RequiredIn [ "maximum-content" ] }
    [ required "scene.obstacle-rock" "ObstacleRock"
      required "scene.obstacle-tinted-rock" "ObstacleTintedRock"
      required "scene.obstacle-pot" "ObstaclePot"
      required "scene.obstacle-spikes" "ObstacleSpikes"
      required "scene.obstacle-pit" "ObstaclePit"
      required "scene.pickup-coin-1" "PickupCoin1"
      required "scene.pickup-coin-3" "PickupCoin3"
      required "scene.pickup-half-red-heart" "PickupHalfRedHeart"
      required "scene.pickup-key" "PickupKey"
      required "scene.pickup-bomb" "PickupBomb"
      required "scene.pickup-soul-heart" "PickupSoulHeart"
      { required "scene.boss-gnawer" "BossGnawer" with
          Disposition=NonPerformance "one live boss kind per room; covered by production raster/catalog evidence" }
      { required "scene.boss-hollow-choir" "BossHollowChoir" with
          Disposition=NonPerformance "one live boss kind per room; covered by production raster/catalog evidence" }
      required "scene.boss-maw" "BossMaw"
      required "scene.shop-item" "ShopItem"
      required "scene.room-walls" "RoomWalls"
      required "scene.door-open" "DoorOpen"
      required "scene.door-locked-key" "DoorLockedKey"
      required "scene.door-boss-door" "DoorBossDoor"
      required "scene.door-hidden-wall" "DoorHiddenWall"
      // M11: a room's COMBAT LOCK applies to every doorway at once, so a sealed presentation cannot
      // co-exist with an open one in a single frame — a room has four walls and the maximum-content
      // fixture already spends all four on the four floor-graph states. Their structural cost is
      // identical to the measured `DoorOpen`; their appearance is covered by production raster and
      // catalog evidence. Same treatment as the two non-representative boss kinds above.
      { required "scene.door-locked-clear" "DoorLockedClear" with
          Disposition=NonPerformance "the combat lock seals every doorway at once and cannot co-exist with an open door in one frame; covered by production raster/catalog evidence" }
      { required "scene.door-boss-sealed" "DoorBossSealed" with
          Disposition=NonPerformance "the boss lock seals every doorway at once and cannot co-exist with an open door in one frame; covered by production raster/catalog evidence" }
      required "scene.room-drop" "RoomDrop"
      required "scene.room-reward" "RoomReward"
      required "scene.trapdoor" "Trapdoor"
      required "scene.trapdoor-ready" "TrapdoorReady"
      // Board item #55. The shop's interact affordance cannot share a frame with `TrapdoorReady`,
      // and not by convention: both are drawn from where the ONE player is standing, and the two
      // sensors are disjoint by construction. `Model.placementAccepts` rejects any fixture position
      // inside `trapdoorContains`, and the nearest authored candidate row is 56 units from the
      // hatch's edge against an interact reach of `shopSlotRadius + playerRadius` = 33 —
      // `M14ItemGrantTests` asserts that gap over the whole candidate list rather than leaving it as
      // a claim. The maximum-content fixture spends the player's position on the trapdoor, so this
      // element can never be raised in it. Its structural cost is the same shape as the element it
      // cannot co-exist with: one stroked rectangle, four corner circles and one text run. Same
      // treatment, and for the same reason, as the two combat-lock door presentations above.
      { required "scene.shop-slot-ready" "ShopSlotReady" with
          Disposition=NonPerformance "drawn from the single player position, which the maximum-content fixture spends on the trapdoor; the shop and trapdoor sensors are disjoint by placement so the two can never share a frame; identical structural cost to the measured TrapdoorReady, and appearance covered by production raster/catalog evidence" }
      required "scene.placed-bomb" "PlacedBomb"
      required "scene.shadow" "Shadow" ]

/// Independent rogue3 inventory. It is intentionally not derived from `expectedWorkloads`: adding a
/// gameplay visual or cost driver must edit this list, and the coverage gate compares the two sets.
let performanceCostDrivers =
    [ { Id = "simulation.fixed-step"
        Category = Simulation
        ScaleSource = "Model.SimStepCount delta; Tick(1/60) drains two shipped 120 Hz fixed steps"
        MaximumExpected = 2
        VisualElement = None
        Disposition = RequiredIn [ "idle"; "movement-aiming"; "firing"; "effects-fog"; "maximum-content" ] }
      { Id = "input.snapshot-resolution"
        Category = Input
        ScaleSource = "Model.SimStepCount delta while a sampled key/pointer/gamepad snapshot is active; resolution runs once per fixed step"
        MaximumExpected = 2
        VisualElement = None
        Disposition = RequiredIn [ "movement-aiming"; "firing"; "maximum-content" ] }
      { Id = "simulation.shot-spawn"
        Category = Simulation
        ScaleSource = "Model.Instrumentation.TotalShotSpawns delta across one sampled production frame"
        MaximumExpected = 3
        VisualElement = None
        Disposition = RequiredIn [ "firing"; "maximum-content" ] }
      { Id = "state.live-player-shots"
        Category = Simulation
        ScaleSource = "Model.ShotSpawns live range-bounded projectile count"
        MaximumExpected = 40
        VisualElement = None
        Disposition = RequiredIn [ "maximum-content" ] }
      { Id = "generation.floor-room-budget"
        Category = Simulation
        ScaleSource = "Floor.Rooms.Count after production DescendFloor generation; §4.8 hard cap"
        MaximumExpected = 20
        VisualElement = None
        Disposition = RequiredIn [ "floor-generation" ] }
      { Id = "collision.shot-wall-queries"
        Category = Simulation
        ScaleSource = "Model.Instrumentation.TotalWallQueries delta: two fixed steps each cast 40 shots once against the 7 shot-blocking rects of the room's obstacles, and perform two player-axis casts plus four slide contact folds against all 8 movement-blocking rects and, from M13, the 7 solid wall slabs of the room shell (four walls, three of them split by a passable doorway; the fourth carries a hidden wall, which stays solid). Board item #20 moved this from 820: the collider set is derived from Model.Obstacles now, so the shot pass-through filter finally subtracts the Pit the player still collides with - the old free-floating rect list could never match a derived rect and left that filter a no-op"
        MaximumExpected = 740
        VisualElement = None
        Disposition = RequiredIn [ "maximum-content" ] }
      { Id = "simulation.homing-target-considerations"
        Category = Simulation
        ScaleSource = "Model.Instrumentation.TotalHomingQueries delta: two fixed steps each consider 30 stable targets for 40 homing shots"
        MaximumExpected = 2400
        VisualElement = None
        Disposition = RequiredIn [ "maximum-content" ] }
      { Id = "state.static-obstacles"
        Category = Simulation
        ScaleSource = "blockingObstacleRects Model.Obstacles exact count: the grounded-blocking projection the player sweep and the shot-wall filter iterate"
        MaximumExpected = 8
        VisualElement = None
        Disposition = RequiredIn [ "maximum-content" ] }
      { Id = "state.homing-targets"
        Category = Simulation
        ScaleSource = "Model.HomingTargets exact live count"
        MaximumExpected = 30
        VisualElement = None
        Disposition = RequiredIn [ "maximum-content" ] }
      { Id = "state.live-enemies"
        Category = Simulation
        ScaleSource = "Model.Enemies exact live count (HitPoints > 0)"
        MaximumExpected = 30
        VisualElement = None
        Disposition = RequiredIn [ "maximum-content" ] }
      { Id = "state.enemy-bullets"
        Category = Simulation
        ScaleSource = "Model.EnemyBullets legacy maximum baseline ids 1..120; M5 emissions are independently counted"
        MaximumExpected = 120
        VisualElement = None
        Disposition = RequiredIn [ "maximum-content" ] }
      { Id = "projectiles.m5-boss-emitted"
        Category = Simulation
        ScaleSource = "Model.BossBulletEmissions delta: the production phase-three Maw materializes one exact eight-projectile homing ring before its 0.8-second cadence resets"
        MaximumExpected = 8
        VisualElement = None
        Disposition = RequiredIn [ "maximum-content" ] }
      { Id = "state.m5-enemies"
        Category = AiPathfindingPerception
        ScaleSource = "Model.Enemies exact live count spanning every M5 enemy kind"
        MaximumExpected = 30
        VisualElement = None
        Disposition = RequiredIn [ "maximum-content" ] }
      { Id = "ai.m5-decisions"
        Category = AiPathfindingPerception
        ScaleSource = "Model.AiDecisions delta: 30 actors across two fixed steps"
        MaximumExpected = 60
        VisualElement = None
        Disposition = RequiredIn [ "maximum-content" ] }
      { Id = "state.m5-obstacles"
        Category = Simulation
        ScaleSource = "Model.Obstacles exact count: nine placed obstacles spanning all five kinds, eight of which block movement and seven of which also block shots"
        MaximumExpected = 9
        VisualElement = None
        Disposition = RequiredIn [ "maximum-content" ] }
      { Id = "state.m5-shop-slots"
        Category = Simulation
        ScaleSource = "Model.ShopSlots exact generated shop inventory"
        MaximumExpected = 3
        VisualElement = None
        Disposition = RequiredIn [ "maximum-content" ] }
      { Id = "boss.m5-pattern-emissions"
        Category = AiPathfindingPerception
        ScaleSource = "Model.BossPatternEmissions delta from a live phase-three Maw on the production tick route"
        MaximumExpected = 1
        VisualElement = None
        Disposition = RequiredIn [ "maximum-content" ] }
      { Id = "collision.combat-candidates"
        Category = Simulation
        ScaleSource = "Model.Instrumentation.TotalCombatCandidates delta: two fixed steps query 30 spatially overlapping retained shots x 30 enemies, 30 player-contact candidates, and all 120 bullet-player broadphase candidates; all 40 shots still traverse movement/wall/homing. Board item #20 moved this from 2520: the enemy population is a spawnable roster clustered on the player now, with radii 8..22 in place of 30 uniform 64-unit discs no floor can produce, so fewer shots are retained past their pierce budget"
        MaximumExpected = 2100
        VisualElement = None
        Disposition = RequiredIn [ "maximum-content" ] }
      { Id = "scene.player"
        Category = SceneRender
        ScaleSource = "GameplayVisualInventory.Player production renderer binding"
        MaximumExpected = 1
        VisualElement = Some "Player"
        Disposition = RequiredIn [ "idle"; "movement-aiming"; "firing"; "effects-fog"; "maximum-content" ] }
      { Id = "scene.player-shot"
        Category = SceneRender
        ScaleSource = "GameplayVisualInventory.PlayerShot production renderer binding"
        MaximumExpected = 1
        VisualElement = Some "PlayerShot"
        Disposition = RequiredIn [ "firing"; "maximum-content" ] }
      { Id = "scene.enemy-bullet"
        Category = SceneRender
        ScaleSource = "GameplayVisualInventory.EnemyBullet production renderer binding"
        MaximumExpected = 1
        VisualElement = Some "EnemyBullet"
        Disposition = RequiredIn [ "maximum-content" ] }
      // M13: the HUD is inventoried one REGION at a time. `ui.hud-score` is retired; these four
      // inherit its disposition unchanged, one node group each in every steady-state frame.
      { Id = "ui.hud-hearts"
        Category = UiControl
        ScaleSource = "GameplayVisualInventory.HudHearts production renderer binding"
        MaximumExpected = 1
        VisualElement = Some "HudHearts"
        Disposition = RequiredIn [ "firing"; "maximum-content" ] }
      { Id = "ui.hud-currency"
        Category = UiControl
        ScaleSource = "GameplayVisualInventory.HudCurrency production renderer binding"
        MaximumExpected = 1
        VisualElement = Some "HudCurrency"
        Disposition = RequiredIn [ "firing"; "maximum-content" ] }
      { Id = "ui.hud-active-charge"
        Category = UiControl
        ScaleSource = "GameplayVisualInventory.HudActiveCharge production renderer binding"
        MaximumExpected = 1
        VisualElement = Some "HudActiveCharge"
        Disposition = RequiredIn [ "firing"; "maximum-content" ] }
      { Id = "ui.hud-minimap"
        Category = UiControl
        ScaleSource = "GameplayVisualInventory.HudMinimap production renderer binding"
        MaximumExpected = 1
        VisualElement = Some "HudMinimap"
        Disposition = RequiredIn [ "firing"; "maximum-content" ] }
      { Id = "ui.hud-floor-banner"
        Category = UiControl
        ScaleSource = "GameplayVisualInventory.HudFloorBanner production renderer binding"
        MaximumExpected = 1
        VisualElement = Some "HudFloorBanner"
        Disposition =
            NonPerformance
                "a timed floor announcement (FloorNameTicks) that no steady-state workload frame holds; covered by production raster/catalog evidence" }
      // M13: the room being LEFT during a crossing. `maximum-content` holds an active transition for
      // every sampled frame, so the second room shell is measured rather than argued.
      { Id = "scene.departed-room"
        Category = SceneRender
        ScaleSource = "GameplayVisualInventory.DepartedRoom production renderer binding while a room crossing is in flight"
        MaximumExpected = 1
        VisualElement = Some "DepartedRoom"
        Disposition = RequiredIn [ "maximum-content" ] }
      // M13: the four world-space state visuals. The dodge workload commits to a roll on its boot
      // latch, so both player-motion states are measured there; the downed state and an enemy
      // wind-up are transient and mutually exclusive with the live-player workloads.
      { Id = "scene.player-invulnerable"
        Category = SceneRender
        ScaleSource = "GameplayVisualInventory.PlayerInvulnerable production renderer binding during dodge i-frames or post-hit invulnerability"
        MaximumExpected = 1
        VisualElement = Some "PlayerInvulnerable"
        Disposition = RequiredIn [ "effects-fog" ] }
      { Id = "scene.player-dodge-roll"
        Category = SceneRender
        ScaleSource = "GameplayVisualInventory.PlayerDodgeRoll production renderer binding during the committed roll"
        MaximumExpected = 1
        VisualElement = Some "PlayerDodgeRoll"
        Disposition = RequiredIn [ "effects-fog" ] }
      { Id = "scene.player-down"
        Category = SceneRender
        ScaleSource = "GameplayVisualInventory.PlayerDown production renderer binding"
        MaximumExpected = 1
        VisualElement = Some "PlayerDown"
        Disposition =
            NonPerformance
                "the downed state is terminal and mutually exclusive with every live-player workload; covered by production raster/catalog evidence" }
      { Id = "scene.enemy-telegraph"
        Category = SceneRender
        ScaleSource = "GameplayVisualInventory.EnemyTelegraph production renderer binding, one per enemy committed to a wind-up, dash or dive"
        MaximumExpected = 30
        VisualElement = Some "EnemyTelegraph"
        Disposition =
            NonPerformance
                "a wind-up is a transient per-actor state the maximum-content roster does not deterministically hold at a sampled frame; bounded above by the measured state.m5-enemies count and covered by production raster/catalog evidence" }
      // M13: the floor-pickup collection scan is the one M13 addition on the fixed-step hot path.
      { Id = "simulation.floor-pickup-candidates"
        Category = Simulation
        ScaleSource = "Model.Instrumentation.TotalFloorPickupCandidates delta: player-versus-floor-pickup overlap tests across one sampled production frame — two 120 Hz steps against the six drops the maximum-content room carries"
        MaximumExpected = 12
        VisualElement = None
        Disposition = RequiredIn [ "maximum-content" ] }
      { Id = "ui.run-result-overlay"
        Category = UiControl
        ScaleSource = "GameplayVisualInventory.RunResultOverlay production terminal renderer binding"
        MaximumExpected = 1
        VisualElement = Some "RunResultOverlay"
        Disposition = MeasuredInUi [ "run-result" ] }
      { Id = "persistence.meta-profile"
        Category = PersistenceEffectResult
        ScaleSource = "ProfileStore.Store debounces profile mutations and performs one sibling-temp atomic replacement"
        MaximumExpected = 1
        VisualElement = None
        Disposition = NonPerformance "end-run/settings host I/O is event-driven rather than per-frame; real-file debounce and atomic-rename tests cover it" }
      { Id = "scene.floor-background"
        Category = SceneRender
        ScaleSource = "GameplayVisualInventory.FloorBackground production renderer binding"
        MaximumExpected = 1
        VisualElement = Some "FloorBackground"
        Disposition = RequiredIn [ "idle"; "movement-aiming"; "firing"; "effects-fog"; "maximum-content" ] }
      { Id = "effects.pooled-particles"
        Category = EffectsParticles
        ScaleSource = "Model.Particles exact retained live count after production update applies the hard pool cap"
        MaximumExpected = 600
        VisualElement = Some "Particle"
        Disposition = RequiredIn [ "maximum-content" ] }
      { Id = "scene.m6-enemy-grub"
        Category = SceneRender
        ScaleSource = "GameplayVisualInventory.EnemyGrub production token binding"
        MaximumExpected = 1
        VisualElement = Some "EnemyGrub"
        Disposition = RequiredIn [ "maximum-content" ] }
      { Id = "scene.m6-enemy-maggot"
        Category = SceneRender
        ScaleSource = "GameplayVisualInventory.EnemyMaggot production token binding"
        MaximumExpected = 1
        VisualElement = Some "EnemyMaggot"
        Disposition = RequiredIn [ "maximum-content" ] }
      { Id = "scene.m6-enemy-spitter"
        Category = SceneRender
        ScaleSource = "GameplayVisualInventory.EnemySpitter production token binding"
        MaximumExpected = 1
        VisualElement = Some "EnemySpitter"
        Disposition = RequiredIn [ "maximum-content" ] }
      { Id = "scene.m6-enemy-fly"
        Category = SceneRender
        ScaleSource = "GameplayVisualInventory.EnemyFly production token binding"
        MaximumExpected = 1
        VisualElement = Some "EnemyFly"
        Disposition = RequiredIn [ "maximum-content" ] }
      { Id = "scene.m6-enemy-charger"
        Category = SceneRender
        ScaleSource = "GameplayVisualInventory.EnemyCharger production token binding"
        MaximumExpected = 1
        VisualElement = Some "EnemyCharger"
        Disposition = RequiredIn [ "maximum-content" ] }
      { Id = "scene.m6-enemy-turret"
        Category = SceneRender
        ScaleSource = "GameplayVisualInventory.EnemyTurret production token binding"
        MaximumExpected = 1
        VisualElement = Some "EnemyTurret"
        Disposition = RequiredIn [ "maximum-content" ] }
      { Id = "scene.m6-enemy-caster"
        Category = SceneRender
        ScaleSource = "GameplayVisualInventory.EnemyCaster production token binding"
        MaximumExpected = 1
        VisualElement = Some "EnemyCaster"
        Disposition = RequiredIn [ "maximum-content" ] }
      { Id = "scene.m6-enemy-brute"
        Category = SceneRender
        ScaleSource = "GameplayVisualInventory.EnemyBrute production token binding"
        MaximumExpected = 1
        VisualElement = Some "EnemyBrute"
        Disposition = RequiredIn [ "maximum-content" ] }
      { Id = "scene.m6-enemy-symbols"
        Category = SceneRender
        ScaleSource = "Rogue3.Render.enemyTokens exact live M5 enemy projection spanning every EnemyKind"
        MaximumExpected = 8
        VisualElement = None
        Disposition = RequiredIn [ "maximum-content" ] }
      { Id = "scene.m6-ordered-layers"
        Category = SceneRender
        ScaleSource = "Rogue3.Render.layers exact back-to-front layer count consumed by production View.view"
        MaximumExpected = 11
        VisualElement = None
        Disposition = RequiredIn [ "maximum-content" ] }
      { Id = "scene.m6-camera-transition"
        Category = SceneRender
        ScaleSource = "Model.CameraTransition active flag sampled through production view"
        MaximumExpected = 1
        VisualElement = None
        Disposition = RequiredIn [ "maximum-content" ] }
      { Id = "host.presentation"
        Category = HostPresentation
        ScaleSource = "protected live-compositor host"
        MaximumExpected = 1
        VisualElement = None
        Disposition =
            NonPerformance
                "bounded headless evidence cannot measure present/drop/swapchain/vsync; use a live-compositor workload" }
      // M10 §14.14: the same-step secret reveal is the one M10 addition on the fixed-step hot path,
      // so its scan and its live pending set are measured, not argued.
      { Id = "simulation.secret-reveal-candidates"
        Category = Simulation
        ScaleSource = "Model.Instrumentation.TotalSecretRevealCandidates delta: pending secret/adjacent pairs the blast scan examines while resolving one fixed step's detonations, for the representative floor's current room"
        MaximumExpected = 1
        VisualElement = None
        Disposition = RequiredIn [ "secret-reveal" ] }
      // M11: the doorway sensor scan is the one M11 addition on the fixed-step hot path, so its cost
      // is measured rather than argued. A room grid is orthogonal, so a room has at most four doors.
      { Id = "simulation.door-sensor-candidates"
        Category = Simulation
        ScaleSource = "Model.Instrumentation.TotalDoorSensorQueries delta: doorway sensors the fixed-step door scan examines across one sampled production frame — two 120 Hz steps against the at-most four walls of a room"
        MaximumExpected = 8
        VisualElement = None
        Disposition = RequiredIn [ "maximum-content" ] }
      { Id = "state.pending-secrets"
        Category = Simulation
        ScaleSource = "Floor.PendingSecrets live count on the representative production floor; §4.8 places one hidden room on floors 1-2 and two from floor 3, each reachable from its orthogonal neighbours"
        MaximumExpected = 3
        VisualElement = None
        Disposition = RequiredIn [ "secret-reveal" ] }
      { Id = "state.placed-bombs"
        Category = Simulation
        // The product's own bound is the 99 currency cap; this representative workload carries 48
        // live fuses, which is what the observed gate can honestly assert. Raise both together if a
        // workload ever exercises the full cap.
        ScaleSource = "Model.Bombs live fused count carried into one fixed step's chain-detonation resolution; the representative workload holds 48 of the product's 99-bomb cap"
        MaximumExpected = 48
        VisualElement = None
        Disposition = RequiredIn [ "secret-reveal" ] }
      { Id = "determinism.replay-log-entries"
        Category = Simulation
        ScaleSource = "Rogue3.Replay input-log length folded through production Model.update"
        MaximumExpected = 1
        VisualElement = None
        Disposition =
            NonPerformance
                "replay is an offline verification fold over production update, bounded by the authored log length rather than a frame budget; the Release suite exercises it instead of a timed workload" } ]
    @ m6AdditionalVisualCostDrivers

type Workload =
    { Id: string
      Definition: string
      Classification: WorkloadClass
      WarmupFrames: int
      SampleFrames: int
      EventsPerFrame: int
      PointerEventsPerFrame: int
      InitialState: unit -> Model
      MessagesAt: int -> Msg list
      Provenance: WorkloadProvenance
      Composition: CompositionClaim
      CostDriverIds: string list
      Budget: Budget option
      BlockingDebt: string option
      Authorship: WorkloadAuthorship }

type Verdict = { Passed: bool; Reasons: string list }

type WorkloadResult =
    { Workload: Workload
      DefinitionDigest: string
      P50Ms: float
      P95Ms: float
      P99Ms: float
      UpdateCount: int
      PresentCount: CapabilityMetric
      CatchUpFrames: int
      DroppedFrames: CapabilityMetric
      DeclaredEventCount: int
      ObservedEventCount: int
      DeclaredPointerEventCount: int
      ObservedPointerEventCount: int
      RawInputSampleCount: int
      SceneNodeCount: int
      ShotSpawnHistoryCount: int
      ObservedScale: Map<string, int>
      AllocatedBytes: int64
      Verdict: Verdict }

let private classToken =
    function
    | NormalPlay -> "normal"
    | Stress -> "stress"
    | Throughput -> "throughput"
    | LiveCompositor -> "live-compositor"

let private percentile value samples =
    match samples |> List.sort with
    | [] -> 0.0
    | sorted ->
        let index =
            Math.Ceiling(value / 100.0 * float sorted.Length)
            |> int
            |> fun i -> Math.Clamp(i - 1, 0, sorted.Length - 1)

        sorted.[index]

let private sha256Text (text: string) =
    SHA256.HashData(Encoding.UTF8.GetBytes text)
    |> Convert.ToHexString
    |> _.ToLowerInvariant()

let private journeyKind = "produc" + "tion-journey"
let private completeCompositionKind = "produc" + "tion-composition"

let private runnerReceiptToken (receipt: JourneyReceipt) =
    [ string (JourneyReceipt.schemaVersion receipt)
      JourneyReceipt.runnerIdentity receipt
      JourneyReceipt.runnerVersion receipt
      JourneyReceipt.compositionAuthority receipt
      string (JourneyReceipt.origin receipt)
      JourneyReceipt.routeId receipt
      JourneyReceipt.scenarioId receipt
      JourneyReceipt.testId receipt
      string (JourneyReceipt.inputKind receipt)
      JourneyReceipt.inputIdentity receipt
      JourneyReceipt.inputDigest receipt
      JourneyReceipt.scriptDigest receipt
      JourneyReceipt.traceDigest receipt
      JourneyReceipt.initialFingerprintDigest receipt
      JourneyReceipt.terminalFingerprintDigest receipt
      JourneyReceipt.terminalPredicateIdentity receipt
      string (JourneyReceipt.terminalPredicateReached receipt)
      string (JourneyReceipt.result receipt)
      string (JourneyReceipt.steps receipt)
      string (JourneyReceipt.maxSteps receipt) ]
    |> String.concat "|"
    |> sha256Text

let private provenanceToken =
    function
    | RunnerIssuedJourney receipt -> $"{journeyKind}:{runnerReceiptToken receipt}"
    | SyntheticConstructed reason -> $"synthetic-constructed:{reason}"

// Authorship changes rebuild this assembly and therefore change the runner receipt's composition
// authority MVID. Keep that volatile build identity in the critic digest above, but exclude it from
// the source-declaration digest so copying the emitted authorship digest is not circular.
let private provenanceDefinitionToken =
    function
    | RunnerIssuedJourney receipt ->
        [ journeyKind
          JourneyReceipt.routeId receipt
          JourneyReceipt.scenarioId receipt
          JourneyReceipt.testId receipt
          JourneyReceipt.inputIdentity receipt
          JourneyReceipt.inputDigest receipt
          JourneyReceipt.scriptDigest receipt
          JourneyReceipt.traceDigest receipt
          JourneyReceipt.initialFingerprintDigest receipt
          JourneyReceipt.terminalFingerprintDigest receipt
          JourneyReceipt.terminalPredicateIdentity receipt
          string (JourneyReceipt.terminalPredicateReached receipt)
          string (JourneyReceipt.result receipt)
          string (JourneyReceipt.steps receipt)
          string (JourneyReceipt.maxSteps receipt) ]
        |> String.concat "|"
        |> sha256Text
        |> fun digest -> $"{journeyKind}:{digest}"
    | SyntheticConstructed reason -> $"synthetic-constructed:{reason}"

let private compositionToken =
    function
    | CompleteComposition -> completeCompositionKind
    | ComponentOnlySupplemental reason -> $"component-only-supplemental:{reason}"

let private evaluateProvenance workload =
    let validate (receipt: JourneyReceipt) =
        let expectedOrigin = "Produc" + "tionJourney"
        let required =
            [ "runner identity", JourneyReceipt.runnerIdentity receipt
              "runner version", JourneyReceipt.runnerVersion receipt
              "composition authority", JourneyReceipt.compositionAuthority receipt
              "route id", JourneyReceipt.routeId receipt
              "scenario id", JourneyReceipt.scenarioId receipt
              "test id", JourneyReceipt.testId receipt
              "input identity", JourneyReceipt.inputIdentity receipt
              "input digest", JourneyReceipt.inputDigest receipt
              "script digest", JourneyReceipt.scriptDigest receipt
              "trace digest", JourneyReceipt.traceDigest receipt
              "initial fingerprint", JourneyReceipt.initialFingerprintDigest receipt
              "terminal fingerprint", JourneyReceipt.terminalFingerprintDigest receipt
              "terminal predicate identity", JourneyReceipt.terminalPredicateIdentity receipt ]

        [ if JourneyReceipt.schemaVersion receipt <> 1 then
              $"workload '{workload.Id}' runner receipt schema is unsupported"
          if not (String.Equals(string (JourneyReceipt.origin receipt), expectedOrigin, StringComparison.Ordinal)) then
              $"workload '{workload.Id}' receipt did not originate from the shipped journey runner"
          for label, value in required do
              if String.IsNullOrWhiteSpace value then
                  $"workload '{workload.Id}' runner receipt is missing {label}"
          if JourneyReceipt.result receipt <> JourneyResult.Passed then
              $"workload '{workload.Id}' runner receipt did not pass"
          if not (JourneyReceipt.terminalPredicateReached receipt) then
              $"workload '{workload.Id}' runner receipt did not reach its terminal predicate"
          if
              JourneyReceipt.steps receipt <= 0
              || JourneyReceipt.steps receipt > JourneyReceipt.maxSteps receipt
          then
              $"workload '{workload.Id}' runner receipt has invalid bounded steps" ]

    let reasons =
        match workload.Provenance with
        | RunnerIssuedJourney receipt -> validate receipt
        | SyntheticConstructed reason ->
            [ $"workload '{workload.Id}' is synthetic-constructed ({reason}); it may support component/stress/throughput evidence but cannot establish shipped-route normal-play or maximum-scale coverage" ]

    let reasons =
        match workload.Classification, workload.Composition with
        | NormalPlay, ComponentOnlySupplemental reason ->
            $"workload '{workload.Id}' is component-only supplemental ({reason}); it cannot claim complete normal-play composition"
            :: reasons
        | _ -> reasons

    { Passed = List.isEmpty reasons; Reasons = reasons }

let private declarationPattern =
    Regex(
        @"Authorship\s*=\s*(?:Placeholder\s+""[^""]*""|Authored\s+""[^""]*"")",
        RegexOptions.CultureInvariant
    )

let private debtPattern =
    Regex(
        @"BlockingDebt\s*=\s*(?:None|Some\s+""[^""]*"")",
        RegexOptions.CultureInvariant
    )

let private countOccurrences (needle: string) (text: string) =
    let rec loop start count =
        let found = text.IndexOf(needle, start, StringComparison.Ordinal)

        if found < 0 then
            count
        else
            loop (found + needle.Length) (count + 1)

    loop 0 0

/// Fingerprint the executable source block for one workload. This binds the declaration to the
/// actual InitialState/MessagesAt code rather than trusting its prose. The declaration itself is
/// normalized to a sentinel so copying the emitted digest into `Authored` is not circular.
let private workloadSourceFingerprint id =
    let sourcePath = Path.Combine(__SOURCE_DIRECTORY__, "PerformanceEvidence.fs")

    if not (File.Exists sourcePath) then
        None
    else
        let source = File.ReadAllText sourcePath
        let beginMarker = $"// WORKLOAD-SOURCE-BEGIN {id}"
        let endMarker = $"// WORKLOAD-SOURCE-END {id}"
        let start = source.IndexOf(beginMarker, StringComparison.Ordinal)
        let finish = source.IndexOf(endMarker, max 0 (start + beginMarker.Length), StringComparison.Ordinal)

        if
            countOccurrences beginMarker source <> 1
            || countOccurrences endMarker source <> 1
            || start < 0
            || finish < 0
            || finish <= start
        then
            None
        else
            source.Substring(start, finish + endMarker.Length - start)
            |> fun block -> declarationPattern.Replace(block, "Authorship = <declaration>")
            |> fun block -> debtPattern.Replace(block, "BlockingDebt = <debt>")
            |> _.Replace("\r\n", "\n")
            |> _.Trim()
            |> sha256Text
            |> Some

let private modelDefinitionFingerprint (model: Model) =
    // M10: this used `sprintf "%A" model`, which TRUNCATES a collection after 100 elements. The
    // maximum-content fixture carries 600 particles, 120 enemy bullets and 40 shots, so the digest
    // could not distinguish two initial states differing past element 100 — a fingerprint that
    // silently agrees is worse than no fingerprint. `Determinism.encode` walks the same closed
    // structural record with no length limit and the same deterministic Set/Map ordering.
    Rogue3.Determinism.encode model |> _.Replace("\r\n", "\n") |> sha256Text

let private messageDefinitionFingerprint (workload: Workload) =
    // Same truncation hazard: this list is one entry per sampled frame, and the sampled workloads
    // run 120 to 720 frames, so `%A` was fingerprinting only the first 100 frames of the route.
    [ for frame in 0 .. max workload.WarmupFrames workload.SampleFrames - 1 ->
          frame, workload.MessagesAt frame ]
    |> Rogue3.Determinism.encode
    |> _.Replace("\r\n", "\n")
    |> sha256Text

let definitionDigest workload =
    let budget =
        workload.Budget
        |> Option.map (fun b -> $"{b.P95Ms:R}|{b.P99Ms:R}|{b.MaximumSceneNodes}|{b.MaximumShotSpawnHistory}|{b.AllowSustainedCatchUp}")
        |> Option.defaultValue "none"

    let executableSource =
        workloadSourceFingerprint workload.Id
        |> Option.defaultValue "missing-workload-source-block"
    let initialState = workload.InitialState() |> modelDefinitionFingerprint
    let messageState = messageDefinitionFingerprint workload

    let structuralBudgets = String.concat "," performanceIntentSeed.StructuralCostBudgets

    let maxP95 = performanceIntentSeed.MaxP95Ms.ToString(CultureInfo.InvariantCulture)
    let maxP99 = performanceIntentSeed.MaxP99Ms.ToString(CultureInfo.InvariantCulture)

    let intentPolicy =
        $"{performanceIntentSeed.Id}|{performanceIntentSeed.Disposition}|{performanceIntentSeed.TargetFps}|{performanceIntentSeed.MaximumExpectedScale}|{maxP95}|{maxP99}|{performanceIntentSeed.MaxCatchUpFrames}|{structuralBudgets}|{performanceIntentSeed.RequiredCapability}|{performanceIntentSeed.LiveCompositorRequired}"

    let costDriverIds = String.concat "," workload.CostDriverIds

    let canonical =
        $"{workload.Id}|{workload.Definition}|{classToken workload.Classification}|{workload.WarmupFrames}|{workload.SampleFrames}|{workload.EventsPerFrame}|{workload.PointerEventsPerFrame}|{provenanceDefinitionToken workload.Provenance}|{compositionToken workload.Composition}|{costDriverIds}|{budget}|{intentPolicy}|{executableSource}|initial={initialState}|messages={messageState}"

    sha256Text canonical

let private ownerRepoIssue =
    Regex(
        @"^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+#[1-9][0-9]*$",
        RegexOptions.CultureInvariant
    )

let private linkedDebtReference (debt: string) =
    let isGitHubIssueUrl =
        match Uri.TryCreate(debt, UriKind.Absolute) with
        | true, uri when uri.Scheme = Uri.UriSchemeHttps && uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) ->
            let segments = uri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries)
            segments.Length = 4
            && segments.[0] <> ""
            && segments.[1] <> ""
            && segments.[2].Equals("issues", StringComparison.OrdinalIgnoreCase)
            && (match Int32.TryParse segments.[3] with
                | true, number -> number > 0
                | _ -> false)
        | _ -> false

    not (String.IsNullOrWhiteSpace debt)
    && (ownerRepoIssue.IsMatch debt || isGitHubIssueUrl)

/// Expected-workload budget semantics. A linked debt permits deliberate BASELINE CAPTURE, not
/// acceptance: its artifact is retained, but Test/Verify still fail until the active target passes.
/// Only normal-play workloads are budget gates; other classes remain separately classified evidence.
let evaluateBudget workload p95 p99 catchUpFrames sceneNodes shotSpawnHistory =
    let budgetVerdict =
        match workload.Classification, workload.Budget with
        | NormalPlay, None ->
            { Passed = false
              Reasons = [ "normal-play workload has no declared budget" ] }
        | NormalPlay, Some budget ->
            let reasons =
                [ if p95 > budget.P95Ms then
                      $"p95 {p95:F3} ms exceeds {budget.P95Ms:F3} ms"
                  if p99 > budget.P99Ms then
                      $"p99 {p99:F3} ms exceeds {budget.P99Ms:F3} ms"
                  if sceneNodes > budget.MaximumSceneNodes then
                      $"scene nodes {sceneNodes} exceed {budget.MaximumSceneNodes}"
                  if shotSpawnHistory > budget.MaximumShotSpawnHistory then
                      $"shot-spawn history {shotSpawnHistory} exceeds {budget.MaximumShotSpawnHistory}"
                  if catchUpFrames > performanceIntentSeed.MaxCatchUpFrames then
                      $"sustained catch-up observed in {catchUpFrames} frame(s), exceeding declared maximum {performanceIntentSeed.MaxCatchUpFrames}" ]

            { Passed = List.isEmpty reasons
              Reasons =
                if List.isEmpty reasons then
                    []
                else
                    "active normal-play target failed; a linked blocking debt permits baseline capture only, never acceptance"
                    :: reasons }
        | _, _ ->
            { Passed = true
              Reasons = [ "informational non-normal workload; not used as the normal-play budget gate" ] }

    match workload.BlockingDebt with
    | None -> budgetVerdict
    | Some debt when not (linkedDebtReference debt) ->
        { Passed = false
          Reasons =
            "baseline capture requires a linked blocking performance-debt issue (owner/repo#number or https://github.com/owner/repo/issues/number); open/blocking state is validated by the governance network edge"
            :: budgetVerdict.Reasons }
    | Some debt ->
        { Passed = false
          Reasons =
            $"baseline-only-with-linked-debt {debt}; captured evidence does not satisfy acceptance"
            :: budgetVerdict.Reasons }

let evaluateAuthorship workload =
    let actualDigest = definitionDigest workload

    match workloadSourceFingerprint workload.Id, workload.Authorship with
    | None, _ ->
        { Passed = false
          Reasons =
            [ $"workload '{workload.Id}' has no readable WORKLOAD-SOURCE block; executable state/message authorship cannot be verified" ] }
    | Some _, Placeholder requiredWork ->
        { Passed = false
          Reasons = [ $"required workload '{workload.Id}' is still a placeholder: {requiredWork}" ] }
    | Some _, Authored declaredDigest when
        not (String.Equals(declaredDigest, actualDigest, StringComparison.OrdinalIgnoreCase))
        ->
        { Passed = false
          Reasons =
            [ $"authored declaration is stale for workload '{workload.Id}': declared {declaredDigest}, current {actualDigest}; review the changed definition and copy the new digest" ] }
    | Some _, Authored _ -> { Passed = true; Reasons = [] }

let private observeRoutedStimulus message =
    match message with
    | ViewerInput _
    | KeyChanged _
    | InputChanged _ ->
        { Events = 1
          PointerEvents = 0
          RawInputSamples = 1 }
    | PointerChanged _ ->
        { Events = 1
          PointerEvents = 1
          RawInputSamples = 1 }
    | _ ->
        { Events = 0
          PointerEvents = 0
          RawInputSamples = 0 }

let private observeCostScale visualCounts driverId routed beforeModel afterModel =
    match performanceCostDrivers |> List.tryFind (fun driver -> driver.Id = driverId) with
    | Some driver ->
        match driver.Id, driver.Category, driver.VisualElement with
        | "simulation.fixed-step", _, _ -> afterModel.SimStepCount - beforeModel.SimStepCount
        | "simulation.shot-spawn", _, _ -> afterModel.Instrumentation.TotalShotSpawns - beforeModel.Instrumentation.TotalShotSpawns
        | "state.live-player-shots", _, _ -> afterModel.ShotSpawns.Length
        | "generation.floor-room-budget", _, _ -> afterModel.Floor.Rooms.Count
        | "collision.shot-wall-queries", _, _ -> afterModel.Instrumentation.TotalWallQueries - beforeModel.Instrumentation.TotalWallQueries
        | "simulation.homing-target-considerations", _, _ -> afterModel.Instrumentation.TotalHomingQueries - beforeModel.Instrumentation.TotalHomingQueries
        | "state.static-obstacles", _, _ -> (blockingObstacleRects afterModel.Obstacles).Length
        | "state.homing-targets", _, _ -> afterModel.HomingTargets.Length
        | "state.live-enemies", _, _ -> afterModel.Enemies |> List.filter (fun (enemy: Rogue3.Entities.EnemyActor) -> enemy.HitPoints > 0.0) |> List.length
        | "state.enemy-bullets", _, _ -> afterModel.EnemyBullets |> List.filter(fun bullet->bullet.Id<=120) |> List.length
        | "projectiles.m5-boss-emitted", _, _ -> afterModel.BossBulletEmissions - beforeModel.BossBulletEmissions
        | "state.m5-enemies", _, _ -> afterModel.Enemies.Length
        | "ai.m5-decisions", _, _ -> afterModel.AiDecisions - beforeModel.AiDecisions
        | "state.m5-obstacles", _, _ -> afterModel.Obstacles.Length
        | "state.m5-shop-slots", _, _ -> afterModel.ShopSlots.Length
        | "boss.m5-pattern-emissions", _, _ -> afterModel.BossPatternEmissions - beforeModel.BossPatternEmissions
        | "collision.combat-candidates", _, _ -> afterModel.Instrumentation.TotalCombatCandidates - beforeModel.Instrumentation.TotalCombatCandidates
        | "simulation.secret-reveal-candidates", _, _ -> afterModel.Instrumentation.TotalSecretRevealCandidates - beforeModel.Instrumentation.TotalSecretRevealCandidates
        | "simulation.door-sensor-candidates", _, _ -> afterModel.Instrumentation.TotalDoorSensorQueries - beforeModel.Instrumentation.TotalDoorSensorQueries
        | "simulation.floor-pickup-candidates", _, _ -> afterModel.Instrumentation.TotalFloorPickupCandidates - beforeModel.Instrumentation.TotalFloorPickupCandidates
        | "state.pending-secrets", _, _ -> afterModel.Floor.PendingSecrets.Count
        | "state.placed-bombs", _, _ -> max afterModel.Bombs.Length beforeModel.Bombs.Length
        | "effects.pooled-particles", _, _ -> afterModel.Particles.Length
        | "scene.m6-enemy-symbols", _, _ ->
            Rogue3.Render.enemyTokens afterModel
            |> List.map (fun token -> token.R,token.Klass,token.Sigil,token.Threat,token.Speed)
            |> List.distinct
            |> List.length
        | "scene.m6-ordered-layers", _, _ -> Rogue3.Render.layers afterModel |> List.length
        | "scene.m6-camera-transition", _, _ -> if afterModel.CameraTransition.IsSome then 1 else 0
        | _, Input, _ ->
            let applied =
                if afterModel.LastResolvedInput.Move <> zero
                   || afterModel.LastResolvedInput.Aim <> zero
                   || afterModel.LastResolvedInput.FireHeld then
                    afterModel.SimStepCount - beforeModel.SimStepCount
                else 0
            max routed.RawInputSamples applied
        | _, (SceneRender | UiControl), Some elementId ->
            visualCounts |> Map.tryFind elementId |> Option.defaultValue 0
        | _ -> 0
    | None -> 0

let private runWorkload workload =
    let mutable model = workload.InitialState()

    for frame in 0 .. max 0 (workload.WarmupFrames - 1) do
        for message in workload.MessagesAt frame do
            model <- fst (update message model)
        view model |> ignore

    let samples = ResizeArray<float>()
    let beforeBytes = GC.GetAllocatedBytesForCurrentThread()
    let mutable sceneNodes = 0
    let mutable shotSpawnHistory = model.ShotSpawns.Length
    let mutable catchUp = 0
    let mutable observedEvents = 0
    let mutable observedPointerEvents = 0
    let mutable rawInputSamples = 0
    let mutable observedScale = Map.empty
    for frame in 0 .. max 0 (workload.SampleFrames - 1) do
        let sw = Stopwatch.StartNew()
        let beforeModel = model
        let messages = workload.MessagesAt frame
        for message in messages do
            model <- fst (update message model)
        let scene = view model
        sw.Stop()
        let routed =
            messages
            |> List.map observeRoutedStimulus
            |> List.fold (fun total item ->
                { Events = total.Events + item.Events
                  PointerEvents = total.PointerEvents + item.PointerEvents
                  RawInputSamples = total.RawInputSamples + item.RawInputSamples })
                { Events = 0; PointerEvents = 0; RawInputSamples = 0 }
        observedEvents <- observedEvents + routed.Events
        observedPointerEvents <- observedPointerEvents + routed.PointerEvents
        rawInputSamples <- rawInputSamples + routed.RawInputSamples
        let visualCounts =
            Rogue3.Render.renderedElements model
            |> List.groupBy _.ElementId
            |> List.map (fun (elementId, _) -> elementId, 1)
            |> Map.ofList
        observedScale <-
            workload.CostDriverIds
            |> List.fold (fun scales id ->
                let count = observeCostScale visualCounts id routed beforeModel model
                scales
                |> Map.change id (fun previous -> Some(max count (previous |> Option.defaultValue 0)))) observedScale
        samples.Add sw.Elapsed.TotalMilliseconds
        sceneNodes <- max sceneNodes (Scene.describe { Nodes = [ scene ] } |> List.length)
        shotSpawnHistory <- max shotSpawnHistory model.ShotSpawns.Length

        // Catch-up is simulation backlog, not wall-clock slowness (which p95/p99 report).
        if model.SimAccumulator + 1e-12 >= fixedDt then
            catchUp <- catchUp + 1

    let allocated = GC.GetAllocatedBytesForCurrentThread() - beforeBytes
    let values = List.ofSeq samples

    let p50, p95, p99 =
        percentile 50.0 values, percentile 95.0 values, percentile 99.0 values

    let digest = definitionDigest workload
    let authorshipVerdict = evaluateAuthorship workload
    let provenanceVerdict = evaluateProvenance workload
    let budgetVerdict = evaluateBudget workload p95 p99 catchUp sceneNodes shotSpawnHistory
    let declaredEvents = workload.SampleFrames * workload.EventsPerFrame
    let declaredPointerEvents = workload.SampleFrames * workload.PointerEventsPerFrame
    let routeReasons =
        [ if declaredEvents <> observedEvents then
              $"workload '{workload.Id}' declared event count {declaredEvents}, observed routed count {observedEvents}; bind the message to the missing shipped-route seam"
          if declaredPointerEvents <> observedPointerEvents then
              $"workload '{workload.Id}' declared pointer event count {declaredPointerEvents}, observed routed count {observedPointerEvents}; bind the message to the missing shipped-route seam" ]

    { Workload = workload
      DefinitionDigest = digest
      P50Ms = p50
      P95Ms = p95
      P99Ms = p99
      UpdateCount = workload.SampleFrames
      PresentCount = Unsupported "bounded-headless route has no compositor presentation capability"
      CatchUpFrames = catchUp
      DroppedFrames = Unsupported "bounded-headless route has no swapchain/drop observation capability"
      DeclaredEventCount = declaredEvents
      ObservedEventCount = observedEvents
      DeclaredPointerEventCount = declaredPointerEvents
      ObservedPointerEventCount = observedPointerEvents
      RawInputSampleCount = rawInputSamples
      SceneNodeCount = sceneNodes
      ShotSpawnHistoryCount = shotSpawnHistory
      ObservedScale = observedScale
      AllocatedBytes = allocated
      Verdict =
        { Passed =
            authorshipVerdict.Passed
            && provenanceVerdict.Passed
            && budgetVerdict.Passed
            && List.isEmpty routeReasons
          Reasons =
            authorshipVerdict.Reasons
            @ provenanceVerdict.Reasons
            @ routeReasons
            @ budgetVerdict.Reasons } }

let private declaredPackageVersions () =
    let path = Path.Combine(Directory.GetCurrentDirectory(), "Directory.Packages.props")

    if not (File.Exists path) then
        []
    else
        let document = XDocument.Load path

        let properties =
            document.Descendants()
            |> Seq.filter (fun element ->
                not (isNull element.Parent) && element.Parent.Name.LocalName = "PropertyGroup")
            |> Seq.map (fun element -> element.Name.LocalName, element.Value.Trim())
            |> Map.ofSeq

        let resolveVersion (version: string) =
            if version.StartsWith("$(") && version.EndsWith(")") then
                properties
                |> Map.tryFind (version.Substring(2, version.Length - 3))
                |> Option.defaultValue version
            else
                version

        document.Descendants(XName.Get "PackageVersion")
        |> Seq.choose (fun element ->
            let includeAttribute = element.Attribute(XName.Get "Include")
            let versionAttribute = element.Attribute(XName.Get "Version")

            if isNull includeAttribute || isNull versionAttribute then
                None
            else
                Some(includeAttribute.Value, resolveVersion versionAttribute.Value))
        |> Seq.sortBy fst
        |> List.ofSeq

let private normalBudget =
    { P95Ms = float performanceIntentSeed.MaxP95Ms
      P99Ms = float performanceIntentSeed.MaxP99Ms
      MaximumSceneNodes = maximumSceneNodes
      MaximumShotSpawnHistory = maximumShotSpawnHistory
      AllowSustainedCatchUp = performanceIntentSeed.MaxCatchUpFrames > 0 }

/// Bind every M2 performance workload to a runner-issued receipt over the shipped composition:
/// boot `initialModel`, map timestamp-free events to production `Msg`, call production `update`,
/// and reach a real fixed step. The measured workload below remains the longer update+view sample.
/// The door of the current room on the wall `direction` names, if the floor graph has one.
let private doorTowards direction (model: Model) =
    Map.tryFind model.Floor.CurrentRoom model.Floor.Rooms
    |> Option.bind (fun room -> room.Doors |> List.tryFind (fun door -> door.Direction = direction))

let private actionName =
    function
    | PlayerAction.CrossDoor direction -> $"cross-door-{string direction |> fun value -> value.ToLowerInvariant()}"
    | PlayerAction.UnlockKeyDoor direction -> $"unlock-key-door-{string direction |> fun value -> value.ToLowerInvariant()}"
    | PlayerAction.UseTrapdoor -> "use-trapdoor"
    | PlayerAction.BurstParticles -> "burst-particles"

/// The shipped production-journey adapter. Public so the M11 suite can prove that a player action
/// nobody wired reports `JourneyDispatch.Unbound` naming it, rather than being inexpressible.
let journeyAdapterWith maxSteps scenarioId boot terminalSteps : ProductionJourney<Model, ViewerKey, Vec2 * bool option, PlayerAction, unit, Msg, string> =
    { RouteId = "rogue3-m3-combat-health-currency-update-view"
      ScenarioId = scenarioId
      TestId = $"performance-{scenarioId}"
      MaxSteps = maxSteps
      Boot = boot
      MapEvent =
        fun event model ->
            match event with
            | JourneyEvent.Start -> JourneyDispatch.Mapped [ Started ]
            | JourneyEvent.KeyInput(key, pressed) -> JourneyDispatch.Mapped [ KeyChanged(keyName key, pressed) ]
            | JourneyEvent.FixedTick -> JourneyDispatch.Mapped [ Tick fixedDt ]
            | JourneyEvent.PointerInput(position, primaryDown) -> JourneyDispatch.Mapped [ PointerChanged(position, primaryDown) ]
            | JourneyEvent.MenuAction PlayerAction.BurstParticles when scenarioId = "maximum-content" ->
                JourneyDispatch.Mapped [ SpawnM6Particles(650, vec2 640.0 360.0, ParticleTint.Explosion) ]
            // A displayed door action resolves against the LIVE floor graph. If the current room has
            // no such door, the action is unbound rather than silently a no-op.
            | JourneyEvent.MenuAction(PlayerAction.CrossDoor direction as action) ->
                match doorTowards direction model with
                | Some door -> JourneyDispatch.Mapped [ TraverseDoor door.ToRoom ]
                | None -> JourneyDispatch.Unbound(actionName action)
            | JourneyEvent.MenuAction(PlayerAction.UnlockKeyDoor direction as action) ->
                match doorTowards direction model with
                | Some door -> JourneyDispatch.Mapped [ UnlockDoor door.ToRoom ]
                | None -> JourneyDispatch.Unbound(actionName action)
            | JourneyEvent.MenuAction PlayerAction.UseTrapdoor ->
                JourneyDispatch.Mapped [ KeyChanged(keyName (Letter 'E'), true); Tick fixedDt ]
            | JourneyEvent.MenuAction action -> JourneyDispatch.Unbound(actionName action)
            | JourneyEvent.Interact when scenarioId = "maximum-content" ->
                JourneyDispatch.Mapped [ BeginM6RoomTransition RoomSlideDirection.East ]
            // M11: interact is the INTERACT KEY, not a direct descent. `DescendFloor` is guarded, so
            // the only way this reaches a new floor is by the player standing on a real trapdoor.
            | JourneyEvent.Interact -> JourneyDispatch.Mapped [ KeyChanged(keyName (Letter 'E'), true) ]
            | JourneyEvent.Pause -> JourneyDispatch.Unbound "pause"
            | JourneyEvent.Resume -> JourneyDispatch.Unbound "resume"
            | JourneyEvent.EffectResult _ -> JourneyDispatch.Unbound "effect result"
      Update = fun message model -> update message model |> fst
      FixedTick = fun model -> update (Tick fixedDt) model |> fst
      ApplyEffectResult = fun _ model -> model
      IsTerminal = fun model -> if scenarioId = "floor-generation" then model.FloorIndex >= terminalSteps else model.SimStepCount >= terminalSteps
      // The opaque runner receipt binds the complete closed Model, including every M3 population,
      // resource, timer and cost counter; the same structural closure authorship digests use.
      Fingerprint = modelDefinitionFingerprint
      EncodeEvent = string
      EncodeFingerprint = id }

let journeyAdapter scenarioId boot terminalSteps = journeyAdapterWith 4 scenarioId boot terminalSteps

/// Lift a model into a journey boot function OWNED BY THIS ASSEMBLY. The runner refuses an adapter
/// whose composition functions do not share one assembly authority — correctly, because a
/// caller-assembled composition is not the shipped one. `NoInlining` is load-bearing: without it the
/// F# optimizer may copy this closure into the caller's assembly and reintroduce the split.
// ------------------------------------------------------------------------------------------------
// M11 journey boots. Authored HERE, and taking NO caller-supplied model, because the runner requires
// every composition function of a production journey to share one assembly authority — and it is
// right to: a caller-assembled composition is not the shipped one. A product-side helper that lifts a
// caller's model into a boot closure satisfies the letter of that check while defeating its purpose,
// so the product owns the whole entry point instead.
// ------------------------------------------------------------------------------------------------

let private m11StartRoomDoor direction =
    Map.tryFind initialModel.Floor.CurrentRoom initialModel.Floor.Rooms
    |> Option.bind (fun room -> room.Doors |> List.tryFind (fun door -> door.Direction = direction))

/// The starting room with the room behind its north door already cleared — the state a player is in
/// once they have fought through it, and the state in which a door can be crossed in both directions.
let m11RoundTripBoot () =
    match m11StartRoomDoor FloorGeneration.North with
    | Some door -> { initialModel with Floor = FloorGeneration.recordRoomCleared door.ToRoom initialModel.Floor }
    | None -> initialModel

/// The starting room with its north door removed, so asking to cross north is an action the live
/// floor graph cannot bind.
let m11NoNorthDoorBoot () =
    let roomId = initialModel.Floor.CurrentRoom
    match Map.tryFind roomId initialModel.Floor.Rooms with
    | Some room ->
        let doors = room.Doors |> List.filter (fun door -> door.Direction <> FloorGeneration.North)
        { initialModel with Floor = { initialModel.Floor with Rooms = Map.add roomId { room with Doors = doors } initialModel.Floor.Rooms } }
    | None -> initialModel

/// Run a production-journey script through the shipped adapter and return the whole run. `maxSteps`
/// bounds the runner; a boot-to-cross-a-door-and-return script needs far more than a workload's four.
let runPlayerJourneyWith maxSteps scenarioId boot terminalSteps script =
    let terminalPredicateIdentity =
        if scenarioId = "floor-generation" then
            $"model.FloorIndex>={terminalSteps}"
        else
            $"model.SimStepCount>={terminalSteps}"

    Journey.runScriptWithIdentity
        $"{scenarioId}-one-fixed-step"
        terminalPredicateIdentity
        (journeyAdapterWith maxSteps scenarioId boot terminalSteps)
        script

let runPlayerJourney scenarioId boot terminalSteps script =
    runPlayerJourneyWith 4 scenarioId boot terminalSteps script

/// The three M11 journeys, entry points and all. A test names the script; the product names the
/// composition. `[<MethodImpl(NoInlining)>]` keeps the boot closures in THIS assembly: without it the
/// F# optimizer may copy them into the caller and re-split the composition authority the runner
/// checks — a failure whose message points at assembly identity and never at inlining.
[<System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)>]
let runM11RoundTripJourney script =
    runPlayerJourneyWith 900 "m11-door-round-trip" m11RoundTripBoot 300 script

[<System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)>]
let runM11UnboundActionJourney script =
    runPlayerJourneyWith 8 "m11-unbound-action" m11NoNorthDoorBoot 1 script

[<System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)>]
let runM11BoundActionJourney script =
    runPlayerJourneyWith 8 "m11-bound-action" (fun () -> initialModel) 1 script

/// Bind every M2 performance workload to a runner-issued receipt over the shipped composition:
/// boot `initialModel`, map timestamp-free events to production `Msg`, call production `update`,
/// and reach a real fixed step. The measured workload below remains the longer update+view sample.
let private performanceJourneyReceipt scenarioId boot terminalSteps script =
    (runPlayerJourney scenarioId boot terminalSteps script).Receipt

let private movementAimInput =
    { emptyInputSnapshot with
        Keys = Set.singleton (ViewerKeyboard.toKeyId (Letter 'A'))
        MousePosition = Some(vec2 (playfieldWidth / 2.0 + 100.0) (playfieldHeight / 2.0)) }

let private firingInput = { movementAimInput with MousePrimaryDown = true }

let private withInput snapshot = update (InputChanged snapshot) initialModel |> fst

let private firingModel () =
    { initialModel with PlayerStats = { basePlayerStats with Multishot = 3 } }

let private dodgeModel () =
    let dodgeInput = { emptyInputSnapshot with Keys = Set.singleton (ViewerKeyboard.toKeyId Space) }
    { initialModel with Input = { initialModel.Input with Current = dodgeInput } }

let private maximumGamepadInput =
    { emptyInputSnapshot with
        Keys = Set.singleton (ViewerKeyboard.toKeyId ArrowRight) }

let private maximumShotHistory =
    [ 1 .. maximumShotSpawnHistory ]
    |> List.map (fun step ->
        spawnShots step step initialModel.PlayerPosition zero (vec2 1.0 0.0) { basePlayerStats with Homing = 1.0 }
        |> List.exactlyOne
        |> fun shot -> { shot with Pierce = 30; HitsRemaining = 31 })

let private maximumTargets =
    [ for index in 0 .. 29 -> { Id = index + 1; Position = vec2 (644.0 + float (index % 3)) (360.0 + float (index % 3)) } ]

let private maximumEnemyBullets =
    [ for index in 0 .. 119 ->
        // Exactly touching the player broadphase radius: returned by inclusive queryRadius, rejected
        // by strict circleContact, so all 120 candidates remain stable across both fixed steps.
        { Id = index + 1; Position = add initialModel.PlayerPosition (vec2 16.0 0.0); Velocity=zero; Radius = 3.0; Damage = 1;Homing=0.;AgeTicks=0 } ]

/// Board item #20: these 30 actors are the workload's ONLY enemy population now. The legacy list of
/// 30 discs that used to carry the combat broadphase beside them is gone, and reproducing what that
/// list measured needs both of the devices it used, restated over actors a floor can actually spawn.
///
/// POSITION is the first. `collision.combat-candidates` measures the retained shots'
/// `SpatialGrid.queryRadius` against the enemy population, and every retained shot sits at the
/// player's start. The legacy discs sat on the player and bought their reach with a 64-unit radius no
/// floor can spawn; a real roster tops out at `Brute` 22, so the actors are placed in the same tight
/// cluster on the player instead. At their old spread positions the driver collapsed from 2520 to
/// 310 — the workload stopped measuring the thing it exists to measure. The cluster is also the state
/// production converges to, since every kind in the roster seeks the player, so it is the honest
/// maximum rather than a contrivance.
///
/// HIT POINTS are the second, and are why the first attempt at this fixture read `state.live-enemies`,
/// `state.m5-enemies` and `ai.m5-decisions` as 0: forty piercing shots sitting on the cluster kill a
/// floor-6 roster outright inside the first sampled step, and a population that dies is a population
/// that stops being measured. The legacy list carried 10000 hit points for exactly this reason. Same
/// device, same value, now on the actor — the workload measures the broadphase SCAN, not a kill, so
/// every actor must survive the frames it is counted in, which is the rule the M13 floor-pickup
/// fixture states in the same words a few fields below.
let private maximumM5Enemies =
    [ for index in 0 .. 29 ->
        let kind = Rogue3.Entities.roster.[index % Rogue3.Entities.roster.Length]
        { Rogue3.Entities.spawn 6 (1000+index) kind (vec2 (644.0+float(index%3)) (360.0+float(index%3))) with
            HitPoints = 10000.0 } ]

/// Board item #20: the player's static collider set is DERIVED from `Obstacles` now, so the
/// maximum-content fixture places real obstacles instead of carrying a free-floating rect list
/// beside them. Nine obstacles at distinct positions spanning all five kinds: eight block movement
/// (everything but `Spikes`) and seven of those also block shots (`Pit` does not). The old fixture
/// could not exercise the shot pass-through filter at all — its eight legacy rects were 32x32 at
/// hand-written coordinates and could never equal a 40x40 rect derived from an M5 obstacle, so the
/// filter was a no-op in the workload that is supposed to measure it.
let private maximumM5Obstacles =
    [ Rogue3.Entities.ObstacleKind.Rock; Rogue3.Entities.ObstacleKind.TintedRock
      Rogue3.Entities.ObstacleKind.Pot; Rogue3.Entities.ObstacleKind.Rock
      Rogue3.Entities.ObstacleKind.TintedRock; Rogue3.Entities.ObstacleKind.Pot
      Rogue3.Entities.ObstacleKind.Rock; Rogue3.Entities.ObstacleKind.Pit
      Rogue3.Entities.ObstacleKind.Spikes ]
    |> List.mapi (fun index kind ->
        Rogue3.Entities.obstacle kind index
        |> Rogue3.Entities.obstacleAt (vec2 (100.0 + float index * 140.0) 24.0))

let private maximumM5ShopSlots =
    let slots,_,_ = Rogue3.Entities.generateShop (FS.GG.Game.Core.Rng.ofSeed 0xA55AUL) (Rogue3.Entities.itemPool [])
    slots

let private maximumContentModel () =
    let maw = Rogue3.Entities.spawnBoss 9999 Rogue3.Entities.BossKind.Maw (vec2 1000. 600.)
    let maximumFloor = initialModel.Floor
    let fixture =
        { withInput maximumGamepadInput with
            ShotSpawns = maximumShotHistory
            Instrumentation = { zeroInstrumentation with TotalShotSpawns = maximumShotSpawnHistory }
            NextShotId = maximumShotSpawnHistory + 1
            PlayerStats = { basePlayerStats with Homing = 1.0; Multishot = 3; Pierce = 30 }
            HomingTargets = maximumTargets
            EnemyBullets = maximumEnemyBullets
            Bombs = [ { Id=9000;Position=vec2 700.0 390.0;FuseTicks=10000 } ]
            Enemies = maximumM5Enemies
            Obstacles = maximumM5Obstacles
            ShopSlots = maximumM5ShopSlots
            // M13: drops are POSITIONED, so the maximum-content fixture places all six well clear of
            // the player at the room centre — the workload measures the collection SCAN, not a
            // collection, so every pickup must survive the sampled frames it is counted in.
            ObstacleDrops =
                [ { Id=9101;Room=0;Kind=Rogue3.Entities.PickupKind.Coin1;Position=vec2 200.0 620.0 }
                  { Id=9102;Room=0;Kind=Rogue3.Entities.PickupKind.Coin3;Position=vec2 280.0 620.0 }
                  { Id=9103;Room=0;Kind=Rogue3.Entities.PickupKind.HalfRedHeart;Position=vec2 360.0 620.0 }
                  { Id=9104;Room=0;Kind=Rogue3.Entities.PickupKind.Key;Position=vec2 440.0 620.0 }
                  { Id=9105;Room=0;Kind=Rogue3.Entities.PickupKind.Bomb;Position=vec2 520.0 620.0 }
                  { Id=9106;Room=0;Kind=Rogue3.Entities.PickupKind.SoulHeart;Position=vec2 600.0 620.0 } ]
            Boss = Some {maw with HitPoints=100.0;Phase=3;PatternTicksLeft=1}
            // M13: the transition names the room it departed, which is what lets the renderer draw a
            // second room shell behind the slide. Room 0 is the start room of the generated floor.
            CameraTransition = Some { Direction=RoomSlideDirection.East; ElapsedTicks=0; FromRoom=0 }
            // M11: the maximum a SINGLE room can present. A room grid is orthogonal, so a room has at
            // most four doorways, and the four floor-graph door states each take one wall. The
            // combat-lock presentations are deliberately absent: a lock seals every doorway at once,
            // so it cannot co-exist with an open door in one frame (see the cost-driver dispositions).
            Floor =
                { maximumFloor with
                    Rooms =
                        Map.add
                            maximumFloor.CurrentRoom
                            { maximumFloor.Rooms.[maximumFloor.CurrentRoom] with
                                // The trapdoor is drawn from the FLOOR fixture (so what a player sees
                                // is what the descent guard tests), which means the maximal fixture has
                                // to record it rather than only setting the loaded room's flag.
                                Fixtures = maximumFloor.Rooms.[maximumFloor.CurrentRoom].Fixtures @ [ FloorGeneration.Trapdoor ]
                                Doors =
                                    [ { ToRoom=901; Direction=FloorGeneration.North; State=FloorGeneration.Open }
                                      { ToRoom=902; Direction=FloorGeneration.East; State=FloorGeneration.LockedKey }
                                      { ToRoom=903; Direction=FloorGeneration.South; State=FloorGeneration.BossDoor }
                                      { ToRoom=904; Direction=FloorGeneration.West; State=FloorGeneration.HiddenWall } ] }
                            maximumFloor.Rooms }
            Room =
                { IsBoss=true; Cleared=false
                  Doors=List.replicate 4 Rogue3.Entities.DoorState.Open
                  LiveEnemyIds=maximumM5Enemies |> List.map _.Id |> Set.ofList
                  Drop=Some Rogue3.Entities.PickupKind.Key
                  Reward=Some Rogue3.Entities.baseItems.Head
                  Trapdoor=true } }
    let populated=update (SpawnM6Particles(650, vec2 640.0 360.0, ParticleTint.Explosion)) fixture |> fst
    {populated with Particles=populated.Particles|>List.map(fun particle->{particle with LifetimeTicks=10000})}

// Product-owned canonical representative factory at the journey boot seam. It is not the ordinary
// player boot; its role is to make maximum authored content reachable through the same production
// update/view composition without caller-authored receipt labels or hashes.
let private maximumContentJourneyBoot () = maximumContentModel ()

// Canonical representative generation boot shared by the timed workload and its runner receipt.
//
// M11: `DescendFloor` is now guarded by the state it depicts, so the boot STAGES THE ROUTE A PLAYER
// TAKES — the floor's boss room is cleared, which is what creates the trapdoor fixture, the room is
// entered through the production seam, and the player stands on the trapdoor. The measured workload
// then descends exactly the way a run descends.
let private bossRoomOf (floor: FloorGeneration.Floor) =
    floor.Rooms
    |> Map.toList
    |> List.tryPick (fun (id, room) -> if room.RoomType = FloorGeneration.Boss then Some id else None)

let private standOnTrapdoorOfBossRoom (model: Model) =
    match bossRoomOf model.Floor with
    | Some bossId ->
        { model with Floor = FloorGeneration.clearBoss bossId model.Floor }
        |> loadM5Room bossId
        |> fun staged -> { staged with PlayerPosition = trapdoorCenter }
    | None -> model

/// Boss-room ids of the floors the generation workload descends through, precomputed OUTSIDE the
/// sampled window. Room ids and room types come from the layout walk, which is a function of the run
/// seed and floor index alone — the item pool only threads the fixture draws — so a fresh `generate`
/// names the same boss room the workload's pooled descent will.
let private floorGenerationBossRooms =
    [| for floorIndex in 9 .. 80 ->
         (FloorGeneration.generate initialModel.RunSeed floorIndex).Floor
         |> bossRoomOf
         |> Option.defaultValue 0 |]

let private floorGenerationModel () =
    let staged = standOnTrapdoorOfBossRoom { initialModel with FloorIndex = 8 }
    // This workload cannot use `WarmupFrames` — `MessagesAt` is frame-indexed and both phases start
    // at frame 0 over one carried model, so a warmup would offset the staged boss-room ids from the
    // floors actually reached. Warm the descent path here instead, on a DISCARDED copy: `InitialState`
    // is evaluated before the stopwatch, so this is warmup in the ordinary sense and the returned
    // model is the pristine staged one.
    let mutable warm = staged
    for frame in 0..3 do
        let bossRoom = floorGenerationBossRooms.[frame]
        warm <- update DescendFloor warm |> fst
        warm <- update (BossCleared bossRoom) warm |> fst
        warm <- update (EnterM5Room bossRoom) warm |> fst
    view warm |> ignore
    staged

// M10 §14.14 representative state. The player stands in the room that borders a hidden secret, and
// the fuses are staggered 1..N so EXACTLY ONE bomb detonates per fixed step: every sampled step
// therefore pays the pending-secret scan the milestone added to the hot path. The grid bombs sit on
// a 100 px lattice inset 150 px from every wall — wider than the 90 px blast radius, so nothing
// chain-detonates and the one-per-step shape holds. The bomb that actually reaches the shared wall
// is fused LAST, so the reveal itself lands inside the sampled window rather than in warmup, and the
// earlier samples still scan the full live pending set.
let private secretRevealBombGrid =
    [ for column in 0 .. 9 do
        for row in 0 .. 4 -> vec2 (150.0 + float column * 100.0) (150.0 + float row * 100.0) ]
    |> List.truncate 48

let private secretRevealModel () =
    let baseModel = maximumContentModel ()
    let floor = baseModel.Floor

    let adjacentRoom, wall =
        match floor.PendingSecrets |> Map.toList with
        | (struct (adjacent, secret), _) :: _ ->
            let direction =
                FloorGeneration.roomDirection adjacent secret floor
                |> Option.defaultValue FloorGeneration.North
            adjacent, wallMidpoint direction
        | [] -> floor.CurrentRoom, wallMidpoint FloorGeneration.North

    let bombs =
        (secretRevealBombGrid @ [ wall ])
        |> List.mapi (fun index position -> { Id = 7000 + index; Position = position; FuseTicks = index + 1 })

    { baseModel with
        Floor = { floor with CurrentRoom = adjacentRoom }
        Bombs = bombs
        PlayerPosition = vec2 (playfieldWidth / 2.0) (playfieldHeight / 2.0) }

let private secretRevealJourneyBoot () = secretRevealModel ()

/// REQUIRED PRODUCT AUTHORING. Every untouched row is deliberately a failing placeholder.
///
/// For each row: replace `InitialState` and `MessagesAt` with representative rogue3 state/messages,
/// rewrite `Definition` to name that route, run PerformanceEvidence once, review the emitted
/// `definitionDigest`, then change `Placeholder` to `Authored "<digest>"`. The measurement always
/// drives the real `update` + scene `view` route; there is no local statistics-only escape hatch.
///
/// Board item #60 re-derived all seven. `definitionDigest` folds in `modelDefinitionFingerprint`,
/// which is `Determinism.encode` of the workload's initial `Model` — and `encode` writes record
/// type names and FIELD names structurally. So moving the seven `Total*` counters into
/// `InstrumentationCounters` and dropping the `M5`/`M6` prefixes moved every one of these seven,
/// with no behaviour change whatsoever. That is the reason this reshape was done as ONE item: the
/// cost is per change-event, not per field, and splitting it would have paid it twice.
///
/// The digest moving is therefore expected and proves nothing on its own. What was REVIEWED is the
/// rest of the artifact, which did NOT move: every workload reported exactly one failing reason
/// (this stale declaration) and zero cost-driver problems, and every observed cost equalled its
/// declared `MaximumExpected` — including all of `maximum-content`, where `costDriverProblems`
/// demands EXACT equality rather than a ceiling. That last part is the load-bearing check on this
/// particular change: the seven counters are now written through a nested copy-and-update, and an
/// increment dropped in that rewrite would surface as `collision.combat-candidates`,
/// `collision.shot-wall-queries`, `simulation.homing-target-considerations`,
/// `simulation.shot-spawn`, `simulation.door-sensor-candidates`,
/// `simulation.floor-pickup-candidates` or `simulation.secret-reveal-candidates` missing its exact
/// value. All seven matched. A green digest copy without that reading is how a reshape ships a
/// silently unwired counter.
let expectedWorkloads =
    [ // WORKLOAD-SOURCE-BEGIN idle
      { Id = "idle"
        Definition = "M2 idle: production Tick(1/60) drains two 120 Hz movement/projectile steps with no actors, then builds the complete logical view"
        Classification = NormalPlay
        WarmupFrames = 20
        SampleFrames = 120
        EventsPerFrame = 0
        PointerEventsPerFrame = 0
        InitialState = (fun () -> initialModel)
        MessagesAt = (fun _ -> [ Tick(1.0 / 60.0) ])
        Provenance = RunnerIssuedJourney(performanceJourneyReceipt "idle" (fun () -> initialModel) 1 [ JourneyEvent.FixedTick ])
        Composition = CompleteComposition
        CostDriverIds = [ "simulation.fixed-step"; "scene.player"; "scene.floor-background" ]
        Budget = Some normalBudget
        BlockingDebt = None
        Authorship = Authored "b356a97739c1b09e416d61a99e53089d8d2dac6cde35e4cac96249939a40a42d" }
      // WORKLOAD-SOURCE-END idle
      // WORKLOAD-SOURCE-BEGIN movement-aiming
      { Id = "movement-aiming"
        Definition = "M2 movement+aiming: sampled production A-key and pointer messages drive acceleration and independent aim across two 120 Hz steps, then build the complete logical view"
        Classification = NormalPlay
        WarmupFrames = 20
        SampleFrames = 120
        EventsPerFrame = 2
        PointerEventsPerFrame = 1
        InitialState = (fun () -> initialModel)
        MessagesAt =
            (fun _ ->
                [ KeyChanged(keyName (Letter 'A'), true)
                  PointerChanged(movementAimInput.MousePosition.Value, None)
                  Tick(1.0 / 60.0) ])
        Provenance =
            RunnerIssuedJourney(
                performanceJourneyReceipt
                    "movement-aiming"
                    (fun () -> initialModel)
                    1
                    [ JourneyEvent.KeyInput(Letter 'A', true)
                      JourneyEvent.PointerInput(movementAimInput.MousePosition.Value, None)
                      JourneyEvent.FixedTick ])
        Composition = CompleteComposition
        CostDriverIds =
            [ "simulation.fixed-step"
              "input.snapshot-resolution"
              "scene.player"
              "scene.floor-background" ]
        Budget = Some normalBudget
        BlockingDebt = None
        Authorship = Authored "0199eee0866cdb8c82047d6bc207314ae0311490016f7808b622edf5c96a2156" }
      // WORKLOAD-SOURCE-END movement-aiming
      // WORKLOAD-SOURCE-BEGIN firing
      { Id = "firing"
        Definition = "M2 firing: sampled production A-key and primary-pointer messages drive acceleration, centered three-shot multishot, velocity inheritance, and two 120 Hz projectile steps before the complete logical view"
        Classification = NormalPlay
        WarmupFrames = 20
        SampleFrames = 120
        EventsPerFrame = 2
        PointerEventsPerFrame = 1
        InitialState = firingModel
        MessagesAt =
            (fun _ ->
                [ KeyChanged(keyName (Letter 'A'), true)
                  PointerChanged(firingInput.MousePosition.Value, Some true)
                  Tick(1.0 / 60.0) ])
        Provenance =
            RunnerIssuedJourney(
                performanceJourneyReceipt
                    "firing"
                    firingModel
                    1
                    [ JourneyEvent.KeyInput(Letter 'A', true)
                      JourneyEvent.PointerInput(firingInput.MousePosition.Value, Some true)
                      JourneyEvent.FixedTick ])
        Composition = CompleteComposition
        CostDriverIds =
            [ "simulation.fixed-step"
              "input.snapshot-resolution"
              "simulation.shot-spawn"
              "scene.player"
              "scene.player-shot"
              "ui.hud-hearts"
              "ui.hud-currency"
              "ui.hud-active-charge"
              "ui.hud-minimap"
              "scene.floor-background" ]
        Budget = Some normalBudget
        BlockingDebt = None
        Authorship = Authored "f09c0add78ce4af79eb95ce46e67476934fd892e6c1cca682f3bdb760ecf0f7f" }
      // WORKLOAD-SOURCE-END firing
      // WORKLOAD-SOURCE-BEGIN effects-fog
      { Id = "effects-fog"
        Definition = "M2 dodge commitment: a boot-latched Space edge starts one roll, then two production Tick(1/120) messages advance i-frame, roll, and cooldown timers before one complete logical view"
        Classification = NormalPlay
        WarmupFrames = 20
        SampleFrames = 120
        EventsPerFrame = 0
        PointerEventsPerFrame = 0
        InitialState = dodgeModel
        MessagesAt = (fun _ -> [ Tick fixedDt; Tick fixedDt ])
        Provenance =
            RunnerIssuedJourney(
                performanceJourneyReceipt
                    "effects-fog"
                    dodgeModel
                    2
                    [ JourneyEvent.FixedTick; JourneyEvent.FixedTick ])
        Composition = CompleteComposition
        CostDriverIds =
            [ "simulation.fixed-step"; "scene.player"; "scene.floor-background"
              // M13: this workload IS the dodge, so it is where the two player-motion state visuals
              // are measured rather than asserted about.
              "scene.player-invulnerable"; "scene.player-dodge-roll" ]
        Budget = Some normalBudget
        BlockingDebt = None
        Authorship = Authored "194b07bdf452eacd93e9600364f260e0246c8af11ba2956b32c1d46911c70495" }
      // WORKLOAD-SOURCE-END effects-fog
      // WORKLOAD-SOURCE-BEGIN floor-generation
      { Id = "floor-generation"
        Definition = "M4 maximum bounded floor generation through the guarded trapdoor route: production DescendFloor derives MapGen.floorSeed, executes bounded room placement with 20-room cap, assigns templates/threat/specials/fixtures and replaces room-local state, then the next floor's boss room is cleared and entered so the player again stands on a real trapdoor"
        Classification = NormalPlay
        // Warmup is zero deliberately. `MessagesAt` is frame-indexed and the warmup and sample phases
        // both start at frame 0 over ONE carried model, so a non-zero warmup would offset the staged
        // boss-room ids from the floors actually reached and silently stop descending. `update` and
        // `view` are already warm here: four workloads run before this one.
        WarmupFrames = 0
        SampleFrames = 40
        EventsPerFrame = 0
        PointerEventsPerFrame = 0
        InitialState = floorGenerationModel
        MessagesAt =
            (fun frame ->
                let bossRoom = floorGenerationBossRooms.[min frame (floorGenerationBossRooms.Length - 1)]
                [ DescendFloor; BossCleared bossRoom; EnterM5Room bossRoom ])
        Provenance =
            RunnerIssuedJourney(
                performanceJourneyReceipt
                    "floor-generation"
                    floorGenerationModel
                    9
                    [ JourneyEvent.Interact; JourneyEvent.FixedTick ])
        Composition = CompleteComposition
        CostDriverIds = [ "generation.floor-room-budget"; "scene.player"; "scene.floor-background" ]
        Budget = Some normalBudget
        BlockingDebt = None
        Authorship = Authored "870bbf7aa5f78efc7a9f85986201a0b6bc9e7d031f7f07538136e5d700d13b8d" }
      // WORKLOAD-SOURCE-END floor-generation
      // WORKLOAD-SOURCE-BEGIN maximum-content
      { Id = "maximum-content"
        Definition = "M6 canonical maximum fixture through production journey/update/view: inherited M5 maximum combat plus eight live enemy symbols, exactly 600 long-lived retained particles, eleven ordered render layers, and one active 0.35-second room camera transition"
        Classification = NormalPlay
        WarmupFrames = 20
        SampleFrames = 720
        EventsPerFrame = 1
        PointerEventsPerFrame = 0
        InitialState = maximumContentModel
        MessagesAt =
            (fun _ ->
                [ BeginM6RoomTransition RoomSlideDirection.East
                  KeyChanged(keyName ArrowRight, true)
                  Tick(1.0 / 60.0) ])
        Provenance =
            RunnerIssuedJourney(
                performanceJourneyReceipt
                    "maximum-content"
                    maximumContentJourneyBoot
                    1
                    [ JourneyEvent.MenuAction PlayerAction.BurstParticles
                      JourneyEvent.Interact
                      JourneyEvent.KeyInput(ArrowRight, true)
                      JourneyEvent.FixedTick ])
        Composition = CompleteComposition
        CostDriverIds =
            [ "simulation.fixed-step"
              "input.snapshot-resolution"
              "simulation.shot-spawn"
              "state.live-player-shots"
              "collision.shot-wall-queries"
              "simulation.homing-target-considerations"
              "state.static-obstacles"
              "state.homing-targets"
              "state.live-enemies"
              "state.enemy-bullets"
              "projectiles.m5-boss-emitted"
              "state.m5-enemies"
              "ai.m5-decisions"
              "state.m5-obstacles"
              "state.m5-shop-slots"
              "boss.m5-pattern-emissions"
              "collision.combat-candidates"
              "scene.obstacle-rock"
              "scene.obstacle-tinted-rock"
              "scene.obstacle-pot"
              "scene.obstacle-spikes"
              "scene.obstacle-pit"
              "scene.pickup-coin-1"
              "scene.pickup-coin-3"
              "scene.pickup-half-red-heart"
              "scene.pickup-key"
              "scene.pickup-bomb"
              "scene.pickup-soul-heart"
              "scene.boss-maw"
              "scene.shop-item"
              "simulation.door-sensor-candidates"
              "simulation.floor-pickup-candidates"
              "scene.departed-room"
              "scene.room-walls"
              "scene.door-open"
              "scene.door-locked-key"
              "scene.door-boss-door"
              "scene.door-hidden-wall"
              "scene.room-drop"
              "scene.room-reward"
              "scene.trapdoor"
              "scene.trapdoor-ready"
              "scene.placed-bomb"
              "scene.shadow"
              "effects.pooled-particles"
              "scene.m6-enemy-grub"
              "scene.m6-enemy-maggot"
              "scene.m6-enemy-spitter"
              "scene.m6-enemy-fly"
              "scene.m6-enemy-charger"
              "scene.m6-enemy-turret"
              "scene.m6-enemy-caster"
              "scene.m6-enemy-brute"
              "scene.m6-enemy-symbols"
              "scene.m6-ordered-layers"
              "scene.m6-camera-transition"
              "scene.player"
              "scene.player-shot"
              "scene.enemy-bullet"
              "ui.hud-hearts"
              "ui.hud-currency"
              "ui.hud-active-charge"
              "ui.hud-minimap"
              "scene.floor-background" ]
        Budget = Some normalBudget
        BlockingDebt = None
        Authorship = Authored "8698dfe7467d727b6ff556961e237a70733ad5ae5f88c6cc514eb5dff7058ba5" }
      // WORKLOAD-SOURCE-END maximum-content
      // WORKLOAD-SOURCE-BEGIN secret-reveal
      { Id = "secret-reveal"
        Definition = "M10 same-step secret reveal: inherited maximum content plus staggered fuses so production Tick(1/120) detonates exactly one bomb per sampled fixed step, scanning the live pending-secret set and carving the reciprocal doors and graph adjacency inside that same step before the complete logical view"
        Classification = NormalPlay
        WarmupFrames = 0
        SampleFrames = 48
        EventsPerFrame = 0
        PointerEventsPerFrame = 0
        InitialState = secretRevealModel
        MessagesAt = (fun _ -> [ Tick fixedDt ])
        Provenance = RunnerIssuedJourney(performanceJourneyReceipt "secret-reveal" secretRevealJourneyBoot 1 [ JourneyEvent.FixedTick ])
        Composition = CompleteComposition
        CostDriverIds =
            [ "simulation.fixed-step"
              "simulation.secret-reveal-candidates"
              "state.pending-secrets"
              "state.placed-bombs"
              "scene.player"
              "scene.floor-background" ]
        Budget = Some normalBudget
        BlockingDebt = None
        Authorship = Authored "04d753f2fc7cbdc4b7b5fe9b269d47af33202d3bd995cc79b15b668f449705d3" }
      // WORKLOAD-SOURCE-END secret-reveal
      ]

let performanceIntentDeclaration =
    { performanceIntentSeed with
        WorkloadIds = expectedWorkloads |> List.map _.Id
        WorkloadDefinitionDigests =
            expectedWorkloads
            |> List.map (fun workload -> $"{workload.Id}=sha256:{definitionDigest workload}") }

let private duplicateValues values =
    values
    |> List.countBy id
    |> List.choose (fun (value, count) -> if count > 1 then Some value else None)

let private requiredNormalWorkloadIds =
    [ "idle"; "movement-aiming"; "firing"; "effects-fog"; "floor-generation"; "maximum-content"; "secret-reveal" ]

let uiEvidenceProblems (path:string) =
    if not(File.Exists path) then [ $"measured UI route artifact is missing: {path}" ],"missing"
    else
        let bytes=File.ReadAllBytes path
        let digest=SHA256.HashData bytes|>Convert.ToHexString|>_.ToLowerInvariant()
        try
            use document=JsonDocument.Parse bytes
            let routes=
                document.RootElement.GetProperty("routes").EnumerateArray()
                |> Seq.map(fun route->route.GetProperty("id").GetString(),route.Clone())
                |> Map.ofSeq
            let problems=
                [ for driver in performanceCostDrivers do
                    match driver.Disposition with
                    | MeasuredInUi routeIds ->
                        for routeId in routeIds do
                            match Map.tryFind routeId routes with
                            | None -> $"cost driver '{driver.Id}' names missing measured UI route '{routeId}'"
                            | Some route ->
                                if not(route.GetProperty("passed").GetBoolean()) then
                                    $"cost driver '{driver.Id}' measured UI route '{routeId}' did not pass"
                                if routeId="run-result" then
                                    let scale=route.GetProperty("observedScale")
                                    let actions=scale.GetProperty("boundActionIds").EnumerateArray()|>Seq.map _.GetString()|>Set.ofSeq
                                    if scale.GetProperty("controlNodes").GetInt32()<>9
                                       || scale.GetProperty("boundControls").GetInt32()<>3
                                       || scale.GetProperty("summaryTextFields").GetInt32()<>5
                                       || actions<>Set["result-new-run";"result-retry-seed";"result-title"] then
                                        $"cost driver '{driver.Id}' measured UI route '{routeId}' has stale or under-scale production output"
                    | _ -> () ]
            problems,digest
        with error -> [ $"measured UI route artifact is unreadable: {error.Message}" ],digest

let private costDriverProblems (results: WorkloadResult list) uiProblems =
    let workloadById = expectedWorkloads |> List.map (fun workload -> workload.Id, workload) |> Map.ofList
    let resultById = results |> List.map (fun result -> result.Workload.Id, result) |> Map.ofList
    let driverById = performanceCostDrivers |> List.map (fun driver -> driver.Id, driver) |> Map.ofList
    let duplicateDriverIds = performanceCostDrivers |> List.map _.Id |> duplicateValues
    let inventoryVisuals =
        performanceCostDrivers
        |> List.choose _.VisualElement
        |> List.sort
    let shippedVisuals =
        GameplayVisualInventory.all
        |> List.map GameplayVisualInventory.elementId
        |> List.sort
    let duplicateDriverText = String.concat ", " duplicateDriverIds
    let shippedVisualText = String.concat "," shippedVisuals
    let inventoryVisualText = String.concat "," inventoryVisuals

    [ yield! uiProblems
      if not (List.isEmpty duplicateDriverIds) then
          $"duplicate performance cost-driver ids: {duplicateDriverText}"
      if inventoryVisuals <> (List.distinct inventoryVisuals) then
          "duplicate visual-element bindings in the performance cost-driver inventory"
      if inventoryVisuals <> shippedVisuals then
          $"performance visual coverage differs from GameplayVisualInventory; required={shippedVisualText}; bound={inventoryVisualText}"
      for driver in performanceCostDrivers do
          if String.IsNullOrWhiteSpace driver.ScaleSource || driver.MaximumExpected <= 0 then
              $"cost driver '{driver.Id}' has no inspectable positive scale source"

          match driver.Disposition with
          | NonPerformance reason when String.IsNullOrWhiteSpace reason ->
              $"cost driver '{driver.Id}' has an empty non-performance disposition"
          | NonPerformance _ -> ()
          | MeasuredInUi routeIds when List.isEmpty routeIds ->
              $"cost driver '{driver.Id}' has no measured UI route binding"
          | MeasuredInUi _ -> ()
          | RequiredIn workloadIds ->
              if List.isEmpty workloadIds then
                  $"cost driver '{driver.Id}' has no required workload binding"

              for workloadId in workloadIds do
                  match Map.tryFind workloadId workloadById, Map.tryFind workloadId resultById with
                  | None, _ -> $"cost driver '{driver.Id}' names missing workload '{workloadId}'"
                  | Some workload, Some result ->
                      if not (List.contains driver.Id workload.CostDriverIds) then
                          $"cost driver '{driver.Id}' is unbound from required workload '{workloadId}'"

                      let observed = result.ObservedScale |> Map.tryFind driver.Id |> Option.defaultValue 0
                      if workloadId = "maximum-content" && observed <> driver.MaximumExpected then
                          $"cost driver '{driver.Id}' maximum scale must be exact in workload '{workloadId}': expected {driver.MaximumExpected} from {driver.ScaleSource}, observed {observed}"
                      elif observed < driver.MaximumExpected then
                          $"cost driver '{driver.Id}' maximum scale is underrepresented in workload '{workloadId}': expected {driver.MaximumExpected} from {driver.ScaleSource}, observed {observed}"
                  | Some _, None -> $"cost driver '{driver.Id}' has no result for required workload '{workloadId}'"
      for workload in expectedWorkloads do
          let duplicateBindings = workload.CostDriverIds |> duplicateValues
          let duplicateBindingText = String.concat ", " duplicateBindings
          if not (List.isEmpty duplicateBindings) then
              $"workload '{workload.Id}' has duplicate cost-driver bindings: {duplicateBindingText}"
          for driverId in workload.CostDriverIds do
              if not (Map.containsKey driverId driverById) then
                  $"workload '{workload.Id}' names unknown cost driver '{driverId}'" ]

let private capabilityMetricToken =
    function
    | Observed value -> $"observed:{value}"
    | Unsupported reason -> $"unsupported:{reason}"

let private criticInputDigest (results: WorkloadResult list) coverageProblems uiEvidenceDigest =
    let intent =
        performanceIntentDeclaration.WorkloadDefinitionDigests
        |> String.concat ","
    let provenance =
        expectedWorkloads
        |> List.map (fun workload -> $"{workload.Id}={provenanceToken workload.Provenance}")
        |> String.concat ","
    let drivers =
        performanceCostDrivers
        |> List.map (fun driver ->
            let disposition =
                match driver.Disposition with
                | RequiredIn ids ->
                    let workloadIds = String.concat "," ids
                    $"required:{workloadIds}"
                | NonPerformance reason -> $"non-performance:{reason}"
                | MeasuredInUi ids ->
                    let routeIds = String.concat "," ids
                    $"measured-ui:{routeIds}"
            $"{driver.Id}|{driver.Category}|{driver.ScaleSource}|{driver.MaximumExpected}|{driver.VisualElement}|{disposition}")
        |> String.concat ";"
    let measuredEvidence =
        results
        |> List.map (fun result ->
            let observedScale =
                result.ObservedScale
                |> Map.toList
                |> List.map (fun (id, count) -> $"{id}={count}")
                |> String.concat ","
            let reasons = String.concat "," result.Verdict.Reasons
            $"{result.Workload.Id}|p50={result.P50Ms:R}|p95={result.P95Ms:R}|p99={result.P99Ms:R}|updates={result.UpdateCount}|present={capabilityMetricToken result.PresentCount}|catchup={result.CatchUpFrames}|drops={capabilityMetricToken result.DroppedFrames}|declaredEvents={result.DeclaredEventCount}|observedEvents={result.ObservedEventCount}|declaredPointers={result.DeclaredPointerEventCount}|observedPointers={result.ObservedPointerEventCount}|rawInputs={result.RawInputSampleCount}|sceneNodes={result.SceneNodeCount}|shotHistory={result.ShotSpawnHistoryCount}|allocated={result.AllocatedBytes}|scale={observedScale}|passed={result.Verdict.Passed}|reasons={reasons}")
        |> String.concat ";"
    let packages =
        declaredPackageVersions ()
        |> List.map (fun (id, version) -> $"{id}={version}")
        |> String.concat ";"
    let coverageVerdict = String.concat ";" coverageProblems
    let host =
        $"{Environment.OSVersion.Platform};{System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture};{Environment.Version}"
    let capability =
        $"{performanceIntentDeclaration.RequiredCapability}|live={performanceIntentDeclaration.LiveCompositorRequired}|bounded-headless-update-and-scene-route|not-authoritative=live-compositor,swapchain,vblank,vsync"
    sha256Text
        $"performance-representativeness-v1|{intent}|{provenance}|{drivers}|{measuredEvidence}|uiEvidence={uiEvidenceDigest}|coverage={coverageVerdict}|packages={packages}|host={host}|capability={capability}"

let private declarationProblems () =
    let duplicateIds = expectedWorkloads |> List.map _.Id |> duplicateValues
    let duplicateBindings = performanceIntentDeclaration.WorkloadDefinitionDigests |> duplicateValues
    let duplicateIdText = String.concat ", " duplicateIds
    let requiredIdText = String.concat ", " requiredNormalWorkloadIds
    let duplicateBindingText = String.concat ", " duplicateBindings
    let authoredProblems =
        expectedWorkloads
        |> List.collect (fun workload ->
            let verdict = evaluateAuthorship workload
            verdict.Reasons)

    [ if not (List.isEmpty duplicateIds) then
          $"duplicate workload ids: {duplicateIdText}"
      if performanceIntentDeclaration.WorkloadIds <> requiredNormalWorkloadIds then
          $"normal-play workload ids must be exactly: {requiredIdText}"
      if not (List.isEmpty duplicateBindings) then
          $"duplicate workload digest bindings: {duplicateBindingText}"
      if performanceIntentDeclaration.TargetFps <= 0 then
          "performance intent target FPS must be positive"
      if String.IsNullOrWhiteSpace performanceIntentDeclaration.MaximumExpectedScale then
          "performance intent maximum expected scale is required"
      if String.IsNullOrWhiteSpace performanceIntentDeclaration.RequiredCapability then
          "performance intent measurement capability is required"
      yield! authoredProblems ]

let private yamlScalar (value: string) = JsonSerializer.Serialize value

let private yamlList values =
    values |> List.map yamlScalar |> String.concat ", " |> fun values -> $"[{values}]"

let writePerformanceIntentDeclaration (path: string) =
    let intent = performanceIntentDeclaration
    let directory = Path.GetDirectoryName path

    if not (String.IsNullOrWhiteSpace directory) then
        Directory.CreateDirectory directory |> ignore

    let optional name value =
        value |> Option.map (fun actual -> $"  {name}: {yamlScalar actual}") |> Option.toList

    let maxP95 = intent.MaxP95Ms.ToString(CultureInfo.InvariantCulture)
    let maxP99 = intent.MaxP99Ms.ToString(CultureInfo.InvariantCulture)

    [ "performanceIntent:"
      $"  id: {yamlScalar intent.Id}"
      $"  disposition: {yamlScalar intent.Disposition}"
      $"  targetFps: {intent.TargetFps}"
      $"  workloadIds: {yamlList intent.WorkloadIds}"
      $"  workloadDefinitionDigests: {yamlList intent.WorkloadDefinitionDigests}"
      $"  maximumExpectedScale: {yamlScalar intent.MaximumExpectedScale}"
      $"  maxP95Ms: {maxP95}"
      $"  maxP99Ms: {maxP99}"
      $"  maxCatchUpFrames: {intent.MaxCatchUpFrames}"
      $"  structuralCostBudgets: {yamlList intent.StructuralCostBudgets}"
      $"  requiredCapability: {yamlScalar intent.RequiredCapability}"
      $"  liveCompositorRequired: {intent.LiveCompositorRequired.ToString().ToLowerInvariant()}"
      yield! optional "deferralIssue" intent.DeferralIssue
      $"  evidenceRefs: {yamlList intent.EvidenceRefs}"
      yield! optional "rationale" intent.Rationale ]
    |> fun lines -> File.WriteAllLines(path, lines)

    match declarationProblems () with
    | [] ->
        printfn "status=ok performance-intent=%s workloads=%d" path intent.WorkloadIds.Length
        0
    | problems ->
        problems |> List.iter (printfn "status=failed performance-intent reason=%s")
        1

let private writeIntentJson (json: Utf8JsonWriter) =
    let intent = performanceIntentDeclaration
    json.WriteStartObject("performanceIntent")
    json.WriteString("id", intent.Id)
    json.WriteString("disposition", intent.Disposition)
    json.WriteNumber("targetFps", intent.TargetFps)
    json.WriteStartArray("workloadIds")
    intent.WorkloadIds |> List.iter json.WriteStringValue
    json.WriteEndArray()
    json.WriteStartArray("workloadDefinitionDigests")
    intent.WorkloadDefinitionDigests |> List.iter json.WriteStringValue
    json.WriteEndArray()
    json.WriteString("maximumExpectedScale", intent.MaximumExpectedScale)
    json.WriteNumber("maxP95Ms", intent.MaxP95Ms)
    json.WriteNumber("maxP99Ms", intent.MaxP99Ms)
    json.WriteNumber("maxCatchUpFrames", intent.MaxCatchUpFrames)
    json.WriteStartArray("structuralCostBudgets")
    intent.StructuralCostBudgets |> List.iter json.WriteStringValue
    json.WriteEndArray()
    json.WriteString("requiredCapability", intent.RequiredCapability)
    json.WriteBoolean("liveCompositorRequired", intent.LiveCompositorRequired)
    intent.DeferralIssue |> Option.iter (fun value -> json.WriteString("deferralIssue", value))
    json.WriteStartArray("evidenceRefs")
    intent.EvidenceRefs |> List.iter json.WriteStringValue
    json.WriteEndArray()
    intent.Rationale |> Option.iter (fun value -> json.WriteString("rationale", value))
    json.WriteEndObject()

let writeExpectedWorkloadEvidence (path: string) =
    let results = expectedWorkloads |> List.map runWorkload
    let directory = Path.GetDirectoryName path
    let uiEvidencePath =
        if String.IsNullOrWhiteSpace directory then "m7-ui-performance.json"
        else Path.Combine(directory,"m7-ui-performance.json")
    let uiProblems,uiEvidenceDigest=uiEvidenceProblems uiEvidencePath
    let coverageProblems = costDriverProblems results uiProblems
    let criticDigest = criticInputDigest results coverageProblems uiEvidenceDigest

    if not (String.IsNullOrWhiteSpace directory) then
        Directory.CreateDirectory directory |> ignore

    use stream = File.Create path
    use json = new Utf8JsonWriter(stream, JsonWriterOptions(Indented = true))
    json.WriteStartObject()
    json.WriteNumber("schemaVersion", 3)
    json.WriteStartObject("compatibility")
    json.WriteStartArray("acceptedLegacySchemaVersions")
    json.WriteNumberValue(2)
    json.WriteEndArray()
    json.WriteString("legacyRepresentativeness", "legacy-unreviewed")
    json.WriteEndObject()
    writeIntentJson json
    json.WriteString("measurementCapability", "bounded-headless-update-and-scene-route")
    json.WriteString("notAuthoritativeFor", "live-compositor,swapchain,vblank,vsync")
    json.WriteStartObject("uiRouteEvidence")
    json.WriteString("artifact",uiEvidencePath)
    json.WriteString("artifactDigest",$"sha256:{uiEvidenceDigest}")
    json.WriteEndObject()

    json.WriteString(
        "hostProfile",
        $"{Environment.OSVersion.Platform};{System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture};{Environment.Version}"
    )

    json.WriteStartObject("packageVersions")

    for packageId, version in declaredPackageVersions () do
        json.WriteString(packageId, version)

    json.WriteEndObject()
    json.WriteString("warmupSamplePolicy", "per-workload; monotonic Stopwatch; warmup excluded")
    json.WriteStartArray("costDrivers")
    for driver in performanceCostDrivers do
        json.WriteStartObject()
        json.WriteString("id", driver.Id)
        json.WriteString("category", string driver.Category)
        json.WriteString("scaleSource", driver.ScaleSource)
        json.WriteNumber("maximumExpected", driver.MaximumExpected)
        match driver.VisualElement with
        | Some value -> json.WriteString("visualElement", value)
        | None -> json.WriteNull("visualElement")
        match driver.Disposition with
        | RequiredIn workloadIds ->
            json.WriteString("disposition", "required-in-workloads")
            json.WriteStartArray("requiredWorkloadIds")
            workloadIds |> List.iter json.WriteStringValue
            json.WriteEndArray()
        | NonPerformance reason ->
            json.WriteString("disposition", "non-performance")
            json.WriteString("reason", reason)
        | MeasuredInUi routeIds ->
            json.WriteString("disposition", "measured-in-ui-routes")
            json.WriteStartArray("requiredUiRouteIds")
            routeIds |> List.iter json.WriteStringValue
            json.WriteEndArray()
        json.WriteEndObject()
    json.WriteEndArray()
    json.WriteStartArray("workloads")

    for result in results do
        json.WriteStartObject()
        json.WriteString("id", result.Workload.Id)
        json.WriteString("definition", result.Workload.Definition)
        json.WriteString("class", classToken result.Workload.Classification)
        json.WriteString("definitionDigest", result.DefinitionDigest)
        match result.Workload.Provenance with
        | RunnerIssuedJourney receipt ->
            json.WriteString("stateProvenance", journeyKind)
            json.WriteStartObject("provenanceReceipt")
            json.WriteNumber("schemaVersion", JourneyReceipt.schemaVersion receipt)
            json.WriteString("runnerIdentity", JourneyReceipt.runnerIdentity receipt)
            json.WriteString("runnerVersion", JourneyReceipt.runnerVersion receipt)
            json.WriteString("compositionAuthority", JourneyReceipt.compositionAuthority receipt)
            json.WriteString("origin", string (JourneyReceipt.origin receipt))
            json.WriteString("routeId", JourneyReceipt.routeId receipt)
            json.WriteString("scenarioId", JourneyReceipt.scenarioId receipt)
            json.WriteString("testId", JourneyReceipt.testId receipt)
            json.WriteString("inputKind", string (JourneyReceipt.inputKind receipt))
            json.WriteString("inputIdentity", JourneyReceipt.inputIdentity receipt)
            json.WriteString("inputDigest", JourneyReceipt.inputDigest receipt)
            json.WriteString("scriptDigest", JourneyReceipt.scriptDigest receipt)
            json.WriteString("traceDigest", JourneyReceipt.traceDigest receipt)
            json.WriteString("initialFingerprintDigest", JourneyReceipt.initialFingerprintDigest receipt)
            json.WriteString("terminalFingerprintDigest", JourneyReceipt.terminalFingerprintDigest receipt)
            json.WriteString("terminalPredicateIdentity", JourneyReceipt.terminalPredicateIdentity receipt)
            json.WriteBoolean("terminalPredicateReached", JourneyReceipt.terminalPredicateReached receipt)
            json.WriteString("result", string (JourneyReceipt.result receipt))
            json.WriteNumber("steps", JourneyReceipt.steps receipt)
            json.WriteNumber("maxSteps", JourneyReceipt.maxSteps receipt)
            json.WriteString("receiptDigest", $"sha256:{runnerReceiptToken receipt}")
            json.WriteEndObject()
        | SyntheticConstructed reason ->
            json.WriteString("stateProvenance", "synthetic-constructed")
            json.WriteString("syntheticReason", reason)
            json.WriteNull("provenanceReceipt")

        match result.Workload.Composition with
        | CompleteComposition -> json.WriteString("compositionClaim", completeCompositionKind)
        | ComponentOnlySupplemental reason ->
            json.WriteString("compositionClaim", "component-only-supplemental")
            json.WriteString("componentOnlyReason", reason)

        json.WriteStartArray("costDriverIds")
        result.Workload.CostDriverIds |> List.iter json.WriteStringValue
        json.WriteEndArray()

        match result.Workload.Authorship with
        | Placeholder requiredWork ->
            json.WriteString("authorship", "placeholder")
            json.WriteString("requiredAuthoringWork", requiredWork)
            json.WriteNull("declaredDefinitionDigest")
        | Authored declaredDigest ->
            json.WriteString("authorship", "authored")
            json.WriteNull("requiredAuthoringWork")
            json.WriteString("declaredDefinitionDigest", declaredDigest)

        match result.Workload.BlockingDebt with
        | Some debt -> json.WriteString("blockingDebt", debt)
        | None -> json.WriteNull("blockingDebt")

        json.WriteNumber("warmupFrames", result.Workload.WarmupFrames)
        json.WriteNumber("sampleFrames", result.Workload.SampleFrames)
        json.WriteNumber("p50Ms", result.P50Ms)
        json.WriteNumber("p95Ms", result.P95Ms)
        json.WriteNumber("p99Ms", result.P99Ms)
        json.WriteNumber("updateCount", result.UpdateCount)
        let writeCapabilityMetric (name: string) (metric: CapabilityMetric) =
            json.WriteStartObject(name)
            match metric with
            | Observed value ->
                json.WriteString("status", "observed")
                json.WriteNumber("value", value)
            | Unsupported reason ->
                json.WriteString("status", "unsupported")
                json.WriteString("reason", reason)
            json.WriteEndObject()

        writeCapabilityMetric "presentCount" result.PresentCount
        json.WriteNumber("catchUpFrames", result.CatchUpFrames)
        writeCapabilityMetric "droppedFrames" result.DroppedFrames
        json.WriteNumber("declaredEventCount", result.DeclaredEventCount)
        json.WriteNumber("observedEventCount", result.ObservedEventCount)
        json.WriteNumber("declaredPointerEventCount", result.DeclaredPointerEventCount)
        json.WriteNumber("observedPointerEventCount", result.ObservedPointerEventCount)
        json.WriteNumber("rawInputSampleCount", result.RawInputSampleCount)
        json.WriteNumber("shotSpawnHistoryCount", result.ShotSpawnHistoryCount)
        json.WriteNumber("allocatedBytes", result.AllocatedBytes)
        json.WriteStartObject("observedScale")
        result.ObservedScale |> Map.iter (fun name value -> json.WriteNumber(name, value))
        json.WriteEndObject()
        json.WriteStartObject("sceneNodesByLayer")
        json.WriteNumber("rogue3-scene", result.SceneNodeCount)
        json.WriteEndObject()
        json.WriteBoolean("passed", result.Verdict.Passed)
        json.WriteStartArray("reasons")
        result.Verdict.Reasons |> List.iter json.WriteStringValue
        json.WriteEndArray()
        json.WriteEndObject()

    json.WriteEndArray()
    json.WriteStartObject("critic")
    json.WriteString("rubricVersion", "performance-representativeness-v1")
    json.WriteString("inputDigest", criticDigest)
    json.WriteString("status", "external-review-required")
    json.WriteString("reviewBoundary", "attributable review system at the exact landing commit")
    json.WriteString("preferredMode", "fresh-context-subagent")
    json.WriteString("fallbackMode", "separated-pass-with-independence-disclosure")
    json.WriteString(
        "prohibitedProof",
        "in-repo JSON, author-entered identity, or a same-context mode string cannot establish independence"
    )
    json.WriteStartArray("acceptedOutcomes")
    [ "supported"
      "underrepresentative"
      "synthetic-only"
      "unmeasured"
      "misclassified"
      "ambiguous" ]
    |> List.iter json.WriteStringValue
    json.WriteEndArray()
    json.WriteBoolean("representativeReady", false)
    json.WriteEndObject()
    json.WriteEndObject()
    json.Flush()

    let declarationFailures = declarationProblems ()
    let failures = results |> List.filter (_.Verdict.Passed >> not)

    if
        List.isEmpty failures
        && List.isEmpty declarationFailures
        && List.isEmpty coverageProblems
    then
        printfn
            "status=ok performance-evidence workloads=%d capability=bounded-headless artifact=%s"
            results.Length
            path

        0
    else
        declarationFailures
        |> List.iter (printfn "status=failed performance-intent reason=%s")

        coverageProblems
        |> List.iter (printfn "status=failed performance-coverage reason=%s")

        failures
        |> List.iter (fun result ->
            printfn
                "status=failed workload=%s reasons=%s"
                result.Workload.Id
                (String.concat " | " result.Verdict.Reasons))

        1

/// Emits the exact evidence-plus-input-digest package a fresh-context critic must cold-read. Approval
/// lives in an attributable review system at the exact landing commit, never in this authored tree,
/// so a critic cannot edit samples, issue provenance, waive a red budget, or upgrade capability.
let writePerformanceCriticRequest (path: string) =
    let directory = Path.GetDirectoryName path
    let evidencePath =
        if String.IsNullOrWhiteSpace directory then
            "performance-evidence.json"
        else
            Path.Combine(directory, "performance-evidence.json")

    let exitCode = writeExpectedWorkloadEvidence evidencePath
    let evidenceBytes = File.ReadAllBytes evidencePath
    let evidenceDigest = SHA256.HashData evidenceBytes |> Convert.ToHexString |> _.ToLowerInvariant()
    use evidence = JsonDocument.Parse evidenceBytes
    let inputDigest = evidence.RootElement.GetProperty("critic").GetProperty("inputDigest").GetString()

    if not (String.IsNullOrWhiteSpace directory) then
        Directory.CreateDirectory directory |> ignore

    use stream = File.Create path
    use json = new Utf8JsonWriter(stream, JsonWriterOptions(Indented = true))
    json.WriteStartObject()
    json.WriteNumber("schemaVersion", 1)
    json.WriteString("rubricVersion", "performance-representativeness-v1")
    json.WriteString("inputDigest", inputDigest)
    json.WriteString("evidenceArtifact", evidencePath)
    json.WriteString("evidenceArtifactDigest", $"sha256:{evidenceDigest}")
    json.WriteNumber("machineExitCode", exitCode)
    json.WriteString("requiredReviewBoundary", "attributable external review at the exact landing commit")
    json.WriteBoolean("representativeReady", false)
    json.WriteEndObject()
    json.Flush()
    exitCode
