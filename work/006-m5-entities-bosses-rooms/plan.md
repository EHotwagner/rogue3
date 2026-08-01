---
schemaVersion: 1
workId: 006-m5-entities-bosses-rooms
title: M5 Entities Bosses Rooms
stage: plan
changeTier: tier1
status: planned
sourceSpec: work/006-m5-entities-bosses-rooms/spec.md
sourceClarifications: work/006-m5-entities-bosses-rooms/clarifications.md
sourceChecklist: work/006-m5-entities-bosses-rooms/checklist.md
publicOrToolFacingImpact: true
---

# M5 Entities Bosses Rooms Plan

Prose status: planned

## Source Snapshot
- spec: work/006-m5-entities-bosses-rooms/spec.md sha256:65d73b124450741704b970d0a27a334f89b7f6215747b79c10777263685dc3d8 schemaVersion:1
- clarifications: work/006-m5-entities-bosses-rooms/clarifications.md sha256:55036185d3f5e0a8381302fb6e45c20db0895fd8ba83435260afe800ac8df4fc schemaVersion:1
- checklist: work/006-m5-entities-bosses-rooms/checklist.md sha256:1393e2bc66e6880252be5607a3b9a1580bd50a1507ea343de475ee6d16a3b531 schemaVersion:1

## Plan Scope
- Work item 006-m5-entities-bosses-rooms is planned from the current specification, clarification, and checklist facts.
- Requirement count: 9.
- Clarification decision count: 0.
- Checklist result count: 9.

## Plan Decisions
- PD-001 [AC-001] [FR-001] complete: Add a product-owned Entities module with immutable definitions and tick-based FSM transitions in stable id order.
- PD-002 [AC-001] [FR-002] complete: Represent boss emitters as declarative records and phase selection as pure HP-threshold functions.
- PD-003 [AC-002] [FR-003] complete: Make room entry/clear atomic pure transitions over room state and threaded RNG.
- PD-004 [AC-002] [FR-004] complete: Centralize exact weighted tables in one total cumulative selector returning the advanced DropRng.
- PD-005 [AC-003] [FR-005] complete: Apply floor scales only at spawn/bullet emission so base definitions remain inspectable.
- PD-006 [AC-003] [FR-006] complete: Use typed obstacle policy functions for movement, shot, flying, hazard, and claimed destruction semantics.
- PD-007 [AC-004] [FR-007] complete: Generate fixtures from LayoutRng while folding a Set of unavailable item ids across all floors in the run.
- PD-008 [AC-005] [FR-008] complete: Use immutable three-slot shops; purchase replaces stock with Empty and generation is the only restock path.
- PD-009 [AC-006] [FR-009] complete: Extend maximum-content production update/view state and counters, then refresh the authored definition digest and runner receipt evidence.

## Contract Impact
- PC-001 [PD-001] product source: src/Rogue3/Entities.fs and src/Rogue3/Model.fs expose product-owned M5 pure values/functions; tests and readiness evidence version them together.

## Verification Obligations
- VO-001 [PD-001] [PC-001] semanticTest: Focused M5 tests cover every roster/boss/table/gate/obstacle/reward/shop contract; full Release Test and Verify plus maximum-content performance evidence bind the exact candidate.

## Performance Intent
- Active producer-owned PI-GENERATED-GAME remains authoritative. Maximum expected M5 scale is 30 enemies spanning all eight kinds, one phase-three boss, 120 enemy bullets, five obstacle kinds, three shop slots, and all existing 40-shot collision load through production update + view. Existing p95 16.67 ms, p99 25 ms, catch-up zero, scene-node 4096, shot-history 40, and combat-candidate 2520 budgets remain hard gates.

## Migration Posture
- PM-001 [PC-001] additive: Existing M0-M4 serialized in-memory model values are extended additively; there is no persisted run-save format or migration obligation in M5.

## Generated View Impact
- GV-001 [PD-001] readiness: Regenerate M5 work model, analysis, verify, summary, ship view/verdict, performance evidence, and performance critic request against the final authored sources.

## Accepted Deferrals
No accepted plan deferrals recorded.

## Planning Findings
No blocking planning findings recorded.

## Advisory Notes
- Optional Governance pointers remain compatibility facts only.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd tasks --work 006-m5-entities-bosses-rooms`.
