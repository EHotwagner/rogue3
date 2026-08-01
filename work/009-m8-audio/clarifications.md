---
schemaVersion: 1
workId: 009-m8-audio
title: M8 Audio
stage: clarify
changeTier: tier1
status: clarified
sourceSpec: work/009-m8-audio/spec.md
publicOrToolFacingImpact: true
---

# M8 Audio Clarifications

## Source Specification
- work/009-m8-audio/spec.md

## Clarification Questions
- **CQ-001**: How are several gameplay events occurring in one drained host frame preserved without relying on a lossy net model diff?
- **CQ-002**: What establishes exactly-one music semantics when `Audio.interpret` intentionally records requests but does not own playback policy?
- **CQ-003**: What does the startup cue contain when initial settings and title context are loaded rather than transitioned into?
- **CQ-004**: Does M8 prove sound reached a physical output device?

## Answers
- CQ-001 → Production state carries bounded per-transition audio-event values emitted while the fixed-step drain resolves events; `forTransition` translates that ordered batch, while direct messages remain direct cues.
- CQ-002 → Product policy emits no music request for a stable context and emits exactly `StopMusic; PlayMusic(track,true)` for each context replacement; the interpreter preserves that order.
- CQ-003 → `Started m m` emits clamped effective master volume followed by the title loop, closing the loaded-state blind spot at the same host sink.
- CQ-004 → No. Evidence proves requested values through the host recording seam. Asset/device availability and audible speaker output remain explicitly unclaimed.

## Decisions
- **DEC-001** [FR-001] [FR-004]: Add an ordered, frame-bounded product audio-event batch to the model, reset it at each production transition, and translate every occurrence to the §10 cue table so simultaneous cues are neither collapsed nor inferred from ambiguous totals.
- **DEC-002** [FR-002]: Derive music context from the message and before/after game state; startup plays title, and every context change requests `StopMusic` before exactly one looping track.
- **DEC-003** [FR-003]: Define effective volume as `0.0` while muted and otherwise `Audio.clampVolume MasterVolume`; emit it on Started, SetMasterVolume, and SetMuted.
- **DEC-004** [FR-004]: Test through `interactiveHost.Init` and production host `Update` effect batches flattened into `Audio.interpret`; direct cue-map tests may diagnose values but cannot alone satisfy host-boundary acceptance.

## Accepted Deferrals
No accepted deferrals recorded.

## Remaining Ambiguity
No blocking ambiguity remains.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd checklist --work 009-m8-audio`.
