---
schemaVersion: 1
workId: 003-m2-movement-dodge-shots
title: M2 Movement Dodge Shots
stage: specify
changeTier: tier1
status: specified
publicOrToolFacingImpact: true
---

# M2 Movement Dodge Shots Specification

Prose status: specified

## User Value
Hollow Depths movement accelerates and slides safely while dodge and fully bounded stat-derived shots create deterministic twin-stick combat motion.

## Scope
- SB-001: Implement all M2 roadmap bullets: movement, circular room/obstacle collision, dodge timing, live projectile creation and guaranteed termination.
- SB-002: Preserve M0/M1 fixed-step, input, RNG, logical-coordinate, shell, and governance behavior.
- SB-003: Expose only a deterministic enemy-hit id seam for pierce accounting; M3 owns damage, health, knockback, and hit feedback.

## Non-Goals
- SB-004: Do not implement M3 combat/health, later enemy/room systems, package upgrades, native gamepad polling, or a bespoke host.

## User Stories
- US-001 (P1): As a player, I can accelerate, coast briefly, and slide along walls at equal cardinal and diagonal speed.
- US-002 (P1): As a player, I can commit to a cooldown-gated invulnerable dodge that temporarily locks firing.
- US-003 (P1): As a player, I can fire stat-derived single or centered multishot projectiles whose bounce, pierce, homing, and range always terminate.

## Acceptance Scenarios
- AC-001 [US-001] [FR-001] [FR-002]: Given cardinal or diagonal movement at 120 Hz, when input is held and released, then velocity approaches the clamped 240 px/s target by 2400 px/s², diagonal magnitude equals cardinal magnitude, speed never exceeds the clamp, and zero-input friction removes 3000 px/s² without overshoot.
- AC-002 [US-001] [FR-003]: Given the radius-13 player approaches room bounds or an AABB obstacle diagonally, when a fixed step moves it, then X and Y resolve independently and the unblocked axis continues without penetration or tunneling at the supported player displacement.
- AC-003 [US-002] [FR-004]: Given dodge is ready, when Dodge is pressed, then the player receives a 460 px/s impulse along move or facing, has 0.40 s i-frames, rolls for 0.45 s, cannot fire during i-frames, and cannot roll again until 0.90 s from activation.
- AC-004 [US-003] [FR-005] [FR-006]: Given multishot 3, right aim, and 18 degree spread, when one fire event occurs, then exactly three live shots spawn at -9, 0, and +9 degrees before 0.25 player-velocity inheritance, each carrying the current damage, fire rate, speed, range, radius, knockback, pierce, bounce, and homing stats.
- AC-005 [US-003] [FR-007] [FR-008]: Given shots with range, bounce, pierce, and homing values, when fixed steps integrate them against bounds and ordered enemy-hit ids, then age/range, depleted wall bounces, the pierce+1th distinct enemy hit, and finite homing turn caps remove or bound them deterministically; every shot terminates by range.
- AC-006 [US-001] [US-002] [US-003] [FR-009]: Given the M2 candidate, when focused/full Release, generated Test/Verify, and representative performance routes run, then M0/M1 behavior stays green and structural workload scales remain exact and within declared budgets.

## Functional Requirements
- FR-001: The simulation MUST normalize finite move input and move velocity toward a speed-stat-derived target using bounded acceleration or friction deltas at fixedDt. (Stories: US-001; Acceptance: AC-001)
- FR-002: Effective movement speed MUST clamp to 120..540 px/s and final non-roll velocity MUST not exceed that effective speed. (Stories: US-001; Acceptance: AC-001)
- FR-003: Player movement MUST resolve a radius-13 circle against room bounds and stable-order AABB obstacles by X then Y while preserving the unblocked axis. (Stories: US-001; Acceptance: AC-002)
- FR-004: Dodge MUST apply 460 px/s along move or facing, last 0.45 s, grant 0.40 s i-frames, lock fire during that window, and enforce a 0.90 s start-to-start cooldown. (Stories: US-002; Acceptance: AC-003)
- FR-005: Fire cadence and each live shot MUST derive from current player damage, fireRate, shotSpeed, range, shotRadius, knockback, multishot, pierce, bounce, and homing stats, adding 0.25 current player velocity. (Stories: US-003; Acceptance: AC-004)
- FR-006: Multishot MUST place N directions evenly over a centered 18 degree fan, with one shot exactly on aim and multishot 3 at -9, 0, +9 degrees. (Stories: US-003; Acceptance: AC-004)
- FR-007: Each shot MUST track age, distance, remaining bounce and pierce state, and distinct enemy ids already hit; it MUST expire after age exceeds range, leaving bounds with no bounce, or consuming pierce+1 distinct enemy hits. (Stories: US-003; Acceptance: AC-005)
- FR-008: Homing MUST turn toward the nearest stable-order live target by at most homing*360 degrees/second without changing finite speed magnitude, and all projectile paths MUST remain range-bounded. (Stories: US-003; Acceptance: AC-005)
- FR-009: The change MUST preserve M0/M1 deterministic fixed-step/input/RNG/coordinate and host/governance contracts and MUST publish exact representative Release/performance evidence. (Stories: US-001; Acceptance: AC-006)

## Ambiguities
- AMB-001: Does fire lockout cover only the 0.40 s i-frame window or the full 0.45 s roll duration?
- AMB-002: Does pierce zero mean destruction on the first enemy hit, and must repeat overlap with the same enemy spend pierce again?
- AMB-003: How should bounce and pierce accounting compose without implementing M3 damage?
- AMB-004: What collision helper boundary is appropriate for player motion versus fast shots?

## Public Or Tool-Facing Impact
- This specification is an SDD lifecycle artifact and command-report contract input.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd clarify --work 003-m2-movement-dodge-shots`.
