---
schemaVersion: 1
workId: 011-m10-acceptance-determinism
title: M10 Acceptance Determinism
stage: clarify
changeTier: tier1
status: clarified
sourceSpec: work/011-m10-acceptance-determinism/spec.md
publicOrToolFacingImpact: true
---

# M10 Acceptance Determinism Clarifications

## Source Specification
- work/011-m10-acceptance-determinism/spec.md

## Clarification Questions
- CQ-001: What is the canonical byte encoding for "byte-identical" in §14.1 and §13, given the source specification names no format?
- CQ-002: Where must the §14.14 secret reveal happen, given the reveal function already exists but nothing detonation-driven calls it?
- CQ-003: Which §14 scenarios have no production seam at all, and are they in scope for this milestone?
- CQ-004: How is the 24-scenario sweep organised so that "all 24 green" is one observable fact rather than a reading of nine milestone test files?
- CQ-005: Does the M10 change touch a measured production performance route, and if so which workload proves it?

## Answers
- CA-001: Not `sprintf "%A"`. Measured on this toolchain, `sprintf "%A" [1..600]` and `sprintf "%A" ([1..599] @ [999])` produce the identical 401-character string, because `%A` truncates a collection after 100 elements and appends an ellipsis. The production maximum-content model carries 600 particles, 120 enemy bullets and 40 shots, so a `%A` golden cannot see a divergence past element 100.
- CA-002: Inside the fixed-step bomb resolution, so the door-graph transition and the blast land in the same step as §14.14 requires. `FloorGeneration.revealSecret` is already atomic over doors, hidden flag, graph and pending set; only the trigger is missing.
- CA-003: §14.15 (open-door traversal) and §14.16 (key-door unlocking) have no production seam: nothing in `src/Rogue3` reads `FloorGeneration.DoorState.LockedKey`, and `Floor.CurrentRoom` is only written by generation, `DescendFloor`, and the `EnterM5Room` teleport-by-id seam. They are in scope, because a scenario cannot be discharged by a test-only replica.
- CA-004: One dedicated Release test list with exactly one named test per scenario, each driving production functions. Scenarios already proven elsewhere are re-asserted there rather than cross-referenced.
- CA-005: Yes. The same-step secret reveal runs inside `resolveBombs`, which is on the fixed-step hot path. Replay and door messages are not per-frame routes.

## Decisions
- DEC-001 [CQ-001] [CA-001]: The product owns a canonical structural encoder, `Rogue3.Determinism`, that walks records, unions, tuples, maps, sets and sequences in declaration/comparison order with invariant round-trip number formatting and no length limit. It is the single definition of byte-identical for floors, models and replays.
- DEC-002 [CQ-001] [CA-001]: `PerformanceEvidence.modelDefinitionFingerprint` moves onto the same encoder. It digests workload initial states and runner-receipt fingerprints with `sprintf "%A"` today and is subject to the identical truncation, so the maximum-content authorship digest cannot currently distinguish models differing past element 100. Every authored workload digest is re-derived as a consequence.
- DEC-003 [CQ-001]: A replay log entry is a strictly increasing sequence number plus one production `Msg`. Timing travels inside the message as the `Tick` payload, so actions and timing are one ordered stream and no wall clock is read. Replay folds the production `Model.update` and rejects a log whose sequence numbers are not unique and strictly increasing.
- DEC-004 [CQ-002] [CA-002]: The bomb blast reveals every pending secret whose adjacent room is the current room and whose shared wall lies inside the blast radius, inside the same fixed step, through `FloorGeneration.revealSecret`. The explicit `RevealSecret` message stays for direct evidence routes.
- DEC-005 [CQ-003] [CA-003]: `TraverseDoor` and `UnlockDoor` become production `Msg` cases over new `FloorGeneration.tryTraverseDoor` and `tryUnlockDoor` transitions. Traversal requires an open or boss door plus graph adjacency and lands the player at the reciprocal doorway; unlocking spends exactly one key and opens both reciprocal records or changes nothing.
- DEC-006 [CQ-003]: The prior-art draft is adopted only for the shape of the two door transitions. Its `Replay.fs` is discarded: it depends on `sprintf "%A"` as a golden encoding, which CA-001 disproves.
- DEC-007 [CQ-004] [CA-004]: The sweep lives in `tests/Rogue3.Tests/M10AcceptanceDeterminismTests.fs` as scenario tests AC01 through AC24, and a guard test asserts the list contains exactly 24 scenario tests with no gaps or duplicates, so a silently dropped scenario cannot pass green.
- DEC-008 [CQ-005] [CA-005]: One new bounded-headless workload, `secret-reveal`, detonates exactly one staggered-fuse bomb per sampled fixed step against the maximum pending-secret set at maximum content, so the added hot-path cost is measured on every sample. The six existing workloads keep their identities and scale gates.
- DEC-009 [CQ-005]: Replay carries no frame budget and is declared as a non-performance cost driver, because it is an offline verification fold over production `update` bounded by the authored log length, not a per-frame route. It is exercised in the Release suite instead.

## Accepted Deferrals
- DEC-010: No work is deferred out of this milestone. M7, M8 and M9 each deferred the acceptance sweep or the replay and determinism work to M10, and both are discharged here; this is the terminal non-deferred milestone, so an open deferral aimed at it would be a blocking finding.

## Remaining Ambiguity
None. Every recorded question has a decision.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd checklist --work 011-m10-acceptance-determinism`.
