---
schemaVersion: 1
workId: 001-m0-scaffold-fixed-step-loop
title: M0 Scaffold Fixed Step Loop
stage: clarify
changeTier: tier1
status: clarified
sourceSpec: work/001-m0-scaffold-fixed-step-loop/spec.md
publicOrToolFacingImpact: true
---

# M0 Scaffold Fixed Step Loop Clarifications

## Source Specification
- work/001-m0-scaffold-fixed-step-loop/spec.md

## Clarification Questions
- **CQ-001**: Does upstream AC #8 require decimal `0.033` itself to yield four steps, despite `0.033 < 4/120`?
- **CQ-002**: Must M0 introduce the double-buffered interpolation loop even though interpolation is listed as a stretch goal?

## Answers
- CQ-001 → no. The exact stated basis is `1/30`; tests separately pin decimal `0.033` to three steps and exact `1.0/30.0` to four.
- CQ-002 → no. M0 uses the source-spec-sanctioned latest-state render; interpolation remains explicitly out of scope.

## Decisions
- **DEC-001** [CQ-001] [FR-002] [AC-002] [AC-003]: Preserve mathematically correct `FixedStep.drainWith` semantics and test both decimal and exact inputs.
- **DEC-002** [CQ-002] [FR-001]: Retain a single simulation state plus banked accumulator for M0; do not feed a render interpolant into the simulation.
- **DEC-003** [FR-003]: Derive streams in a fixed order: split the run generator once for layout, then split the continuation once for drops.

## Accepted Deferrals
No accepted deferrals recorded.

## Remaining Ambiguity
No blocking ambiguity remains.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd checklist --work 001-m0-scaffold-fixed-step-loop`.
