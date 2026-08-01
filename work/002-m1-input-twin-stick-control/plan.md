---
schemaVersion: 1
workId: 002-m1-input-twin-stick-control
title: M1 Input Twin Stick Control
stage: plan
changeTier: tier1
status: planned
sourceSpec: work/002-m1-input-twin-stick-control/spec.md
sourceClarifications: work/002-m1-input-twin-stick-control/clarifications.md
sourceChecklist: work/002-m1-input-twin-stick-control/checklist.md
publicOrToolFacingImpact: true
---

# M1 Input Twin Stick Control Plan

Prose status: planned

## Source Snapshot
- spec: work/002-m1-input-twin-stick-control/spec.md sha256:ecafb44648006f3661e78a5b7341330a8752f6ba7a18ce6c91993fd2c92fed77 schemaVersion:1
- clarifications: work/002-m1-input-twin-stick-control/clarifications.md sha256:e9293f4e11720c999e1a53dbf2d2b7a781a35e1cf031d47bf7774e4479fe3531 schemaVersion:1
- checklist: work/002-m1-input-twin-stick-control/checklist.md sha256:52dc9e746d8062a7a432547121ea39e3867b619c0e13b2b4e395680b2872efea schemaVersion:1

## Plan Scope
- Extend the replaceable product model, host adapter, focused behavior tests, and production-route performance workloads only; retain all durable shell, audio, layout, window, and governance seams.
- Treat the authored Markdown and typed task/evidence files as source; generated readiness and product performance files remain derived views.

## Plan Decisions
- PD-001 [AC-001] [FR-001] [DEC-002] complete: Add `InputSnapshot` current/previous values and `InputState.PressedThisTick`; InputChanged latches current, Tick derives edges, passes them only to the first drained step, and commits previous only after at least one step.
- PD-002 [AC-002] [AC-003] [FR-002] [DEC-001] complete: Add pure normalized move/aim resolution, `PlayerPosition`, `PlayerVelocity`, and `ShotSpawn` intent values; resolve move separately from aim and calculate shot velocity as `aim * 420 + playerVelocity * 0.25`.
- PD-003 [AC-004] [FR-003] [DEC-001] complete: Normalize arrow axes to exact cardinal/diagonal directions and preserve arbitrary finite mouse/right-stick vectors, with arrows then right stick then mouse as aim priority.
- PD-004 [AC-005] [FR-004] [DEC-003] complete: Store simulation-owned fire-held/cooldown state, emit immediately on acquisition, subtract fixed dt, emit at each 0.4-second boundary, and reset readiness on release.
- PD-005 [AC-006] [FR-005] [DEC-004] complete: Continue forwarding both keyboard edges through the shell; add pure pointer-interaction mapping for every coordinate-bearing sample and expose `InputChanged` for full gamepad snapshots without claiming absent native polling.
- PD-006 [FR-001] [FR-002] [FR-004] complete: Keep the existing Pong-compatible fields and movement transition behavior stable while M1 adds the Hollow Depths control state, so durable M0 tests and host/governance behavior do not regress before M2 replaces movement/projectile mechanics.

## Contract Impact
- PC-001 [PD-001] [PD-002] [PD-003] [PD-004] publicSurface: `Rogue3.Model` exposes `GamepadSnapshot`, `InputSnapshot`, `InputState`, `AimSource`, `ResolvedInput`, `ShotSpawn`, pure snapshot/resolution helpers, and `InputChanged`/`PointerChanged` messages.
- PC-002 [PD-005] hostSurface: `Rogue3.EvidenceCommands.interactiveHost` preserves InteractiveAppHost and raw keyboard routing while mapping coordinate-bearing `PointerInteraction` values to product pointer messages.

## Verification Obligations
- VO-001 [PD-001] [PC-001] acceptanceTest: Real Expecto tests pin edge derivation, first-step-only consumption, no-step retention, and previous/current commit order.
- VO-002 [PD-002] [PD-003] [PC-001] acceptanceTest: Real tests pin AC #9 left move/right shot inheritance, independent gamepad sticks, all eight arrow directions, and non-cardinal analog/mouse preservation.
- VO-003 [PD-004] [PC-001] acceptanceTest: Real 120 Hz tests pin immediate acquisition, 0.4-second repeat cadence, release, and immediate re-press.
- VO-004 [PD-005] [PC-002] integrationTest: Host-level keyboard down/ticks/up and coordinate-bearing pointer press/release tests traverse the production shell adapter; governance scans remain green.
- VO-005 [PD-006] releaseAudit: Run focused and full Release tests with TRX, Dev/Test/PerformanceIntent/PerformanceEvidence/Verify, exact SDD verify/ship, and exact work-roadmap checkpoint/report/state validators.

## Performance Intent
No performance intent is declared for this work item.

## Migration Posture
- PM-001 [PC-001] additive: Add input/control fields and messages while retaining M0 host-facing cases and model fields; M2 may replace the temporary direct velocity/shot-spawn intent with full physics/projectile state.
- PM-002 [PC-002] compatibleHost: No package upgrade or launcher swap; the live shell host continues to own menu/pause/settings and accepts the input messages its pinned keyboard/pointer seams can provide.

## Generated View Impact
- GV-001 [PD-001] workModel: Refresh analysis, work model, agent guidance, verify, ship, and committed ship verdict from current authored sources.
- GV-002 [PD-002] performanceEvidence: Regenerate performance intent/evidence after workload definitions and candidate code change, acknowledging new definition digests.

## Accepted Deferrals
No accepted plan deferrals recorded.

## Planning Findings
No blocking planning findings recorded.

## Advisory Notes
- The pinned InteractiveAppHost API has no gamepad polling field and exposes pointer coordinates only on emitted PointerInteraction values; native gamepad polling and unpressed continuous hover cannot be claimed as tested live-host behavior.
- No remote exists, so PR, protected-branch critic marker, merge, and post-merge verification evidence remain unavailable; local orchestration is reported honestly.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd tasks --work 002-m1-input-twin-stick-control`.
