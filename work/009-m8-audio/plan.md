---
schemaVersion: 1
workId: 009-m8-audio
title: M8 Audio
stage: plan
changeTier: tier1
status: planned
sourceSpec: work/009-m8-audio/spec.md
sourceClarifications: work/009-m8-audio/clarifications.md
sourceChecklist: work/009-m8-audio/checklist.md
publicOrToolFacingImpact: true
---

# M8 Audio Plan

Prose status: planned

## Source Snapshot
- spec: work/009-m8-audio/spec.md sha256:1cd0875903f04c738a90c6fea3d1b020c4ae266968440c9db5cda6ab83b0537d schemaVersion:1
- clarifications: work/009-m8-audio/clarifications.md sha256:8f22524bdf8abba6ad6ed2cfa6a32bc0e9800d77471ee7046747bf8a67056964 schemaVersion:1
- checklist: work/009-m8-audio/checklist.md sha256:f6b49f9b6eb8b915437583bc6acdc9a2dbf7507abfec0c66f0de10ad2e5697aa schemaVersion:1

## Plan Scope
- Work item 009-m8-audio is planned from the current specification, clarification, and checklist facts.
- Requirement count: 4.
- Clarification decision count: 4.
- Checklist result count: 4.

## Plan Decisions
- PD-001 [AC-001] [FR-001] complete: Add a bounded ordered `AudioEvent` batch to production model transitions and map each occurrence in `AudioCues.forTransition` to the exact §10 smart-constructed SFX request; direct message cues are appended in deterministic order.
- PD-002 [AC-002] [FR-002] complete: Define a product music-context projection and emit startup `PlayMusic title true` plus `StopMusic; PlayMusic context true` only on title/floor/shop/boss/end replacements, preventing duplicate starts in a stable context.
- PD-003 [AC-003] [FR-003] complete: Route effective volume through `Audio.setMasterVolume`, using zero while muted and `Audio.clampVolume` otherwise, on Started and both settings transitions.
- PD-004 [AC-004] [FR-004] complete: Add focused Release tests that drive real update transitions through `interactiveHost`/production host effect extraction and assert the resulting `AudioEvidence.Requested`; retain record-only/device caveats.

## Contract Impact
- PC-001 [PD-001] product source: `Model.fs` gains product-internal event values/state, `AudioCues.fs` owns cue/music policy, and `M8AudioTests.fs` plus readiness evidence bind the existing `ViewerEffect.PlayAudio` host contract; no framework signature or package pin changes.

## Verification Obligations
- VO-001 [PD-001] [PC-001] semanticTest: Run focused M8 and full Release tests with TRX, full Verify, SDD verify/ship, and exact-SHA functional/performance critics; assert cue values, simultaneous order, startup/updates at the sink, one-loop replacement, and clamp/mute without claiming speaker output.

## Performance Intent
- The existing producer-owned PI-GENERATED-GAME and six authored runner-issued workloads remain authoritative. M8 adds bounded per-transition event-list mapping only; retain p95 16.67 ms, p99 25 ms, zero catch-up, maximum-content structural budgets, and bounded-headless capability caveats.

## Migration Posture
- PM-001 [PC-001] additive: Existing settings/profile data remains compatible; the new audio-event batch is transient per-transition state and is not persisted.

## Generated View Impact
- GV-001 [PD-001] readiness: Regenerate analysis/work-model/verify/summary/ship verdict, Release TRX, audio request evidence, performance evidence, critic artifacts, and feedback/audit against final sources and exact candidate SHA.

## Accepted Deferrals
No accepted plan deferrals recorded.

## Planning Findings
No blocking planning findings recorded.

## Advisory Notes
- Optional Governance pointers remain compatibility facts only.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd tasks --work 009-m8-audio`.
