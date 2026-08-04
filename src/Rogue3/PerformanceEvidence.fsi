// The DECLARED public surface of this module (EHotwagner/rogue3#96, constitution principle III:
// "Public Surface Is Declared, Not Incidental"). A binding absent from this file is private to
// the implementation and is NOT product API -- adding one here is a contracted (Tier 1) change.
module Rogue3.PerformanceEvidence

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
    {
      P95Ms: float
      P99Ms: float
      MaximumSceneNodes: int
      MaximumShotSpawnHistory: int
      AllowSustainedCatchUp: bool
    }

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
    | RunnerIssuedJourney of FS.GG.Game.Harness.JourneyReceipt
    | SyntheticConstructed of reason: string

type CompositionClaim =
    | CompleteComposition
    | ComponentOnlySupplemental of reason: string

type Workload =
    {
      Id: string
      Definition: string
      Classification: WorkloadClass
      WarmupFrames: int
      SampleFrames: int
      EventsPerFrame: int
      PointerEventsPerFrame: int
      InitialState: (unit -> Model.Model)
      MessagesAt: (int -> Model.Msg list)
      Provenance: WorkloadProvenance
      Composition: CompositionClaim
      CostDriverIds: string list
      Budget: Budget option
      BlockingDebt: string option
      Authorship: WorkloadAuthorship
    }

val definitionDigest: workload: Workload -> string

/// The starting room with its north door removed, so asking to cross north is an action the live
/// floor graph cannot bind.
val m11NoNorthDoorBoot: unit -> Model.Model

/// The three M11 journeys, entry points and all. A test names the script; the product names the
/// composition. `[<MethodImpl(NoInlining)>]` keeps the boot closures in THIS assembly: without it the
/// F# optimizer may copy them into the caller and re-split the composition authority the runner
/// checks — a failure whose message points at assembly identity and never at inlining.
[<System.Runtime.CompilerServices.MethodImpl
  (enum<System.Runtime.CompilerServices.MethodImplOptions> (8))>]
val runM11RoundTripJourney:
  script: FS.GG.Game.Harness.JourneyEvent<FS.GG.UI.KeyboardInput.ViewerKey,
                                          (Geometry.Vec2 * bool option),
                                          PlayerAction,unit> list ->
    FS.GG.Game.Harness.JourneyRun<Model.Model,
                                  FS.GG.Game.Harness.JourneyEvent<FS.GG.UI.KeyboardInput.ViewerKey,
                                                                  (Geometry.Vec2 *
                                                                   bool option),
                                                                  PlayerAction,
                                                                  unit>,string>

[<System.Runtime.CompilerServices.MethodImpl
  (enum<System.Runtime.CompilerServices.MethodImplOptions> (8))>]
val runM11UnboundActionJourney:
  script: FS.GG.Game.Harness.JourneyEvent<FS.GG.UI.KeyboardInput.ViewerKey,
                                          (Geometry.Vec2 * bool option),
                                          PlayerAction,unit> list ->
    FS.GG.Game.Harness.JourneyRun<Model.Model,
                                  FS.GG.Game.Harness.JourneyEvent<FS.GG.UI.KeyboardInput.ViewerKey,
                                                                  (Geometry.Vec2 *
                                                                   bool option),
                                                                  PlayerAction,
                                                                  unit>,string>

[<System.Runtime.CompilerServices.MethodImpl
  (enum<System.Runtime.CompilerServices.MethodImplOptions> (8))>]
val runM11BoundActionJourney:
  script: FS.GG.Game.Harness.JourneyEvent<FS.GG.UI.KeyboardInput.ViewerKey,
                                          (Geometry.Vec2 * bool option),
                                          PlayerAction,unit> list ->
    FS.GG.Game.Harness.JourneyRun<Model.Model,
                                  FS.GG.Game.Harness.JourneyEvent<FS.GG.UI.KeyboardInput.ViewerKey,
                                                                  (Geometry.Vec2 *
                                                                   bool option),
                                                                  PlayerAction,
                                                                  unit>,string>

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
/// declared `MaximumExpected`. That is the load-bearing check on this particular change, because
/// the seven counters are now written through a NESTED copy-and-update and an increment dropped in
/// that rewrite has to surface somewhere.
///
/// Six of the seven are `RequiredIn [ "maximum-content" ]`, where `costDriverProblems` demands
/// EXACT equality rather than a ceiling, so a dropped, doubled or mis-targeted increment reds the
/// run: `simulation.shot-spawn` 3, `collision.shot-wall-queries` 740,
/// `simulation.homing-target-considerations` 2400, `collision.combat-candidates` 2100,
/// `simulation.floor-pickup-candidates` 12, `simulation.door-sensor-candidates` 8.
///
/// The seventh, `simulation.secret-reveal-candidates`, is `RequiredIn [ "secret-reveal" ]`, where
/// the check is the weaker `observed < MaximumExpected`. Its declared maximum is 1 and it observed
/// 1, so a dropped increment WOULD red it (0 < 1) — but a doubled one would not. Stated exactly
/// rather than folded into the other six, because the guarantee is genuinely weaker there and a
/// reader entitled to assume otherwise is how the next reshape ships an unwired counter.
///
/// `TotalWallQueries`, `TotalHomingQueries`, `TotalSecretRevealCandidates` and
/// `TotalDoorSensorQueries` have no assertion anywhere in the test suite. For those four this
/// evidence is the ONLY thing standing between a nested-record rewrite and a silently dead counter.
val expectedWorkloads: Workload list

val uiEvidenceProblems: path: string -> string list * string

val writePerformanceIntentDeclaration: path: string -> int

val writeExpectedWorkloadEvidence: path: string -> int

/// Emits the exact evidence-plus-input-digest package a fresh-context critic must cold-read. Approval
/// lives in an attributable review system at the exact landing commit, never in this authored tree,
/// so a critic cannot edit samples, issue provenance, waive a red budget, or upgrade capability.
val writePerformanceCriticRequest: path: string -> int
