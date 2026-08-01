---
schemaVersion: 1
workId: 002-m1-input-twin-stick-control
title: M1 Input Twin Stick Control
stage: clarify
changeTier: tier1
status: clarified
sourceSpec: work/002-m1-input-twin-stick-control/spec.md
publicOrToolFacingImpact: true
---

# M1 Input Twin Stick Control Clarifications

## Source Specification
- work/002-m1-input-twin-stick-control/spec.md

## Clarification Questions
- CQ-001 [AMB:AMB-001] blocking: Which active aim source wins deterministically?
- CQ-002 [AMB:AMB-002] blocking: Which fixed step sees an edge during multi-step catch-up?
- CQ-003 [AMB:AMB-003] blocking: Does firing wait one cadence after acquisition?
- CQ-004 [AMB:AMB-004] blocking: How can gamepad state be implemented without a pinned host polling API?

## Answers
- CQ-001 → arrow keys win when held, otherwise a non-deadzone right stick wins, otherwise a finite non-zero cursor-minus-player vector supplies mouse aim.
- CQ-002 → only the first drained fixed step sees the derived key edge; later steps receive an empty edge set. If no fixed step drains, previous is not advanced and the edge remains eligible for the next Tick.
- CQ-003 → acquisition fires immediately, stores a 0.4-second cooldown, and subtracts fixed dt until the next event; release resets readiness so re-press is immediate.
- CQ-004 → `InputChanged` accepts a complete pure snapshot including both gamepad sticks, trigger, and buttons. The pinned InteractiveAppHost supplies no native gamepad polling field, so no I/O is invented; host polling remains a documented release integration obligation outside this product contract.

## Decisions
- DEC-001 [CQ-001] [AMB:AMB-001] [FR-002] [FR-003]: Resolve aim in priority order arrows, active right stick, then mouse; movement is resolved independently from WASD plus left stick.
- DEC-002 [CQ-002] [AMB:AMB-002] [FR-001]: Derive the key edge at Tick, give it to the first drained step only, and commit current as previous after at least one simulated step.
- DEC-003 [CQ-003] [AMB:AMB-003] [FR-004]: Fire immediately on held acquisition, repeat every 0.4 simulated seconds, and reset cadence readiness on release.
- DEC-004 [CQ-004] [AMB:AMB-004] [FR-005]: Implement the full pure gamepad snapshot/message seam and expose the pinned-host capability absence honestly; do not claim native polling evidence.

## Accepted Deferrals
No accepted deferrals recorded.

## Remaining Ambiguity
- None.

## Lifecycle Notes
- M0 has no accepted clarification deferral targeting M1.
- Next lifecycle action: `fsgg-sdd checklist --work 002-m1-input-twin-stick-control`.
