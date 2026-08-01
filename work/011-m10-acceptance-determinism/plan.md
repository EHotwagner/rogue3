---
schemaVersion: 1
workId: 011-m10-acceptance-determinism
title: M10 Acceptance Determinism
stage: plan
changeTier: tier1
status: planned
sourceSpec: work/011-m10-acceptance-determinism/spec.md
sourceClarifications: work/011-m10-acceptance-determinism/clarifications.md
sourceChecklist: work/011-m10-acceptance-determinism/checklist.md
publicOrToolFacingImpact: true
---

# M10 Acceptance Determinism Plan

Prose status: planned

## Source Snapshot
- spec: work/011-m10-acceptance-determinism/spec.md sha256:78f91dd2ec97e8c5c9996355a8ef1ebe7e42cbf6ea221897a7fab26edcd07346 schemaVersion:1
- clarifications: work/011-m10-acceptance-determinism/clarifications.md sha256:204e7d9dffa2e3df433572f32597471c8df8cfc4ec3de46749bb127fbf91da79 schemaVersion:1
- checklist: work/011-m10-acceptance-determinism/checklist.md sha256:8002ca696d087db1a2a5d61c6361dc0ace5df77ea62c9c71a3a6a7ce655916db schemaVersion:1

## Plan Scope
- Work item 011-m10-acceptance-determinism is planned from the current specification, clarification, and checklist facts.
- Requirement count: 8.
- Clarification decision count: 9.
- Checklist result count: 9.

## Plan Decisions
- PD-001 [AC-001] [FR-001] complete: Author `tests/Rogue3.Tests/M10AcceptanceDeterminismTests.fs` as one Release list holding exactly one named test per source-specification scenario 1 through 24, each driving production `Model`, `FloorGeneration`, `Entities`, `Render` or shell-host functions, plus a guard test that fails when the list does not hold exactly the 24 distinct scenario ids.
- PD-002 [AC-002] [FR-002] complete: Add `src/Rogue3/Determinism.fs`, a reflective canonical structural encoder over records, unions, tuples, maps, sets and sequences with declaration/comparison ordering, invariant round-trip number formatting and no length limit, exposing floor and model byte encodings and their digests.
- PD-003 [AC-003] [FR-003] complete: Prove stream independence by advancing `DropRng` a differing number of draws between two same-seed runs and comparing the canonical floor encodings, rather than by re-asserting the generator signature.
- PD-004 [AC-004] [FR-004] complete: Walk a whole run's floors through the production item pool, collect every pedestal, shop slot and boss reward, and assert identical ids/prices between differently-played same-seed runs and no repeated item id within one run.
- PD-005 [AC-005] [FR-005] complete: Assert the exact §12 Hard row latches at `StartRun` and that the latched values reach `PostHitInvulnTicks`, enemy hit points and the drop-nothing weight, while a mid-run `SetDifficulty` leaves `ActiveDifficulty` untouched.
- PD-006 [AC-006] [FR-006] complete: Extend fixed-step bomb resolution so an explosion whose blast radius covers the wall shared with a pending secret adjacent to the current room applies `FloorGeneration.revealSecret` in that same step, and expose a deterministic candidate counter for the scan.
- PD-007 [AC-007] [FR-007] complete: Add `src/Rogue3/Replay.fs` carrying an ordered `InputLogEntry` log, a strictly-increasing sequence guard, and a fold of the log through production `Model.update` from `initialModelForSeed`, compared with the DEC-001 canonical encoding.
- PD-008 [AC-008] [FR-008] complete: Add `FloorGeneration.tryUnlockDoor`/`tryTraverseDoor` and the `UnlockDoor`/`TraverseDoor` production messages, so traversal needs an open or boss door plus graph adjacency and lands at the reciprocal doorway, and unlocking spends exactly one key or changes nothing.
- PD-009 [DEC-010] acceptedDeferral: DEC-010 defers no work out of M10. M10 is the terminal non-deferred milestone, so planning discharges the inbound deferrals instead of forwarding any: the M7 and M8 "M10 acceptance sweep" deferrals land in PD-001, and the M9 "M10 replay/determinism acceptance work" deferral lands in PD-002 and PD-007. Any deferral still aimed at M10 after this item ships is a blocking finding, not a note.
- PD-010 [CR-009] acceptedDeferral: CR-009 is the checklist review row that carries DEC-010 forward. It needs no separate plan work because DEC-010 forwards nothing; PD-009 records the discharge, and tasks T018/T023 verify that no earlier work item still points an open deferral at M10.

## Contract Impact
- PC-001 [PD-002] [PD-006] [PD-007] [PD-008] product contract: Adds the product-owned `Rogue3.Determinism` canonical encoding and `Rogue3.Replay` log contracts, two production `Msg` cases, two `FloorGeneration` door transitions, and one simulation cost counter; no framework package API changes.

## Verification Obligations
- VO-001 [PD-001] [PD-002] [PD-003] [PD-004] [PD-005] [PD-006] [PD-007] [PD-008] [PC-001] semanticTest: A focused Release run of the 24-scenario sweep plus the determinism and replay tests, a full Release `Test`/`Verify` run, and the regenerated bounded-headless workload evidence including the new `secret-reveal` workload prove behaviour, regression and performance readiness at the exact candidate.

## Performance Intent
No performance intent is declared for this work item.

## Migration Posture
- PM-001 [PC-001] additive: The new modules, messages and counter are additive. Existing saved profiles, floors and messages are unaffected, and the re-derived workload digests fail closed until reviewed and copied rather than migrating silently.

## Generated View Impact
- GV-001 [PD-001] [PD-002] workModel: `readiness/011-m10-acceptance-determinism/` refreshes from current plan sources or reports staleGeneratedView, the M10 ship verdict is committed, and `readiness/performance-evidence.json`, `readiness/performance-intent.yml` and `readiness/performance-critic-request.json` are regenerated at the exact candidate.

## Accepted Deferrals
- DEC-010 acceptedDeferral: Nothing leaves M10. The deferral exists to make the inbound M7/M8/M9 deferrals visible to tasks and evidence so their discharge is asserted rather than assumed.
- CR-009 acceptedDeferral: The checklist row carrying DEC-010; it stays visible so evidence must show the deferral ledger is empty at ship, not merely that M10's own scenarios are green.

## Planning Findings
No blocking planning findings recorded.

## Advisory Notes
- Optional Governance pointers remain compatibility facts only.
- `sprintf "%A"` truncates a collection after 100 elements, so it cannot serve as a determinism golden or a fingerprint for the 600-particle maximum-content model. This is recorded once here and drives PD-002, PD-007 and the workload-digest re-derivation.
- Performance posture (the single typed declaration stays `readiness/performance-intent.yml`, produced from `src/Rogue3/PerformanceEvidence.fs`; this note records only how M10 moves it). The added same-step secret-reveal scan sits on the fixed-step hot path, so a seventh bounded-headless workload, `secret-reveal`, detonates exactly one staggered-fuse bomb per sampled step at maximum content against the maximum pending-secret set, gated by the existing 16.67 ms p95 / 25.0 ms p99 normal-play budget with zero permitted catch-up frames; expected scale is at most eight pending secret/adjacent pairs scanned per detonation and at most eight live pending secrets. The six existing workloads keep their identities, budgets and observed scale gates. Replacing the `sprintf "%A"` model fingerprint re-derives every authored workload definition digest, so all seven are re-reviewed and copied from a fresh measurement. Replay carries no frame budget: it is an offline verification fold over production `update` bounded by the authored log length, declared as a non-performance cost driver and exercised in the Release suite.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd tasks --work 011-m10-acceptance-determinism`.
