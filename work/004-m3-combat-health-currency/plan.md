---
schemaVersion: 1
workId: 004-m3-combat-health-currency
title: M3 combat health and currency
stage: plan
changeTier: tier1
status: planned
sourceSpec: work/004-m3-combat-health-currency/spec.md
sourceClarifications: work/004-m3-combat-health-currency/clarifications.md
sourceChecklist: work/004-m3-combat-health-currency/checklist.md
publicOrToolFacingImpact: true
---

# M3 combat health and currency Plan

Prose status: planned

## Source Snapshot
- spec: work/004-m3-combat-health-currency/spec.md sha256:739bc976fc12daab22c106a6877d6d3f8b787539cf78efd04c1e379294ddaa40 schemaVersion:1
- clarifications: work/004-m3-combat-health-currency/clarifications.md sha256:2e0baf1a9d74517014322f29a84f5c230833f1c8dc0921ece385681464a7e60f schemaVersion:1
- checklist: work/004-m3-combat-health-currency/checklist.md sha256:aad0cc445f27996a7f74c3eea373caec1f546b37681e3408c1887be103fd54da schemaVersion:1

## Plan Scope
- Work item 004-m3-combat-health-currency is planned from the current specification, clarification, and checklist facts.
- Requirement count: 1.
- Clarification decision count: 0.
- Checklist result count: 1.

## Plan Decisions
- PD-001 [AC-001] [FR-001] complete: Extend the immutable Model with source-shaped combat values and resolve one fixed-order combat phase after movement: build 64-pixel SpatialGrid indices, resolve shot hits, enemy bullets, per-enemy contact timers, bombs and chained blasts, then commit death without entering M9 GameOver.

## Contract Impact
- PC-001 [PD-001] source contract: Model.fs owns Enemy, EnemyBullet, Bomb, Health, Currency, stat-modifier, shop-slot and descent-carry values; ShotHitsThisTick is removed so all pierce and damage share the SpatialGrid-backed production resolver.

## Verification Obligations
- VO-001 [PD-001] [PC-001] semanticTest: Run focused deterministic M3 tests for AC 3, 6 and 11 plus bombs, heart ordering, black bursts, broadphase and descent; then full Release, Test, Verify, exact performance evidence and fresh-context critic review.

## Performance Intent
- Producer-owned `readiness/performance-intent.yml` remains the sole contract. Preserve M2 complete-model/all-frame definition closure and exact 40 shots, 8 obstacles, 30 targets, 736 wall primitives, 2,400 homing considerations and multishot 3 gates while adding observable combat broadphase/narrow-phase counters for the production route.

## Migration Posture
- PM-001 [PC-001] replace: Consume the temporary M2 ShotHitsThisTick map and its tests in the new resolver; preserve M0-M2 external behavior and leave M4+ floor generation and M9 screen/meta transitions untouched.

## Generated View Impact
- GV-001 [PD-001] productReadiness: Regenerate performance intent/evidence, evidence graph/audit, SDD analysis/verify/ship verdict and roadmap/feedback views from the exact candidate.

## Accepted Deferrals
No accepted plan deferrals recorded.

## Planning Findings
No blocking planning findings recorded.

## Advisory Notes
- Optional Governance pointers remain compatibility facts only.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd tasks --work 004-m3-combat-health-currency`.
