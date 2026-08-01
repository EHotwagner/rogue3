---
schemaVersion: 1
workId: 006-m5-entities-bosses-rooms
title: M5 Entities Bosses Rooms
stage: specify
changeTier: tier1
status: specified
publicOrToolFacingImpact: true
---

# M5 Entities Bosses Rooms Specification

Prose status: specified

## User Value
Rooms now contain deterministic enemies, bosses, hazards, drops, rewards, and shops.

## Scope
- SB-001: All nine M5 roadmap rows; no M6 rendering or later systems.

## Non-Goals
- SB-002: Do not implement later lifecycle commands or Governance enforcement in this specification.

## User Stories
- US-001 (P1): As a delver, I fight readable deterministic enemy and boss patterns in sealed rooms.
- US-002 (P1): As a delver, I receive deterministic drops, rewards, and shop choices without duplicates.
- US-003 (P1): As a delver, obstacles and floor scaling change tactics predictably.

## Acceptance Scenarios
- AC-001 [US-001] [FR-001] [FR-002]: Given each roster member and boss, fixed-step replay produces its exact timed state/pattern sequence, including Charger early dash termination and boss phase changes.
- AC-002 [US-001] [FR-003] [FR-004]: Given an uncleared room, doors remain sealed until the final enemy dies, then clear/open and exactly one weighted DropRng roll commit in that same step.
- AC-003 [US-003] [FR-005] [FR-006]: Given deeper floors and every obstacle kind, HP/bullet scales match the formulas and collision, destruction, hazard, and fly-over behavior match the specification.
- AC-004 [US-002] [FR-007]: Given identical run seeds but different combat/drop draws, treasure and boss item ids stay identical and no item repeats in one run.
- AC-005 [US-002] [FR-008]: Given a generated floor shop, its three contents, prices, and locks are deterministic; successful purchase empties one slot, rejected purchase preserves it, and it never restocks.
- AC-006 [US-001] [US-002] [US-003] [FR-009]: Given the exact maximum M5 content workload, the production update and view route stays within declared structural and timing budgets with runner-issued provenance.

## Functional Requirements
- FR-001: Implement Grub, Maggot, Spitter, Fly, Charger, Turret, Caster, and Brute definitions and deterministic state machines with every §5.2 timing, radius, HP, speed, threat, damage, and bounded split parameter. (Stories: US-001; Acceptance: AC-001)
- FR-002: Implement Gnawer, Hollow Choir, and Maw phases plus declarative emitters carrying count, arc, speed, spin, cadence, homing, and gap data. (Stories: US-001; Acceptance: AC-001)
- FR-003: Combat/boss entry MUST seal doors; only the last-enemy transition clears the room, opens clear/boss seals, and creates boss reward/trapdoor where applicable. (Stories: US-001; Acceptance: AC-002)
- FR-004: Room-clear, pot, and tinted-rock weighted tables MUST draw only from threaded DropRng with the exact §4.9 weights and at most one roll per claimed destruction. (Stories: US-002; Acceptance: AC-002)
- FR-005: Enemy HP MUST scale by `1 + 0.12*floorIndex`, bullets by `1 + 0.05*floorIndex`, and room threat budget remain `6 + 2*floorIndex`, with deeper elite roster gates. (Stories: US-003; Acceptance: AC-003)
- FR-006: Rocks/tinted rocks/pots MUST block grounded movement and shots as specified; destructibles roll once; spikes deal contact damage; pits block grounded movement while shots and flying enemies pass over. (Stories: US-003; Acceptance: AC-003)
- FR-007: Treasure and boss fixtures MUST draw from LayoutRng at generation and remove item ids from a run-scoped available pool so no pedestal, stock, or grant duplicates; exhaustion falls back to consumables. (Stories: US-002; Acceptance: AC-004)
- FR-008: Shops MUST generate three LayoutRng-fixed item/consumable slots with tier-adjusted/fixed prices and optional key locks; purchase empties a slot and shops never restock within a floor. (Stories: US-002; Acceptance: AC-005)
- FR-009: Extend the authored maximum-content workload through production update and view with full M5 live roster/boss/bullets/room content, deterministic counters, frozen digest, runner-issued receipt, Release proof, and independent exact-candidate performance verdict. (Stories: US-001, US-002, US-003; Acceptance: AC-006)

## Ambiguities
No material ambiguities recorded.

## Public Or Tool-Facing Impact
- Adds product-owned entity/content contracts and deterministic pure transitions; no framework API changes.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd clarify --work 006-m5-entities-bosses-rooms`.
