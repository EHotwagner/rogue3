---
schemaVersion: 1
workId: 005-m4-procedural-floor-generation
title: M4 Procedural Floor Generation
stage: specify
changeTier: tier1
status: specified
publicOrToolFacingImpact: true
---

# M4 Procedural Floor Generation Specification

## User Value
Seeded floors feel varied and curated while replaying exactly, and descent safely preserves the run build.

## Scope
- SB-001: M4 generation, interior data, doors, hidden-room reveal, fixtures, trapdoor, and descent only.

## Non-Goals
- SB-002: Enemy AI/state machines (M5), room traversal/locks (M6), drops (M7), items (M8), screens/meta (M9+), and rendering are excluded.

## User Stories
- US-001 (P1): As a delver, I receive a deterministic connected floor with meaningful special rooms.
- US-002 (P1): As a delver, bombing secrets and descending changes the whole floor atomically.

## Acceptance Scenarios
- AC-001 [US-001] [FR-001] [FR-002] [FR-003] [FR-004]: Given one run seed and floor index, repeated generation yields equal room budgets, cells, types, templates, enemy anchors, doors, and RNG continuation; total room count including hidden rooms is 8..20 and meets the target, required special rooms exist, and every carved door joins orthogonal cells.
- AC-002 [US-002] [FR-005]: Given a hidden secret link, reveal commits reciprocal open doors, both graph edges, visibility, and pending-link removal in one value transition.
- AC-003 [US-002] [FR-006]: Given a boss clear and descent, exactly one trapdoor exists, the next seed/floor replaces room-local entities and state, and player items/stats/health/currencies persist.

## Functional Requirements
- FR-001: Generation MUST derive `MapGen.floorSeed runSeed floorIndex`, create a floor-local `Rng`, thread every returned continuation through bounded generation and fixtures, and never advance `DropRng`. (Stories: US-001; Acceptance: AC-001)
- FR-002: The product MUST compute `round(7 + 1.6*floorIndex + n)` for inclusive `n=0..2`, clamp 8..20, bound whole-walk retries to 32, and use a deterministic connected orthogonal fallback if the framework returns a partial walk. (Stories: US-001; Acceptance: AC-001)
- FR-003: Floors MUST assign Start, far/dead-end Boss and Treasure, Shop from floor 2, Secret adjacent to at least two rooms, optional SuperSecret from floor 3 adjacent to one room, and data templates/enemy anchors spending threat budget `6 + 2*floorIndex`. (Stories: US-001; Acceptance: AC-001)
- FR-004: Layout generation MUST carve only reciprocal orthogonal room doors and MUST leave treasure pedestal contents, boss reward stock, shop prices/key locks, and room-clear door opening to M5. (Stories: US-001; Acceptance: AC-001)
- FR-005: Bomb reveal MUST atomically expose Secret or SuperSecret, add reciprocal Open door records and graph adjacency, and make repeated/invalid reveals idempotent. (Stories: US-002; Acceptance: AC-002)
- FR-006: Boss clear MUST add exactly one trapdoor; `DescendFloor` MUST increment the index, regenerate from the next seed, enter Start, carry player build/health/currencies, and discard rooms, enemies, bullets, bombs, pickups/shops, doors, and clear state. (Stories: US-002; Acceptance: AC-003)

## Ambiguities
No material ambiguities recorded.

## Public Or Tool-Facing Impact
- Adds product-owned floor value types/functions and Model/Msg state, plus a sixth representative `floor-generation` workload with exact 20-room maximum scale.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd clarify --work 005-m4-procedural-floor-generation`.
