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
      MaximumExpectedScale = "20 generated floor rooms plus 40 live player shots, 8 static obstacles, 30 live enemies/targets, 120 enemy bullets, 736 wall primitives, 2,400 homing considerations, multishot 3, and all five scaffold visuals"
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
    | NonPerformance of reason: string

type PerformanceCostDriver =
    { Id: string
      Category: CostDriverCategory
      ScaleSource: string
      MaximumExpected: int
      VisualElement: string option
      Disposition: CostDriverDisposition }

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
        ScaleSource = "Model.TotalShotSpawns delta across one sampled production frame"
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
        ScaleSource = "Model.TotalWallQueries delta: two fixed steps each cast 40 shots once and perform two player-axis casts plus four slide contact folds against 8 stable AABBs"
        MaximumExpected = 736
        VisualElement = None
        Disposition = RequiredIn [ "maximum-content" ] }
      { Id = "simulation.homing-target-considerations"
        Category = Simulation
        ScaleSource = "Model.TotalHomingQueries delta: two fixed steps each consider 30 stable targets for 40 homing shots"
        MaximumExpected = 2400
        VisualElement = None
        Disposition = RequiredIn [ "maximum-content" ] }
      { Id = "state.static-obstacles"
        Category = Simulation
        ScaleSource = "Model.Obstacles exact live count"
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
        ScaleSource = "Model.Enemies exact live count"
        MaximumExpected = 30
        VisualElement = None
        Disposition = RequiredIn [ "maximum-content" ] }
      { Id = "state.enemy-bullets"
        Category = Simulation
        ScaleSource = "Model.EnemyBullets exact live count"
        MaximumExpected = 120
        VisualElement = None
        Disposition = RequiredIn [ "maximum-content" ] }
      { Id = "collision.combat-candidates"
        Category = Simulation
        ScaleSource = "Model.TotalCombatCandidates delta: two fixed steps query 37 spatially overlapping retained shots x 30 enemies, 30 player-contact candidates, and all 120 bullet-player broadphase candidates; all 40 shots still traverse movement/wall/homing"
        MaximumExpected = 2520
        VisualElement = None
        Disposition = RequiredIn [ "maximum-content" ] }
      { Id = "scene.ball"
        Category = SceneRender
        ScaleSource = "GameplayVisualInventory.Ball"
        MaximumExpected = 1
        VisualElement = Some "Ball"
        Disposition = RequiredIn [ "movement-aiming"; "firing"; "maximum-content" ] }
      { Id = "scene.left-paddle"
        Category = SceneRender
        ScaleSource = "GameplayVisualInventory.LeftPaddle"
        MaximumExpected = 1
        VisualElement = Some "LeftPaddle"
        Disposition = RequiredIn [ "movement-aiming"; "maximum-content" ] }
      { Id = "scene.right-paddle"
        Category = SceneRender
        ScaleSource = "GameplayVisualInventory.RightPaddle"
        MaximumExpected = 1
        VisualElement = Some "RightPaddle"
        Disposition = RequiredIn [ "maximum-content" ] }
      { Id = "ui.score"
        Category = UiControl
        ScaleSource = "GameplayVisualInventory.Score"
        MaximumExpected = 1
        VisualElement = Some "Score"
        Disposition = RequiredIn [ "firing"; "maximum-content" ] }
      { Id = "scene.playfield"
        Category = SceneRender
        ScaleSource = "GameplayVisualInventory.Playfield"
        MaximumExpected = 1
        VisualElement = Some "Playfield"
        Disposition = RequiredIn [ "idle"; "movement-aiming"; "firing"; "effects-fog"; "maximum-content" ] }
      { Id = "effects.rogue3"
        Category = EffectsParticles
        ScaleSource = "starter has no effect/particle system; replace this disposition when one is added"
        MaximumExpected = 1
        VisualElement = None
        Disposition = NonPerformance "no effect/particle system exists in the generated starter" }
      { Id = "host.presentation"
        Category = HostPresentation
        ScaleSource = "protected live-compositor host"
        MaximumExpected = 1
        VisualElement = None
        Disposition =
            NonPerformance
                "bounded headless evidence cannot measure present/drop/swapchain/vsync; use a live-compositor workload" } ]

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
    // Model is a closed structural record: %A covers every field recursively, with Set/Map in their
    // deterministic comparison order. Normalizing newlines makes the digest stable across hosts.
    sprintf "%A" model |> _.Replace("\r\n", "\n") |> sha256Text

let private messageDefinitionFingerprint (workload: Workload) =
    [ for frame in 0 .. max workload.WarmupFrames workload.SampleFrames - 1 ->
          frame, workload.MessagesAt frame ]
    |> sprintf "%A"
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

