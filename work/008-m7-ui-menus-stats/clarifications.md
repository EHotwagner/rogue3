---
schemaVersion: 1
workId: 008-m7-ui-menus-stats
title: M7 Ui Menus Stats
stage: clarify
changeTier: tier1
status: clarified
sourceSpec: work/008-m7-ui-menus-stats/spec.md
publicOrToolFacingImpact: true
---

# M7 Ui Menus Stats Clarifications

## Source Specification
- work/008-m7-ui-menus-stats/spec.md

## Clarification Questions
- **CQ-001**: How can M7 satisfy persistent game-specific settings without implementing M9's atomic profile-file backend early?
- **CQ-002**: Does volume configuration implement M8 audio playback?
- **CQ-003**: Which UI route establishes interaction and responsive-layout evidence?

## Answers
- CQ-001 → M7 owns the versioned `MetaProfile` values/codecs and `PersistenceEffect.Save` request observed through the record-only interpreter; M9 owns actual platform-file durability.
- CQ-002 → M7 owns the clamped live profile value and mute semantics; it emits no M8 event cues, music loop, or playback claim.
- CQ-003 → Tests drive the shipped `interactiveHost` through retained pointer scripts and its raw `MapKey`; screen captures render the same production control/scene routes at 1280x720 and 1920x1080.

## Decisions
- **DEC-001** [FR-004] [FR-005]: Persist shell and game-specific preferences as deterministic versioned values and record-only persistence requests; claim request evidence, not unavailable file durability.
- **DEC-002** [FR-002] [FR-003]: Extend the existing generic shell only through game-supplied extra rows/routes; retain one shared main/settings/pause implementation and one pointer-aware host.
- **DEC-003** [FR-006] [FR-007]: Treat Stats as a pure snapshot screen. Chart data is product-owned and rendered with typed Controls chart widgets using stable series identities/colors.
- **DEC-004** [FR-008]: Store the selected difficulty in `MetaProfile` and copy its complete scaling record into `RunState` exactly once at `StartRun`.
- **DEC-005** [FR-001]: Render HUD geometry through production `Render` with responsive placement derived from the requested size, and expose deterministic layout evidence for both representative sizes.

## Accepted Deferrals
No accepted deferrals recorded.

## Remaining Ambiguity
No blocking ambiguity remains.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd checklist --work 008-m7-ui-menus-stats`.
