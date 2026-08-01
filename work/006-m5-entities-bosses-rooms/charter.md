---
schemaVersion: 1
workId: 006-m5-entities-bosses-rooms
title: M5 Entities Bosses and Rooms
stage: charter
changeTier: tier1
status: chartered
policyPointers:
  - .fsgg/sdd.yml
  - .fsgg/agents.yml
  - .fsgg/policy.yml
  - .fsgg/capabilities.yml
  - .fsgg/tooling.yml
---

# M5 Entities Bosses and Rooms Charter

## Identity
- M5 completes Hollow Depths' room-scale content simulation: deterministic enemies, bosses, obstacles, drops, rewards, and shops.

## Principles
- Pure fixed-step transitions; total-order entity processing; isolated LayoutRng and DropRng; bounded maximum-content cost; test every specified timing and gate.

## Scope Boundaries
- In: all nine M5 roadmap rows. Out: M6 rendering/symbology and later UI, audio, persistence, victory, and meta-progression.

## Policy Pointers
- Constitution I, II, IV, V, VI, VII, and VIII; source specification sections 4.9, 4.11, 5.2-5.5, 6, 7.3, 13, and acceptance scenarios 5, 11, 12.

## Lifecycle Notes
- Tier 1 product contract expansion. M4 explicitly deferred room seals, fixture contents, boss reward stock, and shop pricing to this milestone; all are discharged here. No M6+ visuals are introduced.
