---
schemaVersion: 1
workId: 001-m0-scaffold-fixed-step-loop
title: M0 Scaffold Fixed Step Loop
stage: plan
changeTier: tier1
status: planned
sourceSpec: work/001-m0-scaffold-fixed-step-loop/spec.md
sourceClarifications: work/001-m0-scaffold-fixed-step-loop/clarifications.md
sourceChecklist: work/001-m0-scaffold-fixed-step-loop/checklist.md
publicOrToolFacingImpact: true
---

# M0 Scaffold Fixed Step Loop Plan

Prose status: planned

## Source Snapshot
- spec: work/001-m0-scaffold-fixed-step-loop/spec.md sha256:15095a1953b45bf185af613ed4888f8ad57b27a2d52b628f71e14d523d4c0580 schemaVersion:1
- clarifications: work/001-m0-scaffold-fixed-step-loop/clarifications.md sha256:6efc7ee989b48e94078ce2d1d2698ce25fa3acc68f1fa8bd6bbf076e5ceaa8ea schemaVersion:1
- checklist: work/001-m0-scaffold-fixed-step-loop/checklist.md sha256:2754e73c353983fd83b4d085a6ca05a306c0858c9a87ee4ff1d0c9692d0792c5 schemaVersion:1

## Plan Scope
- Replace only the scaffold-owned game model/projection and their replaceable tests, plus the smallest durable evidence repoints required by `docs/scaffold-map.md`.
- Preserve Program, WindowOptions, generic GameShell, host composition, audio/effect boundaries, and governance tests.

## Plan Decisions
- PD-001 [AC-001] [FR-001] complete: Replace the Pong fields with a minimal Hollow Depths `RunState` and `Model`, retain host-facing `Started`, `ViewerInput`, and command signatures, and make `view` project the logical frame from product state.
- PD-002 [AC-002] [AC-003] [FR-002] [DEC-001] [DEC-002] complete: Define `fixedDt = 1.0 / 120.0`, `maxSteps = 5`, call `FixedStep.drainWith (float maxSteps * fixedDt) fixedDt`, run `stepSim` exactly the returned count, and store only its returned sub-step remainder.
- PD-003 [AC-004] [FR-003] [DEC-003] complete: `initRun seed` creates `Rng.ofSeed seed`, splits twice in fixed order, and stores value-type `LayoutRng`/`DropRng` in the model; focused tests draw one stream and assert the other is structurally unchanged.
- PD-004 [AC-005] [FR-004] complete: Add a pure logical-canvas transform record with uniform min-axis scale and centered offsets, expose `worldToScreen` and `screenToWorld`, and render a 1280×720 playfield through the existing scene route.
- PD-005 [FR-001] [FR-002] complete: Keep the M0 single-state loop as the source-spec-sanctioned interpolation departure; the Stretch milestone owns any later double-buffer migration.

## Contract Impact
- PC-001 [PD-001] publicSurface: `Rogue3.Model` continues to expose `Model`, `Msg`, `init`, `update`, `subscriptions`, and host compatibility helpers, while its replaceable game-specific fields become Hollow Depths M0 state.
- PC-002 [PD-002] framework: FS.GG.Game.Core#FixedStep.drainWith
- PC-003 [PD-003] framework: FS.GG.Game.Core#RngModule.split

## Verification Obligations
- VO-001 [PD-001] [PC-001] semanticTest: Focused Expecto tests call the production `init`/`update`/`view` seam and retain a passing Release TRX.
- VO-002 [PD-002] [PC-002] acceptanceTest: Pin decimal `0.033`, exact `1/30`, sub-step banking, and the five-step huge-stall guard through `update (Tick dt)`.
- VO-003 [PD-003] [PC-003] determinismTest: Pin same-seed equality and split-stream isolation using real `Rng` values.
- VO-004 [PD-004] semanticTest: Pin logical extent, centered letterbox fit, endpoint mapping, and inverse round-trip.
- VO-005 [PD-001] buildAudit: Run Dev, focused Test with TRX, full Test, PerformanceIntent/Evidence, Verify, SDD verify, and SDD ship against the exact candidate.

## Performance Intent
No performance intent is declared for this work item.

## Migration Posture
- PM-001 [PC-001] directReplacement: Replace the documented scaffold-owned product model/projection and rewrite replaceable behavior tests; preserve durable host and governance surfaces.

## Generated View Impact
- GV-001 [PD-001] workModel: Refresh SDD work-model, agent guidance, analysis, verify, ship, and committed ship-verdict views after their authored inputs change.
- GV-002 [PD-001] productEvidence: Regenerate product logs/TRX/performance artifacts; treat bounded headless metrics as non-compositor evidence.

## Accepted Deferrals
No accepted plan deferrals recorded.

## Planning Findings
No blocking planning findings recorded.

## Advisory Notes
- No remote exists, so PR/GitHub acceptance cannot be fabricated; the exact local branch and commit remain the host's merge unit.
- The lifecycle sentinel is removed only after the native work item exists, with the init/noChange/unsafeOverwrite behavior retained in feedback evidence.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd tasks --work 001-m0-scaffold-fixed-step-loop`.
