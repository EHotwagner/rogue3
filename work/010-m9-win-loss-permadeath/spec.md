---
schemaVersion: 1
workId: 010-m9-win-loss-permadeath
title: M9 Win Loss Permadeath
stage: specify
changeTier: tier1
status: specified
publicOrToolFacingImpact: true
---

# M9 Win Loss Permadeath Specification

Prose status: specified

## User Value
Runs end visibly and fairly with scored victory or permadeath while earned meta-progression survives restart.

## Scope
- SB-001: Floor-6 victory, zero-health game over, run summaries, unlock evaluation, and versioned debounced atomic MetaProfile persistence; no M10 replay work or mid-run saves.

## Non-Goals
- SB-002: M10 replay/determinism acceptance, online score submission, mid-run saves, and new playable characters remain out of scope.

## User Stories
- US-001 (P1): As a player, I see a complete scored result when I die or defeat the final boss, and I can begin a clean run afterward.
- US-002 (P1): As a returning player, I retain milestone unlocks, lifetime stats, settings, and best scores without retaining a dead run.

## Acceptance Scenarios
- AC-001 [US-001] [FR-001]: Given a floor-6 boss is alive, when production boss damage defeats it, then exactly one Victory summary is retained, the run is discarded, the victory unlock is awarded, and persistence is requested.
- AC-002 [US-001] [FR-002]: Given an active run at one total half-heart, when production simulation applies lethal damage, then exactly one GameOver summary is retained, the run is discarded, eligible end-run unlocks are awarded, and persistence is requested.
- AC-003 [US-001] [FR-003]: Given known run statistics, when either terminal path resolves, then the score equals the §11 formula and is recorded as the maximum for that seed without affecting unlock eligibility.
- AC-004 [US-002] [FR-004]: Given a caller-owned temporary profile directory, when multiple writes occur inside the debounce window, then only the latest profile becomes durable through temp-file replacement; boot load restores supported JSON and safely falls back for absent, malformed, or unsupported versions.
- AC-005 [US-001] [FR-005]: Given a terminal result, when the production shell renders and routes effects, then GameOver/Victory content and score are visible and the correct audio transition is requested without simulation continuing.

## Functional Requirements
- FR-001: Production floor-6 boss defeat MUST create Victory, award the victory unlock, retain a RunSummary, discard run state, and request MetaProfile persistence exactly once. (covers AC-001)
- FR-002: Production simulation MUST convert zero total half-hearts into GameOver at the end of the step, evaluate milestone unlocks, retain a RunSummary, discard run state, and request persistence exactly once. (covers AC-002)
- FR-003: End-run processing MUST tally the §11 score, update lifetime stats and best score per seed, and keep score independent from milestone unlock eligibility. (covers AC-003)
- FR-004: The host MUST load versioned MetaProfile JSON at boot and debounce atomic temp-file-plus-rename writes, with safe absent, malformed, and unsupported-version fallback. (covers AC-004)
- FR-005: The result presentation MUST expose GameOver/Victory score and unlocks through the production scene/shell and request the matching terminal audio transition while the simulation remains stopped. (covers AC-005)

## Ambiguities
No material ambiguities recorded.

## Public Or Tool-Facing Impact
- Adds product-owned version-1 MetaProfile JSON and terminal result contracts; no framework API changes.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd clarify --work 010-m9-win-loss-permadeath`.
