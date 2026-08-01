---
schemaVersion: 1
workId: 005-m4-procedural-floor-generation
title: M4 Procedural Floor Generation
stage: plan
changeTier: tier1
status: planned
sourceSpec: work/005-m4-procedural-floor-generation/spec.md
sourceClarifications: work/005-m4-procedural-floor-generation/clarifications.md
sourceChecklist: work/005-m4-procedural-floor-generation/checklist.md
publicOrToolFacingImpact: true
---

# M4 Procedural Floor Generation Plan

Prose status: planned

## Source Snapshot
- spec: work/005-m4-procedural-floor-generation/spec.md sha256:ca44c23ab092c5564570e873cf2b6dfc4ec7906191c91ed6eef73d8f41946d6d schemaVersion:1
- clarifications: work/005-m4-procedural-floor-generation/clarifications.md sha256:5f7c22bd3fa5ce2c731581ec6eb4e6cb6344d203d898e0d9b5b35b7efd458bef schemaVersion:1
- checklist: work/005-m4-procedural-floor-generation/checklist.md sha256:034c93c7eadb7d32cab05a106b46b443da53dcff1d08c304d888e559d03657aa schemaVersion:1

## Plan Scope
- Work item 005-m4-procedural-floor-generation is planned from the current specification, clarification, and checklist facts.
- Requirement count: 6.
- Clarification decision count: 0.
- Checklist result count: 6.

## Plan Decisions
- PD-001 [AC-001] [FR-001] complete: Add a product-owned `FloorGeneration` module over `MapGen.floorSeed`, value-type `Rng`, immutable room/floor records, and an explicit remaining run-item pool.
- PD-002 [AC-001] [FR-002] complete: Draw the inclusive budget noise, call `MapGen.floorLayout` for at most 32 threaded attempts, retain the best, then build a deterministic comb tree if the framework walk remains partial.
- PD-003 [AC-001] [FR-003] complete: Assign hidden cells deterministically and populate integer template obstacles plus roster anchors until the exact threat budget is spent without implementing live M5 AI.
- PD-004 [AC-001] [FR-004] complete: Project sorted orthogonal layout edges into reciprocal door records while leaving pedestal/shop/reward contents and room-clear door opening to M5.
- PD-005 [AC-002] [FR-005] complete: Implement secret reveal as one pure floor replacement that updates room visibility, two door values, both graph lists, map reveal, and pending links together.
- PD-006 [AC-003] [FR-006] complete: Make boss-clear trapdoor creation idempotent and add `DescendFloor` to regenerate while explicitly clearing every room-local live collection and preserving player/run values.

## Contract Impact
- PC-001 [PD-001] source contract: `FloorGeneration.fs` owns M4 floor values/pure generation; `Model.fs` owns run integration/messages; tests and performance evidence consume those product surfaces.

## Verification Obligations
- VO-001 [PD-001] [PD-002] [PD-003] [PD-004] [PD-005] [PD-006] [PC-001] semanticTest: Run six focused deterministic cases, full Release tests, Dev/Test/PerformanceIntent/PerformanceEvidence/Verify, exact TRX evidence, fresh performance critic, and SDD verify/ship.

## Performance Intent
No performance intent is declared for this work item.

## Migration Posture
- PM-001 [PC-001] additive: Preserve M0-M3 public behavior and local state; add M4 floor values without implementing M5+ behavior or mutating package surfaces.

## Generated View Impact
- GV-001 [PD-001] workModel: Refresh SDD work model/analysis/verify/ship plus exact performance intent/evidence/critic request, roadmap M4 evidence, and feedback schema-v2 state.

## Accepted Deferrals
No accepted plan deferrals recorded.

## Planning Findings
No blocking planning findings recorded.

## Advisory Notes
- Optional Governance pointers remain compatibility facts only.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd tasks --work 005-m4-procedural-floor-generation`.
