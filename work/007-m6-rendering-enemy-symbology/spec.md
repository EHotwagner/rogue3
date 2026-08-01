---
schemaVersion: 1
workId: 007-m6-rendering-enemy-symbology
title: M6 Rendering Enemy Symbology
stage: specify
changeTier: tier1
status: specified
publicOrToolFacingImpact: true
---

# M6 Rendering Enemy Symbology Specification

Prose status: specified

## User Value
Hollow Depths renders combat rooms with readable enemy symbols, deterministic effects, and a clear room-transition camera.

## Scope
- SB-001: M6 rendering only: ordered world layers, Enemy-to-Token mapping, accepted Size-only legibility warning, pooled particles capped at 600, and a 0.35-second room slide; no M7 UI/menu/stat work.

## Non-Goals
- SB-002: Do not implement later lifecycle commands or Governance enforcement in this specification.

## User Stories
- US-001 (P1): As a delver, I can read every enemy kind, facing, health, and threat while dodging.
- US-002 (P1): As a delver, I see combat, effects, and overlays in a stable painter's order.
- US-003 (P1): As a delver, room changes slide clearly and particle-heavy combat remains bounded.

## Acceptance Scenarios
- AC-001 [US-002] [FR-001]: Given a representative combat model, production `View.view` emits the eleven named layers in exact back-to-front order, with HUD and overlays last.
- AC-002 [US-001] [FR-002] [FR-003]: Given all eight enemy kinds at live roster radii, the render-owned ChannelMap emits `Grammar.Token` symbols whose only linter finding is the accepted Size warning and whose raster is readable at the declared logical size.
- AC-003 [US-003] [FR-004]: Given more than 600 requested particles, production update retains exactly the newest 600, advances lifetime/fade deterministically, and production view renders the bounded pool in the particle layer.
- AC-004 [US-003] [FR-005]: Given a room transition, production update and view expose a deterministic slide from one room-width offset to rest, in progress before 0.35 seconds and exactly settled at 0.35 seconds.
- AC-005 [US-001] [US-002] [US-003] [FR-006]: Given representative maximum M6 content, runner-issued production update/view evidence exact-gates enemy symbols, 600 particles, eleven layers, and camera transition cost within existing timing and scene budgets.

## Functional Requirements
- FR-001: Production view MUST compose floor background, decals, obstacles, pickups, shadows, enemies, player, projectiles, particles, HUD, and screen overlays in that exact back-to-front order. (Stories: US-002; Acceptance: AC-001)
- FR-002: A render-owned `EnemyActor -> Token` ChannelMap MUST use `Symbology.token`, preserve exact physics radius, map Enemy faction, quantise Threat to four tiers, normalize Health by max HP, and distinguish all eight kinds through Klass and Sigil. (Stories: US-001; Acceptance: AC-002)
- FR-003: The legibility gate MUST reject every Error and every Warning except the explicitly accepted Size channel overload caused by physics-faithful radii. (Stories: US-001; Acceptance: AC-002)
- FR-004: Production update MUST maintain a deterministic particle pool capped at 600, with colored circle/quad particles carrying velocity, lifetime, age, and fade; production view MUST consume the bounded live pool. (Stories: US-003; Acceptance: AC-003)
- FR-005: Production update and view MUST model a room-transition camera slide with a 0.35-second duration, clamped progress, deterministic direction, and identity transform at rest. (Stories: US-003; Acceptance: AC-004)
- FR-006: Representative bounded-headless performance evidence MUST traverse production update and view with all eight enemy kinds, the 600-particle cap, ordered layer output, and active camera slide, while retaining runner-issued provenance and existing timing/catch-up/scene-node budgets. (Stories: US-001, US-002, US-003; Acceptance: AC-005)

## Ambiguities
No material ambiguities recorded.

## Public Or Tool-Facing Impact
- Adds product-owned render projection, particle, camera, inventory, evidence, and test contracts; no framework API changes.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd clarify --work 007-m6-rendering-enemy-symbology`.
