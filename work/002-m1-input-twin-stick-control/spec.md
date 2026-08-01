---
schemaVersion: 1
workId: 002-m1-input-twin-stick-control
title: M1 Input Twin Stick Control
stage: specify
changeTier: tier1
status: specified
publicOrToolFacingImpact: true
---

# M1 Input Twin Stick Control Specification

Prose status: specified

## User Value
Players can move and aim independently with keyboard/mouse or gamepad snapshots, hold fire at a deterministic cadence, and have edge-triggered controls register exactly once.

## Scope
- SB-001: Add the M1 input snapshot, keyboard/pointer/gamepad adapters, aim selection, fire cadence, shot-spawn intent, host routing that the pinned APIs can carry, focused tests, performance evidence, and roadmap evidence.
- SB-002: Preserve the M0 120 Hz fixed step, five-step stall guard, banked accumulator, split RNG state, logical 1280x720 canvas, shell navigation, audio/effect boundary, and governance checks.

## Non-Goals
- SB-003: Do not implement M2 acceleration/friction, collision, dodge state, projectile lifetime, range, multishot, bounce, pierce, or homing.
- SB-004: Do not fabricate a native gamepad poll or continuous unpressed-hover callback absent from the pinned InteractiveAppHost package contract; expose pure replayable snapshot/message seams and route every coordinate-bearing pointer interaction the host supplies.

## User Stories
- US-001 (P1): As a player, I can hold movement in one direction and aim/fire in another without either vector overwriting the other.
- US-002 (P1): As a player, I can hold fire and receive deterministic shots at the configured cadence rather than native input-repeat cadence.
- US-003 (P1): As a player, I can use cardinal or diagonal arrow aim and get an exact eight-way direction, while mouse and right-stick aim retain 360-degree vectors.
- US-004 (P1): As a player, I can press an edge action once and have it observed once even when one render Tick drains multiple fixed simulation steps.

## Acceptance Scenarios
- AC-001 [US-004] [FR-001]: Given current keys contain Space and previous keys do not, when one Tick drains two fixed steps, then `PressedThisTick = currentKeys - previousKeys` is visible to only the first step and previous becomes current after simulation; a held second Tick has no new Space edge.
- AC-002 [US-001] [FR-002]: Given A is held and mouse aim is to the player's right, when a fixed step applies input and firing occurs, then player velocity points left while the spawned shot's primary direction points right and its velocity includes 0.25 times the leftward player velocity, matching upstream AC #9.
- AC-003 [US-001] [FR-002]: Given a gamepad snapshot with left stick left and right stick at an independent non-cardinal angle, when a fixed step applies input, then move and aim preserve their independent normalized directions.
- AC-004 [US-003] [FR-003]: Given each valid cardinal or diagonal arrow-key combination, when input is resolved, then aim is one of exactly eight normalized directions; mouse and right-stick non-cardinal vectors remain normalized without eight-way snapping.
- AC-005 [US-002] [FR-004]: Given held fire, a 2.5 shots-per-second rate, and 120 Hz simulation, when 0.8 simulated seconds elapse, then a shot fires immediately and then every 0.4 seconds for exactly three spawn events; release prevents further events and re-press fires immediately.
- AC-006 [US-001] [US-003] [FR-005]: Given live shell gameplay receives keyboard edges or coordinate-bearing pointer interactions, when the host routes them, then both down/up or press/release samples update the product snapshot without changing menu, pause, audio, window, or governance routing.

## Functional Requirements
- FR-001: The model MUST store current and previous input snapshots, derive `PressedThisTick` as `currentKeys - previousKeys` at Tick sampling, expose it to exactly one fixed step, and update previous to current only after drained simulation steps complete. (covers AC-001)
- FR-002: The fixed step MUST resolve keyboard/mouse and gamepad movement and aim independently, normalize movement and analog aim, and produce an AC #9 shot-spawn intent whose velocity is aim-speed plus `0.25 * playerVelocity`. (covers AC-002, AC-003)
- FR-003: Arrow-key aim MUST snap cardinal/two-key diagonal input to eight normalized directions, while mouse cursor-minus-player and right-stick aim MUST preserve normalized 360-degree direction. (covers AC-004)
- FR-004: Held mouse-primary, arrow aim, right trigger, or deflected right stick MUST auto-fire immediately on acquisition and every `1 / 2.5` simulated seconds thereafter, independent of operating-system key repeat. (covers AC-005)
- FR-005: Keyboard and coordinate-bearing pointer host events MUST flow through pure product messages, and a public pure gamepad snapshot seam MUST support left/right sticks and trigger without claiming unavailable native polling. (covers AC-006)

## Ambiguities
- AMB-001: When arrow, right-stick, and mouse aim are simultaneously non-zero, which source has deterministic priority?
- AMB-002: How are edge actions prevented from repeating when one host Tick drains multiple fixed steps?
- AMB-003: How is first held-fire acquisition scheduled relative to the 0.4-second repeat interval?
- AMB-004: How is the source-spec gamepad polling subscription represented when the pinned host has no gamepad field?

## Public Or Tool-Facing Impact
- `Rogue3.Model` gains authored input snapshot, gamepad, aim-source, and shot-spawn value types plus pure resolution helpers and messages.
- The existing InteractiveAppHost remains the live shell host; its raw keyboard seam and coordinate-bearing PointerInteraction fallback feed the product model.
- Product performance intent/evidence definitions and SDD generated views are refreshed; no package version changes are required.

## Lifecycle Notes
- M0 clarification audit found no accepted deferrals aimed at M1.
- Upstream Hollow Depths sections 3, 4.3, 7.3, 7.5, acceptance #9, and roadmap M1 are the product requirements source.
- Next lifecycle action: `fsgg-sdd clarify --work 002-m1-input-twin-stick-control`.
