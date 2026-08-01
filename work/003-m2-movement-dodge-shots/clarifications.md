---
schemaVersion: 1
workId: 003-m2-movement-dodge-shots
title: M2 Movement Dodge Shots
stage: clarify
changeTier: tier1
status: needsAnswers
sourceSpec: work/003-m2-movement-dodge-shots/spec.md
publicOrToolFacingImpact: true
---

# M2 Movement Dodge Shots Clarifications

## Source Specification
- work/003-m2-movement-dodge-shots/spec.md

## Clarification Questions
- CQ-001 [AMB:AMB-001] blocking: Does fire lockout cover only the i-frame window or all roll commitment?
- CQ-002 [AMB:AMB-002] blocking: How exactly does the integer pierce stat spend across enemy overlaps?
- CQ-003 [AMB:AMB-003] blocking: How does M2 prove pierce without prematurely implementing M3 damage?
- CQ-004 [AMB:AMB-004] blocking: Which collision routes apply to the player and to projectiles?

## Answers
- CQ-001 → The source explicitly says "during the i-frame window"; lock firing for 0.40 s, while roll control decay continues through 0.45 s.
- CQ-002 → Pierce is the number of extra distinct enemies passed through. A shot with pierce zero expires on its first distinct enemy; pierce two expires on its third. Persistent overlap with an already-hit id does not spend again.
- CQ-003 → M2 consumes stable ordered enemy-hit ids and updates shot bookkeeping only. M3 later owns damage, health, knockback, flash, and enemy pruning.
- CQ-004 → Extend the product-owned circular mover with packaged segment/AABB casts for an axis-separated radius-13 sweep, retaining `Collision.slideCircle` for overlap response. Use the same swept primitive for fast projectiles, never the rigid-body engine.

## Decisions
- DEC-001 [CQ-001] [AMB:AMB-001] [FR-004] [AC-003]: Fire lockout equals the 0.40-second i-frame window; roll control state lasts 0.45 seconds and cooldown starts at activation.
- DEC-002 [CQ-002] [AMB:AMB-002] [FR-007] [AC-005]: Track already-hit enemy ids per shot and spend one of pierce+1 total distinct-hit allowances in deterministic supplied order.
- DEC-003 [CQ-003] [AMB:AMB-003] [FR-007] [AC-005]: M2 accepts ordered enemy-hit ids as a minimal pure seam and performs no damage/health side effects.
- DEC-004 [CQ-004] [AMB:AMB-004] [FR-003] [FR-007]: Route player circles through an X-then-Y `Collision.sweepCircle` composed from packaged segment/AABB casts plus `slideCircle`; route fast shot walls through the same cast primitive and explicit remaining-bounce accounting.

## Accepted Deferrals
No accepted deferrals recorded.

## Remaining Ambiguity
- None.

## Lifecycle Notes
- Incoming deferral audit: M0 and M1 carry no accepted deferral targeting M2. M1 native-gamepad and continuous-pointer host gaps remain unchanged release obligations outside this work item.
- Next lifecycle action: `fsgg-sdd checklist --work 003-m2-movement-dodge-shots`.
