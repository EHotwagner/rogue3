---
schemaVersion: 1
workId: 005-m4-procedural-floor-generation
title: M4 Procedural Floor Generation
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

# M4 Procedural Floor Generation Charter

## Identity
- Work id: `005-m4-procedural-floor-generation`
- Lifecycle stage: charter
- Status: chartered

## Principles
- Determinism includes graph, content, fixtures, prices, and RNG continuation; no ambient randomness.
- Hidden-room reveal and descent are whole-value transitions with no half-open graph state.

## Scope Boundaries
- Own only roadmap M4 and preserve M0-M3 behavior; M5+ behavior and rendering remain outside this work.

## Policy Pointers
- SDD policy comes from `.fsgg/sdd.yml` and `.fsgg/agents.yml`.
- Governance files are optional compatibility pointers and are not evaluated by this command.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd specify --work 005-m4-procedural-floor-generation`.
