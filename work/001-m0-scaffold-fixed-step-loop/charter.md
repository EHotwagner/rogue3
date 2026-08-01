---
schemaVersion: 1
workId: 001-m0-scaffold-fixed-step-loop
title: Hollow Depths M0 scaffold and fixed-step loop
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

# Hollow Depths M0 scaffold and fixed-step loop Charter

## Identity
Establish the Hollow Depths product-owned MVU skeleton and the deterministic M0
simulation foundation described by roadmap M0 and upstream acceptance scenario 8.

## Principles
- Keep `update` and `view` pure: no ambient clock or randomness.
- Use the pinned `FS.GG.Game.Core` `FixedStep` and `Rng` contracts rather than local substitutes.
- Preserve the durable generated-product host, shell, evidence, and governance spine.
- Exercise the production `update` + `view` route and retain deterministic structural performance counters.

## Scope Boundaries
In: the M0 `Model`/`Msg`/`update`/`view` skeleton, 120 Hz fixed-step drain with a
five-step frame guard and banked remainder, seeded independent layout/drop RNG
streams, and the logical 1280×720 world-to-screen transform. Out: input controls,
movement/combat, floor generation, interpolation, and all later roadmap milestones.

## Policy Pointers
- Honors constitution principles I, III, V, VI, VII, and VIII.
- Source specification: `docs/roguelike-dungeon-crawler-roadmap.md` and the linked upstream Hollow Depths spec.
- SDD policy comes from `.fsgg/sdd.yml` and `.fsgg/agents.yml`; Governance remains an optional handoff.

## Lifecycle Notes
- Tier 1 model-surface replacement with focused tests and a full product build/test/verify audit.
- The M0 performance route is bounded headless `update` + `view`; live compositor evidence is not claimed.
