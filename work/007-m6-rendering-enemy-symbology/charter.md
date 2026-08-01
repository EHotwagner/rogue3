---
schemaVersion: 1
workId: 007-m6-rendering-enemy-symbology
title: M6 rendering and enemy symbology
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

# M6 rendering and enemy symbology Charter

## Identity
- M6 completes Hollow Depths' room rendering: ordered scene layers, physics-faithful enemy symbols, deterministic pooled particles, and the room-slide camera.

## Principles
- Pure model/update/view boundaries; enemy radius remains identical to the drawn Token radius; deterministic fixed-step effects; bounded production-route scene cost; final raster and legibility inspection.

## Scope Boundaries
- In: all four M6 roadmap rows. Out: M7 HUD/menu/stats work and all later audio, persistence, end-state, and meta-progression work.

## Policy Pointers
- Constitution I, II, IV, V, VI, VII, and VIII; source specification sections 5.2, 7.3-7.4, 8-8.1, and 13.

## Lifecycle Notes
- Tier 1 product rendering expansion. M5 supplied live room entities but deferred their production visual projection to M6; this milestone discharges that seam without taking M7 HUD ownership.
