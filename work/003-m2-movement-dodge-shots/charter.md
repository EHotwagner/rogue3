---
schemaVersion: 1
workId: 003-m2-movement-dodge-shots
title: "M2 movement, dodge, and shots"
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

# M2 movement, dodge, and shots Charter

## Identity
- Work id: `003-m2-movement-dodge-shots`
- Lifecycle stage: charter
- Status: chartered

## Principles
- Preserve the 120 Hz fixed-step, input-edge, seeded-RNG, logical-coordinate, shell, and governance contracts established by M0/M1.
- Keep simulation transitions pure and deterministic; use source-shaped FS.GG.Game.Core collision/ballistics primitives where their public surface fits.
- Bind acceptance to real Release tests and production-route bounded-headless workloads with exact structural counters.

## Scope Boundaries
- In: player acceleration/friction and circular obstacle movement, dodge state/timing, stat-derived live projectiles, multishot, range/lifetime, wall bounce, pierce accounting, and homing termination.
- Out: damage, health, knockback, enemy AI, room generation, native gamepad host work, package upgrades, and remote PR/merge evidence.

## Policy Pointers
- SDD policy comes from `.fsgg/sdd.yml` and `.fsgg/agents.yml`.
- Governance files are optional compatibility pointers and are not evaluated by this command.

## Lifecycle Notes
- One bounded roadmap item: every M2 bullet plus upstream acceptance scenarios 4 and 10.
- M0/M1 accepted no deferrals targeting M2. Their host-input release obligations remain visible but are not widened into this item.
- Next lifecycle action: `fsgg-sdd specify --work 003-m2-movement-dodge-shots`.
