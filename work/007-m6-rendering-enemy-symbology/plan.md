---
schemaVersion: 1
workId: 007-m6-rendering-enemy-symbology
title: M6 Rendering Enemy Symbology
stage: plan
changeTier: tier1
status: planned
sourceSpec: work/007-m6-rendering-enemy-symbology/spec.md
sourceClarifications: work/007-m6-rendering-enemy-symbology/clarifications.md
sourceChecklist: work/007-m6-rendering-enemy-symbology/checklist.md
publicOrToolFacingImpact: true
---

# M6 Rendering Enemy Symbology Plan

Prose status: planned

## Source Snapshot
- spec: work/007-m6-rendering-enemy-symbology/spec.md sha256:81fc71e362e81a337059cb392e72d44a496bf332a5cf686411f09974588fc1b8 schemaVersion:1
- clarifications: work/007-m6-rendering-enemy-symbology/clarifications.md sha256:bb80f558938400051c939fcb614e08b68bdef3b531314fbd9237f645c12b4d56 schemaVersion:1
- checklist: work/007-m6-rendering-enemy-symbology/checklist.md sha256:299a14b99077eef6197cf434cd2ad397f90654c3e197381ad066234a908a0a8b schemaVersion:1

## Plan Scope
- Work item 007-m6-rendering-enemy-symbology is planned from the current specification, clarification, and checklist facts.
- Requirement count: 6.
- Clarification decision count: 0.
- Checklist result count: 6.

## Plan Decisions
- PD-001 [AC-001] [FR-001] complete: Introduce a render-owned ordered `RenderLayer` projection and have production `View.view` flatten it in one explicit painter-order list.
- PD-002 [AC-002] [FR-002] complete: Add `Rogue3.Render` after the sim model; map live M5 EnemyActors to exact-radius `Token`s and use `Symbology.token` only at the render boundary.
- PD-003 [AC-002] [FR-003] complete: Assert grammar-aware linter findings by severity and channel, accepting only Size while retaining raster inspection at 1280x720.
- PD-004 [AC-003] [FR-004] complete: Store immutable particle values in Model, append requested bursts in stable order, keep the newest 600, advance and cull on fixed steps, and render alpha from remaining lifetime.
- PD-005 [AC-004] [FR-005] complete: Store camera transition direction and elapsed fixed ticks; sample one pure world translation clamped over 42 ticks (0.35 seconds at 120 Hz), lowering settled state to identity.
- PD-006 [AC-005] [FR-006] complete: Extend `maximum-content` before implementation to exact-gate eight M6 symbols, 600 particles, eleven layers, and an active camera transition through production update + view; acknowledge the refreshed definition digest only after inspection.

## Contract Impact
- PC-001 [PD-001] product source: src/Rogue3/Render.fs, Model.fs, View.fs, GameplayVisualInventory.fs, and PerformanceEvidence.fs expose product-owned M6 values/functions; tests, raster artifacts, catalog, and readiness evidence version them together.

## Verification Obligations
- VO-001 [PD-001] [PC-001] semanticTest: Focused M6 tests cover exact order, complete enemy map, accepted Size warning, pool cap/cull/fade, and transition samples; full Release Test/Verify, catalog audit, PNG inspection, and maximum-content evidence bind the exact candidate.

## Performance Intent
- Active producer-owned PI-GENERATED-GAME remains authoritative. Maximum M6 scale adds eight enemy symbols, exactly 600 retained particles, eleven ordered layers, and one active camera transition through production update + view. Existing p95 16.67 ms, p99 25 ms, zero catch-up, and scene-node 4096 budgets remain hard; bounded headless evidence cannot claim compositor/vsync metrics.

## Migration Posture
- PM-001 [PC-001] additive: Existing M0-M5 model values are extended additively; no persisted run-save format exists and M6 introduces no migration obligation.

## Generated View Impact
- GV-001 [PD-001] readiness: Regenerate the M6 work model, analysis, verify, summary, ship verdict, visual/legibility evidence, performance evidence, and performance critic request against final authored sources.

## Accepted Deferrals
No accepted plan deferrals recorded.

## Planning Findings
No blocking planning findings recorded.

## Advisory Notes
- Optional Governance pointers remain compatibility facts only.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd tasks --work 007-m6-rendering-enemy-symbology`.
