---
schemaVersion: 1
workId: 009-m8-audio
title: M8 Audio
stage: specify
changeTier: tier1
status: specified
publicOrToolFacingImpact: true
---

# M8 Audio Specification

Prose status: specified

## User Value
Players receive responsive event cues and context-appropriate music whose requested values remain deterministic, ordered, and testable without audio hardware.

## Scope
- SB-001: M8 §10 requested audio only; no M9 terminal-state implementation or real speaker-playback claim.

## Non-Goals
- SB-002: Do not add M9 terminal-state flow, a profile-file backend, WAV assets, framework package changes, or claims that a physical speaker produced sound.

## User Stories
- US-001 (P1): As a player, I receive the specified sound cue when a gameplay event occurs.
- US-002 (P1): As a player, I hear at most one context music loop and transitions replace it in a deterministic stop-then-play order.
- US-003 (P1): As a player, my restored/live volume and mute choices reach the audio sink with safe normalized values.
- US-004 (P1): As a maintainer, I can prove exact requested audio through the production update/host recording route without sound hardware.

## Acceptance Scenarios
- AC-001 [US-001] [FR-001]: Given representative production transitions for fire, hit, death, dodge, pickups, explosion, doors, boss states, and floor descent, when `AudioCues.forTransition` runs, then it emits the exact §10 `PlaySfx` ids and volumes in deterministic event order.
- AC-002 [US-002] [FR-002]: Given startup, floor, shop, boss, descend, and run-end context changes, when the host extracts requested effects, then startup establishes one loop and every replacement is exactly `StopMusic` followed by one looping `PlayMusic` for the new context.
- AC-003 [US-003] [FR-003]: Given finite out-of-range values, NaN, mute, and unmute transitions, when requests cross `Audio.interpret`, then carried volume is in `[0,1]`, mute requests `0.0`, and unmute restores the clamped configured master volume.
- AC-004 [US-004] [FR-004]: Given `interactiveHost.Init` and production `Update` transitions, when `ViewerEffect.PlayAudio` batches are flattened and interpreted, then `AudioEvidence.Requested` equals the expected ordered values and no device/speaker result is claimed.

## Functional Requirements
- FR-001: The product MUST map every §10 gameplay event to its exact product-owned `SoundId` and normalized design volume, preserving multiple simultaneous cues in deterministic order. (Stories: US-001; Acceptance: AC-001)
- FR-002: The product MUST request exactly one looping music track for the active title, floor, shop, boss, or run-end context and MUST emit `StopMusic` before each replacement. (Stories: US-002; Acceptance: AC-002)
- FR-003: Startup and settings transitions MUST request a master volume clamped to `[0,1]`; mute MUST request `0.0`, and unmute MUST restore the configured clamped volume. (Stories: US-003; Acceptance: AC-003)
- FR-004: Audio MUST remain pure requested values from production update through `ViewerEffect.PlayAudio` and `Audio.interpret` into `AudioEvidence.Requested`, with no device access in update and no speaker-playback acceptance claim. (Stories: US-004; Acceptance: AC-004)

## Ambiguities
No material ambiguities recorded.

## Public Or Tool-Facing Impact
- Tier 1 product cue behavior and test/evidence expansion over already-captured `FS.GG.Audio.Core` and Host surfaces; no framework API or package pin changes.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd clarify --work 009-m8-audio`.