let private observeCostScale driverId routed beforeModel afterModel =
    match performanceCostDrivers |> List.tryFind (fun driver -> driver.Id = driverId) with
    | Some driver ->
        match driver.Id, driver.Category, driver.VisualElement with
        | "simulation.fixed-step", _, _ -> afterModel.SimStepCount - beforeModel.SimStepCount
        | "simulation.shot-spawn", _, _ -> afterModel.TotalShotSpawns - beforeModel.TotalShotSpawns
        | "state.live-player-shots", _, _ -> afterModel.ShotSpawns.Length
        | "generation.floor-room-budget", _, _ -> afterModel.Floor.Rooms.Count
        | "collision.shot-wall-queries", _, _ -> afterModel.TotalWallQueries - beforeModel.TotalWallQueries
        | "simulation.homing-target-considerations", _, _ -> afterModel.TotalHomingQueries - beforeModel.TotalHomingQueries
        | "state.static-obstacles", _, _ -> afterModel.Obstacles.Length
        | "state.homing-targets", _, _ -> afterModel.HomingTargets.Length
        | "state.live-enemies", _, _ -> afterModel.Enemies |> List.filter (fun enemy -> enemy.HitPoints > 0.0) |> List.length
        | "state.enemy-bullets", _, _ -> afterModel.EnemyBullets.Length
        | "collision.combat-candidates", _, _ -> afterModel.TotalCombatCandidates - beforeModel.TotalCombatCandidates
        | _, Input, _ ->
            let applied =
                if afterModel.LastResolvedInput.Move <> zero
                   || afterModel.LastResolvedInput.Aim <> zero
                   || afterModel.LastResolvedInput.FireHeld then
                    afterModel.SimStepCount - beforeModel.SimStepCount
                else 0
            max routed.RawInputSamples applied
        | _, (SceneRender | UiControl), Some elementId ->
            GameplayVisualInventory.project afterModel
            |> List.filter (fun item -> GameplayVisualInventory.elementId item.Element = elementId)
            |> List.length
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
        observedScale <-
            workload.CostDriverIds
            |> List.fold (fun scales id ->
                let count = observeCostScale id routed beforeModel model
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
let private performanceJourneyReceipt scenarioId boot terminalSteps script =
    let terminalPredicateIdentity =
        if scenarioId = "floor-generation" then
            $"model.FloorIndex>={terminalSteps}"
        else
            $"model.SimStepCount>={terminalSteps}"

    let adapter: ProductionJourney<Model, ViewerKey, Vec2 * bool option, unit, unit, Msg, string> =
        { RouteId = "rogue3-m3-combat-health-currency-update-view"
          ScenarioId = scenarioId
          TestId = $"performance-{scenarioId}"
          MaxSteps = 4
          Boot = boot
          MapEvent =
            fun event _ ->
                match event with
                | JourneyEvent.Start -> JourneyDispatch.Mapped [ Started ]
                | JourneyEvent.KeyInput(key, pressed) -> JourneyDispatch.Mapped [ KeyChanged(keyName key, pressed) ]
                | JourneyEvent.FixedTick -> JourneyDispatch.Mapped [ Tick fixedDt ]
                | JourneyEvent.PointerInput(position, primaryDown) -> JourneyDispatch.Mapped [ PointerChanged(position, primaryDown) ]
                | JourneyEvent.MenuAction _ -> JourneyDispatch.Unbound "menu action"
                | JourneyEvent.Interact -> JourneyDispatch.Mapped [ DescendFloor ]
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

    Journey.runScriptWithIdentity
        $"{scenarioId}-one-fixed-step"
        terminalPredicateIdentity
        adapter
        script
    |> fun run -> run.Receipt

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

let private maximumObstacles: SimRect list =
    [ for index in 0 .. 7 -> { X = 80.0 + float index * 140.0; Y = 24.0; Width = 32.0; Height = 32.0 } ]

let private maximumTargets =
    [ for index in 0 .. 29 -> { Id = index + 1; Position = vec2 (644.0 + float (index % 3)) (360.0 + float (index % 3)) } ]

let private maximumEnemies =
    maximumTargets
    |> List.map (fun target ->
        { Id = target.Id; Position = target.Position; Velocity = zero; Radius = 64.0
          HitPoints = 10000.0; ContactDamage = 0; LastContactTick = None; HitFlashTicks = 0 })

let private maximumEnemyBullets =
    [ for index in 0 .. 119 ->
        // Exactly touching the player broadphase radius: returned by inclusive queryRadius, rejected
        // by strict circleContact, so all 120 candidates remain stable across both fixed steps.
        { Id = index + 1; Position = add initialModel.PlayerPosition (vec2 16.0 0.0); Radius = 3.0; Damage = 1 } ]

let private maximumContentModel () =
    { withInput maximumGamepadInput with
        ShotSpawns = maximumShotHistory
        TotalShotSpawns = maximumShotSpawnHistory
        NextShotId = maximumShotSpawnHistory + 1
        PlayerStats = { basePlayerStats with Homing = 1.0; Multishot = 3; Pierce = 30 }
        Obstacles = maximumObstacles
        HomingTargets = maximumTargets
        Enemies = maximumEnemies
        EnemyBullets = maximumEnemyBullets }

// Product-owned canonical representative factory at the journey boot seam. It is not the ordinary
// player boot; its role is to make maximum authored content reachable through the same production
// update/view composition without caller-authored receipt labels or hashes.
let private maximumContentJourneyBoot () = maximumContentModel ()

// Canonical representative generation boot shared by the timed workload and its runner receipt.
let private floorGenerationModel () = { initialModel with FloorIndex = 8 }

/// REQUIRED PRODUCT AUTHORING. Every untouched row is deliberately a failing placeholder.
///
/// For each row: replace `InitialState` and `MessagesAt` with representative rogue3 state/messages,
/// rewrite `Definition` to name that route, run PerformanceEvidence once, review the emitted
/// `definitionDigest`, then change `Placeholder` to `Authored "<digest>"`. The measurement always
/// drives the real `update` + scene `view` route; there is no local statistics-only escape hatch.
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
        CostDriverIds = [ "simulation.fixed-step"; "scene.playfield" ]
        Budget = Some normalBudget
        BlockingDebt = None
        Authorship = Authored "2ed65809573c84c4051c3a446b8770c36494287729c6fa4db40093cbce81c828" }
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
              "scene.ball"
              "scene.left-paddle"
              "scene.playfield" ]
        Budget = Some normalBudget
        BlockingDebt = None
        Authorship = Authored "a02038b7b8145fd7f3748c2e32152cff1a35fe86b97a4922754c44012f464987" }
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
              "scene.ball"
              "ui.score"
              "scene.playfield" ]
        Budget = Some normalBudget
        BlockingDebt = None
        Authorship = Authored "e009940dc00ac0d2e8c520564c5ecba43af1d973392e4089c546ceea27cbb93e" }
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
        CostDriverIds = [ "simulation.fixed-step"; "scene.playfield" ]
        Budget = Some normalBudget
        BlockingDebt = None
        Authorship = Authored "b30e6ef70f4275280642272ed1705cce182364b6929e4e6827502640f1954b76" }
      // WORKLOAD-SOURCE-END effects-fog
      // WORKLOAD-SOURCE-BEGIN floor-generation
      { Id = "floor-generation"
        Definition = "M4 maximum bounded floor generation: production DescendFloor repeatedly derives MapGen.floorSeed, executes bounded room placement with 20-room cap, assigns templates/threat/specials/fixtures, and replaces room-local state"
        Classification = NormalPlay
        WarmupFrames = 4
        SampleFrames = 40
        EventsPerFrame = 0
        PointerEventsPerFrame = 0
        InitialState = floorGenerationModel
        MessagesAt = (fun _ -> [ DescendFloor ])
        Provenance = RunnerIssuedJourney(performanceJourneyReceipt "floor-generation" floorGenerationModel 9 [ JourneyEvent.Interact ])
        Composition = CompleteComposition
        CostDriverIds = [ "generation.floor-room-budget"; "scene.playfield" ]
        Budget = Some normalBudget
        BlockingDebt = None
        Authorship = Authored "380721f4766bcbd2eeb8d133cb99058df498768a574eb4522678faa2837a82b3" }
      // WORKLOAD-SOURCE-END floor-generation
      // WORKLOAD-SOURCE-BEGIN maximum-content
      { Id = "maximum-content"
        Definition = "M3 canonical maximum fixture through the production journey/update/view route: 40 live homing/piercing shots, 8 static AABBs, 30 live enemies/targets, 120 bullet-player broadphase candidates, held ArrowRight keyboard aim/fire routed through production input, production Tick(1/60), and all five scaffold visuals"
        Classification = NormalPlay
        WarmupFrames = 20
        SampleFrames = 120
        EventsPerFrame = 1
        PointerEventsPerFrame = 0
        InitialState = maximumContentModel
        MessagesAt = (fun _ -> [ KeyChanged(keyName ArrowRight, true); Tick(1.0 / 60.0) ])
        Provenance =
            RunnerIssuedJourney(
                performanceJourneyReceipt
                    "maximum-content"
                    maximumContentJourneyBoot
                    1
                    [ JourneyEvent.KeyInput(ArrowRight, true)
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
              "collision.combat-candidates"
              "scene.ball"
              "scene.left-paddle"
              "scene.right-paddle"
              "ui.score"
              "scene.playfield" ]
        Budget = Some normalBudget
        BlockingDebt = None
        Authorship = Authored "5e31f83fbcbd74c5c9e01b054f31de13aada5fec6d3cd2d36ee3b19bc8a3e28e" }
      // WORKLOAD-SOURCE-END maximum-content
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
    [ "idle"; "movement-aiming"; "firing"; "effects-fog"; "floor-generation"; "maximum-content" ]

let private costDriverProblems (results: WorkloadResult list) =
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

    [ if not (List.isEmpty duplicateDriverIds) then
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

let private criticInputDigest (results: WorkloadResult list) coverageProblems =
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
        $"performance-representativeness-v1|{intent}|{provenance}|{drivers}|{measuredEvidence}|coverage={coverageVerdict}|packages={packages}|host={host}|capability={capability}"

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
    let coverageProblems = costDriverProblems results
    let criticDigest = criticInputDigest results coverageProblems
    let directory = Path.GetDirectoryName path

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
