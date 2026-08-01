---
schemaVersion: 1
workId: 008-m7-ui-menus-stats
title: M7 Ui Menus Stats
stage: plan
changeTier: tier1
status: planned
sourceSpec: work/008-m7-ui-menus-stats/spec.md
sourceClarifications: work/008-m7-ui-menus-stats/clarifications.md
sourceChecklist: work/008-m7-ui-menus-stats/checklist.md
publicOrToolFacingImpact: true
---

# M7 Ui Menus Stats Plan

Prose status: planned

## Source Snapshot
- spec: work/008-m7-ui-menus-stats/spec.md sha256:e192f3df093735805a01a9860d05746cd36f0cff0a8fab34dac31c515c20c8a6 schemaVersion:1
- clarifications: work/008-m7-ui-menus-stats/clarifications.md sha256:fde1322bcffa134e0e7466bb11745e549e171f624a5c693e66acc50798862541 schemaVersion:1
- checklist: work/008-m7-ui-menus-stats/checklist.md sha256:c1c81b2acc8bfd8b3a6d0e83bb670a37f8f1aace145b22877d87a7762f2c97cd schemaVersion:1

## Plan Scope
- Work item 008-m7-ui-menus-stats is planned from the current specification, clarification, and checklist facts.
- Requirement count: 8.
- Clarification decision count: 5.
- Checklist result count: 8.

## Plan Decisions
- PD-001 [AC-001] [FR-001] complete: Add a size-aware HUD projection to production rendering with explicit heart/resource/charge/minimap/floor geometry and a deterministic overlap report at 1280x720 and 1920x1080.
- PD-002 [AC-002] [FR-002] complete: Retain `GameShell.fs` as the sole shared router/settings/rebind implementation, parameterize it as HOLLOW DEPTHS, and add typed game-specific rows through product composition rather than a second router.
- PD-003 [AC-002] [FR-003] complete: Extend the shipped `interactiveHost` and headless test helpers so BoundIds-guarded retained clicks, raw key capture, held key ticks, Esc, and logical-canvas resolution mapping are exercised on the actual launch route.
- PD-004 [AC-003] [FR-004] complete: Preserve deterministic versioned shell codecs and add pure profile request evidence; filesystem writes remain host-boundary behavior and no durability claim closes this obligation.
- PD-005 [AC-004] [FR-005] complete: Add `MetaProfile`, `GameSettings`, and messages for difficulty/volume/mute/shake; clamp volume, apply model values live, and emit one `PersistenceEffect.Save` request per change.
- PD-006 [AC-005] [FR-006] complete: Add `RunStats`, `LifetimeStats`, death-cause/character types, production accumulation hooks for existing combat/pickup/descent paths, and immutable stats snapshots.
- PD-007 [AC-005] [FR-007] complete: Add a Stats route/control tree with four KPI tiles, typed BarChart depth buckets, typed LineChart dealt/taken series, stable colors/identity, and This Run/Lifetime scope without stepping simulation.
- PD-008 [AC-006] [FR-008] complete: Map Easy/Normal/Hard to complete scaling records and make StartRun copy the current profile choice into run state once; settings edits never mutate the active run's scaling.

## Contract Impact
- PC-001 [PD-001] product source: `Model.fs`, `Render.fs`, `GameShell.fs`, `EvidenceCommands.fs`, and a product-owned M7 UI module expose typed product values/functions; Release tests, screenshots, layout reports, persistence-request evidence, and performance artifacts version them together.

## Verification Obligations
- VO-001 [PD-001] [PC-001] semanticTest: Focused M7 tests cover HUD/layout, generic shell composition, real retained clicks/raw keys/rebinding/resolution, pure persistence requests, stats/charts, and AC #13; full Release Test/Verify, raster inspection, and exact representative performance evidence bind the final candidate.

## Performance Intent
- Active producer-owned PI-GENERATED-GAME remains authoritative. Add distinct menu, HUD, and stats production routes before implementation, exact-gating bound controls, HUD primitives/minimap rooms, KPI/bucket/series counts, and maximum UI scene nodes while retaining p95 16.67 ms, p99 25 ms, zero catch-up, and scene-node 4096 budgets. Bounded headless evidence does not claim compositor/vsync metrics.

## Migration Posture
- PM-001 [PC-001] additive: Versioned preference codecs degrade corrupt/unknown data to current defaults. No run save exists; M9's atomic profile-file backend remains outside this change.

## Generated View Impact
- GV-001 [PD-001] readiness: Regenerate work-model/analysis/verify/summary/ship verdict, interaction/layout/visual/persistence evidence, performance evidence, and performance critic request against final sources and exact candidate SHA.

## Accepted Deferrals
No accepted plan deferrals recorded.

## Planning Findings
No blocking planning findings recorded.

## Advisory Notes
- Optional Governance pointers remain compatibility facts only.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd tasks --work 008-m7-ui-menus-stats`.
