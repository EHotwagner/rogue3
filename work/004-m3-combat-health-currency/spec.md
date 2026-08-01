---
schemaVersion: 1
workId: 004-m3-combat-health-currency
title: M3 combat health and currency
stage: specify
changeTier: tier1
status: specified
publicOrToolFacingImpact: true
---

# M3 combat health and currency Specification

Prose status: specified

## User Value
Complete deterministic Hollow Depths combat, health, stat stacking, and currency play.

## Scope
- SB-001: Implement every M3 roadmap bullet, AC 3, AC 6, AC 11, and related scenarios 19 and 20 while preserving M0-M2.

## Non-Goals
- SB-002: Do not implement later lifecycle commands or Governance enforcement in this specification.

## User Stories
- US-001 (P1): As a user, I can complete deterministic Hollow Depths combat, health, stat stacking, and currency play.

## Acceptance Scenarios
- AC-001 [US-001] [FR-001]: Given M3 combat health and currency is available, when the user exercises it, then they can complete deterministic Hollow Depths combat, health, stat stacking, and currency play.

## Functional Requirements
- FR-001: One fixed-step SpatialGrid.build 64.0 combat pipeline resolves shots, enemies, bullets, contacts, bombs, health, stats, shops, caps, and death state with focused deterministic tests and exact representative performance evidence; no M4+ generation or M9 transition. (Stories: US-001; Acceptance: AC-001)

## Ambiguities
No material ambiguities recorded.

## Public Or Tool-Facing Impact
- This specification is an SDD lifecycle artifact and command-report contract input.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd clarify --work 004-m3-combat-health-currency`.
