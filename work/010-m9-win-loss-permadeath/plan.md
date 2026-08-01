---
schemaVersion: 1
workId: 010-m9-win-loss-permadeath
title: M9 Win Loss Permadeath
stage: plan
changeTier: tier1
status: planned
sourceSpec: work/010-m9-win-loss-permadeath/spec.md
sourceClarifications: work/010-m9-win-loss-permadeath/clarifications.md
sourceChecklist: work/010-m9-win-loss-permadeath/checklist.md
publicOrToolFacingImpact: true
---

# M9 Win Loss Permadeath Plan

Prose status: planned

## Source Snapshot
- spec: work/010-m9-win-loss-permadeath/spec.md sha256:fdb71afd34012a88309c13c93a7cfc2c0b4917575e2f0678e069ece842ce4500 schemaVersion:1
- clarifications: work/010-m9-win-loss-permadeath/clarifications.md sha256:b4d5b73eb33bc39313d47880fb986ee470314357479c060407032448567b5beb schemaVersion:1
- checklist: work/010-m9-win-loss-permadeath/checklist.md sha256:70b19829a32be063c37cd8b82aec6613e5958e701a1e9627f0150ed38925419c schemaVersion:1

## Plan Scope
- Work item 010-m9-win-loss-permadeath is planned from the current specification, clarification, and checklist facts.
- Requirement count: 5.
- Clarification decision count: 0.
- Checklist result count: 5.

## Plan Decisions
- PD-001 [AC-001] [FR-001] complete: Add an explicit terminal outcome/summary to the model and finalize the run from the production floor-6 boss damage route.
- PD-002 [AC-002] [FR-002] complete: Detect death after the fixed-step drain and funnel it through the same idempotent end-run reducer.
- PD-003 [AC-003] [FR-003] complete: Make score and unlock evaluation pure functions; terminal reduction updates lifetime facts and best score by seed before resetting transient run fields.
- PD-004 [AC-004] [FR-004] complete: Add a versioned JSON codec and product-owned debounced store whose flush writes a sibling temp file then atomically renames it; live shell init loads from the platform path, while tests inject a safe temporary directory.
- PD-005 [AC-005] [FR-005] complete: Render a result overlay from retained summary state and route terminal music plus persistence effects through the shell host boundary.

## Contract Impact
- PC-001 [PD-001] [PD-002] [PD-003] [PD-004] [PD-005] product contract: Adds RunOutcome, RunSummary, best-score profile JSON v1, and host-store behavior without changing framework package APIs.

## Verification Obligations
- VO-001 [PD-001] [PD-002] [PD-003] [PD-004] [PD-005] [PC-001] semanticTest: Focused Release tests prove production death/victory, idempotent discard, score/unlocks, real-file debounce/atomic/load/fallback/version behavior, rendering, audio, and shell persistence routing; full Release Test/Verify and governed workloads prove regression and performance readiness.

## Performance Intent
No performance intent is declared for this work item.

## Migration Posture
- PM-001 [PC-001] additive: Existing profiles without a file load the default profile; malformed or non-v1 documents are preserved on disk and ignored until a later valid save, with no migration of run state.

## Generated View Impact
- GV-001 [PD-001] [PD-004] readiness: Commit the M9 ship verdict and real-file verification evidence; performance evidence is regenerated at the exact candidate without changing the authored workload contract.

## Accepted Deferrals
No accepted plan deferrals recorded.

## Planning Findings
No blocking planning findings recorded.

## Advisory Notes
- Optional Governance pointers remain compatibility facts only.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd tasks --work 010-m9-win-loss-permadeath`.
