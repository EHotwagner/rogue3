---
schemaVersion: 1
workId: 002-m1-input-twin-stick-control
title: M1 Input Twin Stick Control
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

# M1 Input Twin Stick Control Charter

## Identity
Deliver Hollow Depths M1 input and twin-stick control as one bounded contracted change, including the source specification's acceptance scenario #9.

## Principles
- Keep input snapshots and simulation transitions pure, deterministic, and replayable.
- Sample edge-trigger state at the fixed-step Tick boundary; advance previous input only after simulation consumes the current snapshot.
- Preserve M0's 120 Hz fixed step, five-step guard, split RNG streams, logical 1280x720 canvas, shell, host, and governance behavior.
- Declare performance workloads and structural budgets before implementation, and use real Release test/evidence runs.

## Scope Boundaries
- In: current/previous input snapshots, per-step edge derivation, keyboard and pointer adapters, gamepad snapshot values, decoupled move/aim vectors, held-fire cadence, eight-way arrow aim, 360-degree mouse/right-stick aim, AC #9 regression coverage, performance evidence, and roadmap evidence.
- Out: M2 movement acceleration/collision/dodge/projectile lifetime and full projectile entities; later game-shell rebinding and persistence; adding an unpinned framework gamepad host API.
- Audit result: M0 recorded no accepted clarification deferrals targeting M1, so there is no inherited deferral to discharge.

## Policy Pointers
- Honor constitution principles I, III, V, VI, VII, and VIII; `.fsgg/sdd.yml` and `.fsgg/agents.yml` govern lifecycle projections.
- Governance remains optional at SDD ship; existing product governance checks remain mandatory in the product Verify route.

## Lifecycle Notes
- Tier 1: the product model/message/input contract and pointer-aware shell host mapping change together with tests and docs.
- No remote is configured; PR or protected-branch evidence must remain explicitly unavailable rather than synthesized.
- Next lifecycle action: `fsgg-sdd specify --work 002-m1-input-twin-stick-control`.
