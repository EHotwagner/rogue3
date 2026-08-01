---
schemaVersion: 1
workId: 001-m0-scaffold-fixed-step-loop
title: M0 Scaffold Fixed Step Loop
stage: specify
changeTier: tier1
status: specified
publicOrToolFacingImpact: true
---

# M0 Scaffold Fixed Step Loop Specification

Prose status: specified

## User Value
Hollow Depths has a deterministic, buildable MVU foundation on which later gameplay milestones can evolve without replacing the host or sacrificing replayability.

## Scope
- SB-001: M0 scaffold, fixed 120 Hz loop, split RNG streams, and logical 1280x720 transform only.

## Non-Goals
- SB-002: Input, movement, combat, floor generation, rendering detail, and persistence are later milestones.
- SB-003: Render interpolation is a documented stretch goal; M0 renders the latest model state.

## User Stories
- US-001 (P1): As a developer, I can advance Hollow Depths deterministically through a pure MVU update/view route.
- US-002 (P1): As a developer, I can seed a run once and use independent layout and drop random streams.
- US-003 (P1): As a player, I see a stable logical 1280×720 frame regardless of output size.

## Acceptance Scenarios
- AC-001 [US-001] [FR-001]: Given the product boots, when `init`, `update`, and `view` are invoked, then the pure product-owned skeleton returns a model, commands, and a deterministic scene without ambient time or randomness.
- AC-002 [US-001] [FR-002]: Given accumulator zero and fixed dt 1/120, when `Tick 0.033` is processed, then exactly three whole steps run and the positive sub-step remainder is banked; when exact 1/30 second is processed, exactly four steps run within floating-point epsilon.
- AC-003 [US-001] [FR-002]: Given a one-second stall, when it is processed, then `FixedStep.drainWith` advances at most five steps and banks a remainder below one fixed interval instead of retaining unbounded catch-up debt. This is upstream acceptance scenario 8.
- AC-004 [US-002] [FR-003]: Given the same run seed twice, when the model initializes, then both models contain equal `LayoutRng` and `DropRng` states; advancing one stream does not change the other.
- AC-005 [US-003] [FR-004]: Given logical and output points, when the world-to-screen transform is applied, then 1280×720 maps uniformly with centered letterboxing and an inverse round-trip recovers the logical point within epsilon.

## Functional Requirements
- FR-001: The product MUST expose a pure `Model`/`Msg`/`init`/`update`/`view` skeleton through the existing generated host composition. (covers AC-001)
- FR-002: `Tick` MUST use `FixedStep.drainWith (5.0 * FIXED_DT) FIXED_DT` at 120 Hz, advance only whole steps, and retain the returned banked accumulator. (covers AC-002, AC-003)
- FR-003: The model MUST seed `FS.GG.Game.Core.Rng` once and derive independent `LayoutRng` and `DropRng` values via `Rng.split`, with no ambient randomness. (covers AC-004)
- FR-004: The product MUST define a logical 1280×720 coordinate system and a pure centered uniform world-to-screen transform with an inverse mapping. (covers AC-005)

## Ambiguities
No material ambiguities recorded.

## Public Or Tool-Facing Impact
- Tier 1 replacement of the scaffold's public product model surface and gameplay scene projection.
- Existing durable shell, host, evidence commands, and governance behavior remain compatible.

## Lifecycle Notes
- Headless tests exercise the real update and view functions; no live-compositor claim is made.
- AC-002 deliberately distinguishes decimal `0.033` (three steps) from exact `1.0/30.0` (four steps); upstream AC #8's approximate notation is clarified without changing its exact 1/30-second requirement.
