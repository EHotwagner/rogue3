---
schemaVersion: 1
workId: 008-m7-ui-menus-stats
title: M7 Ui Menus Stats
stage: specify
changeTier: tier1
status: specified
publicOrToolFacingImpact: true
---

# M7 Ui Menus Stats Specification

Prose status: specified

## User Value
Players can read run state, navigate the shared shell, configure controls/display/game preferences, inspect run and lifetime charts, and start a run with deterministic difficulty.

## Scope
- SB-001: M7 HUD, generic shell composition, pure persistent MetaProfile preference requests, stats/charts, and difficulty latching; no M8 audio playback or M9 profile-file backend/end-state flow.

## Non-Goals
- SB-002: Do not implement later lifecycle commands or Governance enforcement in this specification.

## User Stories
- US-001 (P1): As a player, I can read health, resources, active charge, map position, and floor identity without obscuring the room.
- US-002 (P1): As a player, I can navigate the shared shell, pause, change display and control bindings, and continue with those values restored.
- US-003 (P1): As a player, I can change game preferences live and know that a persistence request was emitted for the profile.
- US-004 (P2): As a player, I can inspect deterministic run and lifetime KPIs, a depth histogram, and damage-per-floor lines.
- US-005 (P1): As a player, I can choose difficulty for my next run without changing the scaling of a run already in progress.

## Acceptance Scenarios
- AC-001 [US-001] [FR-001]: Given a representative run at 1280x720 and 1920x1080, when the HUD renders, then hearts, two-digit coins/keys/bombs, charge, discovered minimap rooms/current room, and floor name are present inside their responsive regions without overlap.
- AC-002 [US-002] [FR-002] [FR-003]: Given the interactive host at its default size, when real retained-route clicks open Config and activate a rebind row and raw keys complete capture/pause/resume, then the shell transitions and rebound held key down/ticks/up behavior are observable; changing to 1920x1080 preserves semantic hit geometry.
- AC-003 [US-002] [FR-004]: Given display and keymap changes, when settings persistence values are encoded and decoded, then resolution, fullscreen mode, and bindings round-trip deterministically; the evidence claims requested values only, not unavailable profile-file durability.
- AC-004 [US-003] [FR-005]: Given difficulty, volume/mute, or screen-shake changes, when the setting changes, then it applies immediately to MetaProfile and emits one versioned profile persistence request with volume clamped to [0,1].
- AC-005 [US-004] [FR-006] [FR-007]: Given run and lifetime snapshots, when Stats opens and scope changes, then four KPI tiles, five depth buckets, and dealt/taken damage series render deterministically with stable colors and no simulation advance.
- AC-006 [US-005] [FR-008]: Given Hard is selected before StartRun, when the run starts and Settings changes to Easy mid-run, then active scaling remains enemyHpScale 0.18, postHitInvuln 0.55, dropNothingWeight 55, one extra elite, and no post-boss heal; the next run latches Easy.

## Functional Requirements
- FR-001: The view MUST render the §9 hearts, currency, active-charge, minimap, and floor-name HUD responsively at representative logical sizes. (Stories: US-001; Acceptance: AC-001)
- FR-002: The product MUST parameterize the generic FS.GG game shell as HOLLOW DEPTHS with Start/Config/Exit, Esc pause/resume, display settings, and stable action-catalog key rebinding rather than authoring a second shell. (Stories: US-002; Acceptance: AC-002)
- FR-003: The real `InteractiveAppHost` route MUST prove bound pointer activation, raw-key capture, key-down/two-fixed-ticks/key-up behavior, and equivalent semantic activation after a logical-resolution change. (Stories: US-002; Acceptance: AC-002)
- FR-004: Display and keymap settings MUST round-trip in deterministic versioned values and remain explicit about the boundary between a request/codec proof and host filesystem durability. (Stories: US-002; Acceptance: AC-003)
- FR-005: Difficulty, master volume/mute, and screen shake MUST apply live to `MetaProfile` and emit a versioned record-only profile persistence request; volume MUST clamp to [0,1]. (Stories: US-003; Acceptance: AC-004)
- FR-006: Run statistics MUST include depth, kills by type, items, coins, elapsed seconds, dealt/taken totals, per-floor damage, death cause, and character, and lifetime profile statistics MUST expose runs, deepest floor, wins/win rate, kills, death causes, and depth history. (Stories: US-004; Acceptance: AC-005)
- FR-007: The Stats screen MUST render four KPI tiles, the five-bucket run-depth histogram, and stable-color dealt/taken damage-per-floor series from immutable snapshots, with a scope toggle and no physics advance. (Stories: US-004; Acceptance: AC-005)
- FR-008: StartRun MUST latch the §12 Easy/Normal/Hard scaling table into run state, and later preference changes MUST affect only the next run. (Stories: US-005; Acceptance: AC-006)

## Ambiguities
No material ambiguities recorded.

## Public Or Tool-Facing Impact
- Tier 1 product model/message/view and host composition changes; no framework API or package-version change.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd clarify --work 008-m7-ui-menus-stats`.
